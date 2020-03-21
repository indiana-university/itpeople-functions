namespace Tasks

module Tools =

    open Core.Types
    open Core.Json
    open Database.Command
    open Dapper
    open Microsoft.Azure.WebJobs
    open Microsoft.Extensions.Logging
    open Novell.Directory.Ldap
    open Logging

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

    let enqueueTools (queue:ICollector<string>) connStr (log:Serilog.ILogger) = pipeline {
        let! tools = getAllTools connStr        
        log |> logInfo (sprintf "Enqueueing updates for %d tools." (Seq.length tools)) None
        
        tools 
        |> Seq.map serialize  
        |> Seq.iter queue.Add
        
        let logEnqeued (t:Tool) = log |> logInfo (sprintf "Enqueued update for %s." t.Name) (Some(t))
        tools |> Seq.iter logEnqeued        
        return ()
    }

    let private guardAgainstClearingOfGroup (tool:Tool) adGroupMembers addToAD remFromAD = 
        let countOfMembers = Seq.length adGroupMembers
        let countOfAdded = Seq.length addToAD
        let countOfRemoved = Seq.length remFromAD
        if (countOfAdded = 0 && countOfRemoved <> 0 && countOfRemoved = countOfMembers)
        then error (Status.InternalServerError, sprintf "All %d tool grants for %s would be removed!" countOfMembers tool.Name)
        else ok ()

    let private enqueueUpdates (queue:ICollector<string>) (tool:Tool) addToAD remFromAD =
        let adds = addToAD |> Seq.map (fun a -> Add(a, tool.ADPath, tool.Name))
        let rems = remFromAD |> Seq.map (fun a -> Remove(a, tool.ADPath, tool.Name))
        adds
        |> Seq.append rems 
        |> Seq.map serialize 
        |> Seq.iter queue.Add
        Seq.length adds + Seq.length rems

    let enqueueAccessUpdates (queue:ICollector<string>) toolJson connStr adUser adPassword (log:Serilog.ILogger) = pipeline {
        let! tool = tryDeserializeAsync<Tool> toolJson
        log |> logInfo (sprintf "Processing tool access update for %s" tool.Name) None
        let! adGroupMembers = getADGroupMembers adUser adPassword tool.ADPath
        log |> logInfo (sprintf "Found %d members of tool AD group" (Seq.length adGroupMembers)) None        
        let! toolUsers = getAllToolUsers connStr tool 
        log |> logInfo (sprintf "Found %d netids with access to tool" (Seq.length toolUsers)) None

        let addToAD = toolUsers |> Seq.except adGroupMembers |> Seq.sort 
        log |> logInfo (sprintf "Tool access will be granted to %d netids" (Seq.length addToAD)) (Some(addToAD))        
        let remFromAD =  adGroupMembers |> Seq.except toolUsers |> Seq.sort                
        log |> logInfo (sprintf "Tool access will be revoked from %d netids" (Seq.length remFromAD)) (Some(remFromAD))

        do! guardAgainstClearingOfGroup tool adGroupMembers addToAD remFromAD

        let count = enqueueUpdates queue tool addToAD remFromAD
        log |> logInfo (sprintf "Enqueued %d AD group memberships updates" count) None
        
        return ()
    }

    let updatePersonAccess updateJson connStr adUser adPassword (log:Serilog.ILogger) = pipeline {
        let! update = tryDeserializeAsync<ToolPersonUpdate> updateJson
        log |> logInfo "Processing AD group update" (Some(update))
        do! logADGroupUpdate connStr update
        do! updateADGroup adUser adPassword update
        return ()         
    }