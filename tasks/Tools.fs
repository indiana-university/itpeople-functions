namespace Tasks

module Tools =

    open Core.Types
    open Core.Json
    open Database.Command
    open Dapper
    open Microsoft.Azure.WebJobs
    open Microsoft.Extensions.Logging
    open Novell.Directory.Ldap

    type ADPath = string
    type ToolName = string
    type ADGroupMember = NetId * ADPath * ToolName
    
    type ToolPersonUpdate =
    | Add of ADGroupMember
    | Remove of ADGroupMember

    let private getAllTools connStr =
        let sql = "SELECT * FROM tools"
        fetch (fun cn -> cn.QueryAsync<Tool>(sql)) connStr

    let private getAllToolUsers connStr (tool:Tool) =
        let sql = """
            SELECT DISTINCT p.netid FROM people p
            JOIN unit_members um ON um.person_id = p.id
            JOIN unit_member_tools umt ON umt.membership_id = um.id
            WHERE umt.tool_id = @Id"""
        let param = {Id=tool.Id}
        fetch (fun cn -> cn.QueryAsync<NetId>(sql, param)) connStr

    // LDAP Stuff

    let private searchBase = "ou=Accounts,dc=ads,dc=iu,dc=edu"
    let private searchFilter dn = 
        sprintf "(memberOf=%s)" dn

    let private memberAttribute netid = 
        let value = sprintf "cn=%s,%s" netid searchBase
        LdapAttribute("member", value)

    let private doLdapAction adUser adsPassword action = 
            let adUser = sprintf """ads\%s""" adUser
            use ldap = new LdapConnection()
            ldap.SecureSocketLayer <- true
            ldap.Connect("ads.iu.edu", 636)
            ldap.Bind(adUser, adsPassword)  
            ldap |> action |> ok

    let private getADGroupMembers adUser adPassword dn =
        let getADGroupMembers' (ldap:LdapConnection) = 
            let sam = "sAMAccountName"
            let list = System.Collections.Generic.List<NetId>()
            let search = ldap.Search(searchBase, 1, searchFilter dn, [|sam|], false)          
            while search.hasMore() do
                search.next().getAttribute(sam).StringValue |> list.Add
            list |> seq
        try
            getADGroupMembers' |> doLdapAction adUser adPassword         
        with exn -> 
            let msg = sprintf "Group member lookup failed for %s:\n%A" dn exn
            error(Status.InternalServerError, msg)

    let private updateADGroup adUser adPassword update =
        let updateADGroup' (ldap:LdapConnection) =
            let (dn, modification) = 
                match update with
                | Add(netid, dn, _) -> dn, LdapModification(LdapModification.ADD, memberAttribute netid)
                | Remove(netid, dn, _) -> dn, LdapModification(LdapModification.DELETE, memberAttribute netid)
            ldap.Modify(dn, modification)
            update
        try
            updateADGroup' |> doLdapAction adUser adPassword |> ignore
            ok ()       
        with exn -> 
            if exn.Message = "No Such Object"
            then ok () // This user isn't in AD. There's nothing we can do about it. Squelch
            else 
                let msg = sprintf "Group modification failed for %A:\n%A" update exn
                error(Status.InternalServerError, msg)

    type ToolPersonUpdateRow = 
      { ChangeType: string; 
        NetId: string;
        ToolName: string;
        ToolPath: string; }

    let private logADGroupUpdate connStr toolPersonUpdate =
        let sql = 
            """with cte (unit_id, unit_name) as
                ( 
                	select u.id, u.name
                	from units u
                	join unit_members um on u.id = um.unit_id
                	join unit_member_tools umt on um.id = umt.membership_id
                	join tools t on t.id = umt.tool_id
                	join people p on p.id = um.person_id
                	where p.netid = @NetId
                	and t.name = @ToolName
                )
                INSERT INTO automationlog_tools (change_type, netid, tool_name, tool_path, unit_id, unit_name)
                SELECT 
                    @ChangeType,
                    @NetId,
                    @ToolName,
                    @ToolPath,
                	string_agg(unit_id, '; '), 
                	string_agg(unit_name, '; ')
                FROM cte
                """
        let param = 
            match toolPersonUpdate with
            | Add(netid, path, name)    -> { NetId=netid; ToolPath=path; ToolName=name; ChangeType="add"; }
            | Remove(netid, path, name) -> { NetId=netid; ToolPath=path; ToolName=name; ChangeType="remove"; }
        execute connStr sql param

    let enqueueTools (log:ILogger) (queue:ICollector<string>) connStr = pipeline {
        let! tools = getAllTools connStr
        tools 
        |> Seq.map serialize 
        |> Seq.iter queue.Add
        tools
        |> Seq.map (fun t -> sprintf "%s: %s" t.Name t.ADPath)
        |> String.concat "\n"
        |> sprintf "Enqueued tool permission updates for: %s"
        |> log.LogInformation
        return ()
    }

    let enqueueAccessUpdates (log:ILogger) (queue:ICollector<string>) toolJson connStr adUser adPassword = pipeline {
         let fetchNetids (tool:Tool) = async {
            let! adPromise = getADGroupMembers adUser adPassword tool.ADPath |> Async.StartChild
            let! dbPromise = getAllToolUsers connStr tool |> Async.StartChild
            let! ad = adPromise
            let! db = dbPromise
            return 
                match (ad, db) with
                | (Ok(adr), Ok(dbr)) -> Ok (tool, adr, dbr) 
                | (Error(ade),_) -> Error(ade)
                | (_,Error(dbe)) -> Error(dbe)
         }

         let generateADActions (tool:Tool, ad:seq<NetId>, db:seq<NetId>) = 
            let addToAD = 
                db 
                |> Seq.except ad 
                |> Seq.map (fun a -> Add(a, tool.ADPath, tool.Name))
            let removeFromAD = 
                ad 
                |> Seq.except db 
                |> Seq.map (fun a -> Remove(a, tool.ADPath, tool.Name))                

            let countOfMembers = Seq.length ad
            let countOfAdded = Seq.length addToAD
            let countOfRemoved = Seq.length removeFromAD
            if (countOfAdded = 0 && countOfRemoved <> 0 && countOfRemoved = countOfMembers)
            then error (Status.InternalServerError, sprintf "All %d tool grants for %s would be removed!" countOfMembers tool.Name)
            else ok (Seq.append addToAD removeFromAD)
                   
         sprintf "Processing tool update %s" toolJson |> log.LogInformation
         let! tool = tryDeserializeAsync<Tool> toolJson
         let! netids = fetchNetids tool
         let! actions = generateADActions netids
         actions |> Seq.map serialize |> Seq.iter queue.Add
         return ()
    }

    let updatePersonAccess (log:ILogger) updateJson connStr adUser adPassword = pipeline {
        let logUpdate =
            sprintf "Updated Tool AD Group: %A"
            >> log.LogInformation
         
        sprintf "Processing tool person update %s" updateJson |> log.LogInformation
        let! update = tryDeserializeAsync<ToolPersonUpdate> updateJson
        do! logADGroupUpdate connStr update
        do! updateADGroup adUser adPassword update
        logUpdate update
        return ()         
    }