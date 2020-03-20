// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause    

namespace Tasks 

module Types=
    open Core.Types

    type ADPath = string
    type ToolName = string
    type ADGroupMember = NetId * ADPath * ToolName
    
    type ToolPersonUpdate =
    | Add of ADGroupMember
    | Remove of ADGroupMember

    type UnitRemoval =
      { Name: string
        NetId: string
        UnitName: string
        UnitId: int }

    type IDataRepository =
      { GetAllTools: unit -> Async<Result<seq<Tool>, Error>>
        GetADGroupMembers: ADPath -> Async<Result<seq<NetId>, Error>>
        GetAllToolUsers: Tool -> Async<Result<seq<NetId>, Error>>
        LogADGroupUpdate: ToolPersonUpdate -> Async<Result<unit, Error>>
        UpdateADGroup: ToolPersonUpdate -> Async<Result<unit, Error>>
        FetchAllBuildings: unit -> Async<Result<seq<Building>, Error>>
        UpdateBuildings: seq<Building> -> Async<Result<unit, Error>> }

module DataRepository =
    open Types
    open Core.Types
    open Core.Util
    open Database.Command
    open Dapper
    open System
    open System.Net.Http
    open System.Net.Http.Headers
    open Novell.Directory.Ldap
    open Newtonsoft.Json

    type DenodoResponse<'T> =
      { name: string
        elements: seq<'T> }
    type DenodoBuilding =
      { building_code: string
        site_code: string
        building_name: string
        building_long_description: string
        street: string
        city: string
        state: string
        zip: string }

    let getAllTools connStr =
        let sql = "SELECT * FROM tools"
        fetch (fun cn -> cn.QueryAsync<Tool>(sql)) connStr

    let getAllToolUsers connStr (tool:Tool) =
        let sql = """
            SELECT DISTINCT p.netid FROM people p
            JOIN unit_members um ON um.person_id = p.id
            JOIN unit_member_tools umt ON umt.membership_id = um.id
            WHERE umt.tool_id = @Id"""
        let param = {Id=tool.Id}
        fetch (fun cn -> cn.QueryAsync<NetId>(sql, param)) connStr

    // LDAP Stuff

    let searchBase = "ou=Accounts,dc=ads,dc=iu,dc=edu"
    let searchFilter dn = 
        sprintf "(memberOf=%s)" dn

    let memberAttribute netid = 
        let value = sprintf "cn=%s,%s" netid searchBase
        LdapAttribute("member", value)

    let doLdapAction adUser adsPassword action = 
            let adUser = sprintf """ads\%s""" adUser
            use ldap = new LdapConnection()
            ldap.SecureSocketLayer <- true
            ldap.Connect("ads.iu.edu", 636)
            ldap.Bind(adUser, adsPassword)  
            ldap |> action |> ok

    let getADGroupMembers adUser adPassword dn =
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

    let updateADGroup adUser adPassword update =
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

    let getDenodoResponse url user password = 
        let req = new HttpRequestMessage(HttpMethod.Get, url|>Uri)
        let basicauth = 
            sprintf "%s:%s" user password
            |> Text.Encoding.GetEncoding("ISO-8859-1").GetBytes
            |> Convert.ToBase64String            
        req.Headers.Authorization <- AuthenticationHeaderValue("Basic", basicauth)
        sendAsync<DenodoResponse<DenodoBuilding>> req        

    let mapToDomainBuilding (denodoBuildings:DenodoResponse<DenodoBuilding>) =
        let valueOrEmpty str = if isNull str then "" else str
        
        denodoBuildings.elements
        |> Seq.filter (fun e -> not (isNull e.building_name || isNull e.building_code))
        |> Seq.map (fun e -> 
            { Id=0
              Building.Name = e.building_long_description 
              Code = e.building_code
              Address = valueOrEmpty e.street
              City = valueOrEmpty e.site_code
              State = ""
              PostCode = ""
              Country = "" } )
        |> ok

    let fetchAllBuildings url user password = pipeline {
        let! denodoBuildings = getDenodoResponse url user password
        return! denodoBuildings |> mapToDomainBuilding 
    }
    
    let updateBuildings connStr (buildings:seq<Building>) = 
        let sql = 
            """INSERT INTO buildings (name, code, address, city, state, post_code, country) VALUES
               (@Name, @Code, @Address, @City, @State, @PostCode, @Country)
               ON CONFLICT(code) DO UPDATE SET 
               name = EXCLUDED.name, 
               address = EXCLUDED.address,
               city = EXCLUDED.city,
               state = EXCLUDED.state,
               post_code = EXCLUDED.post_code,
               country = EXCLUDED.country;
               """
        execute connStr sql buildings
    
    type ToolPersonUpdateRow = 
      { ChangeType: string; 
        NetId: string;
        ToolName: string;
        ToolPath: string; }

    let logADGroupUpdate connStr toolPersonUpdate =
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

    let Repository psqlConnStr uaaUrl hrDataUrl adUser adPassword uaaUser uaaPassword buildingUrl buildingUser buildingPassword =
     { GetAllTools = fun () -> getAllTools psqlConnStr 
       GetADGroupMembers = getADGroupMembers adUser adPassword 
       GetAllToolUsers = getAllToolUsers psqlConnStr 
       LogADGroupUpdate = logADGroupUpdate psqlConnStr
       UpdateADGroup = updateADGroup adUser adPassword
       FetchAllBuildings = fun () -> fetchAllBuildings buildingUrl buildingUser buildingPassword
       UpdateBuildings = updateBuildings psqlConnStr }

module Functions=

    open Core.Types
    open Core.Json

    open System.Net
    open System.Net.Http
    open Microsoft.Azure.WebJobs
    open Microsoft.Azure.WebJobs.Extensions.Http
    open Microsoft.Extensions.Logging
    
    open Core.Util
    open Types

    let execute (workflow:Async<Result<'b,Error>>)= 
        async {
            let! result = workflow
            match result with
            | Ok(_) -> ()
            | Error(msg) -> 
                msg
                |> sprintf "Workflow failed with error: %A"
                |> System.Exception
                |> raise
        } |> Async.RunSynchronously

    let data = 
        let psqlConnectionString = env "DbConnectionString"
        let hrDataUrl = env "HrDataUrl"
        let uaaUrl = env "UaaUrl"
        let uaaUser = env "UaaUser"
        let uaaPassword = env "UaaPassword"
        let adUser = env "AdUser"
        let adPassword = env "AdPassword"
        let buildingUrl = env "BuildingUrl"
        let buildingUser = env "BuildingUser"
        let buildingPassword = env "BuildingPassword"
        Database.Command.init()
        DataRepository.Repository psqlConnectionString uaaUrl hrDataUrl adUser adPassword uaaUser uaaPassword buildingUrl buildingUser buildingPassword

    let enqueueAll (queue:ICollector<string>) =
        Seq.map serialize
        >> Seq.iter queue.Add

    /// This module defines the bindings and triggers for all functions in the project
    [<FunctionName("PingGet")>]
    let ping
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")>] req:HttpRequestMessage) =
        req.CreateResponse(HttpStatusCode.OK, "pong!")

    // Update our buildings table from a canonical data source.
    // [<Disable>]
    [<FunctionName("BuildingsUpdate")>]
    let buildingsUpdate
        ([<TimerTrigger("0 */15 * * * *")>] timer: TimerInfo,
         log: ILogger) =

        let logBuildingCount buildings = 
            sprintf "Fetched %d buildings from Denodo." (buildings |> Seq.length)
            |> log.LogInformation

        let workflow = pipeline {
            let! buildings = data.FetchAllBuildings ()
            logBuildingCount buildings
            return! data.UpdateBuildings buildings
        }

        execute workflow

    // Enqueue the netids of all the people for whom we need to update
    // canonical HR data.
    // [<Disable>]
    [<FunctionName("PeopleUpdateHrTable")>]
    let peopleUpdateHrTable
        ([<TimerTrigger("0 */15 * * * *")>] timer: TimerInfo,
         [<Queue("people-update")>] queue: ICollector<string>,
         log: ILogger) = 
        let connStr = env "DbConnectionString"
        let hrDataUrl = env "HrDataUrl"
        let uaaUrl = env "UaaUrl"
        let uaaUser = env "UaaUser"
        let uaaPassword = env "UaaPassword"
        People.updateHrTable log queue connStr hrDataUrl uaaUrl uaaUser uaaPassword |> execute

    // Pluck a netid from the queue, fetch that person's HR data from the API, 
    // and update it in the DB.
    // [<Disable>]
    [<FunctionName("PeopleUpdateWorker")>]
    let peopleUpdateWorker
        ([<QueueTrigger("people-update")>] netid: string,
         log: ILogger) =
        let connStr = env "DbConnectionString"        
        People.updatePerson log netid connStr |> execute

        // Enqueue the tools for which permissions need to be updated.
    // [<Disable>]
    [<FunctionName("ToolUpdateBatcher")>]
    let toolUpdateBatcher
        ([<TimerTrigger("0 */5 * * * *")>] timer: TimerInfo,
         [<Queue("tool-update")>] queue: ICollector<string>,
         log: ILogger) =

         let logEnqueuedTools (tools:Tool seq) = 
            tools
            |> Seq.map (fun t -> sprintf "%s: %s" t.Name t.ADPath)
            |> String.concat "\n"
            |> sprintf "Enqueued tool permission updates for: %s"
            |> log.LogInformation

         let workfow = pipeline {
            let! tools = data.GetAllTools ()
            enqueueAll queue tools
            logEnqueuedTools tools
            return ()
         }
         
         execute workfow

    // Pluck a tool from the queue. 
    // Fetch all the people that should have access to this tool, then fetch 
    // all the people currently in the AD group associated with this tool. 
    // Determine which people should be added/removed from that AD group
    // and enqueue and add/remove message for each.
    // [<Disable>]
    [<FunctionName("ToolUpdateWorker")>]
    let toolUpdateWorker
        ([<QueueTrigger("tool-update")>] item: string,
         [<Queue("tool-update-person")>] queue: ICollector<string>,
         log: ILogger) = 

         let fetchNetids (tool:Tool) = async {
            let! adPromise = data.GetADGroupMembers tool.ADPath |> Async.StartChild
            let! dbPromise = data.GetAllToolUsers tool |> Async.StartChild
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
                   
         let workflow = pipeline {
             let! tool = tryDeserializeAsync<Tool> item
             let! netids = fetchNetids tool
             let! actions = generateADActions netids
             enqueueAll queue actions
             return ()
         }

         sprintf "Processing tool update %s" item |> log.LogInformation
         execute workflow

    // Pluck a tool-person from the queue. 
    // Add/remove the person to/from the specified AD group.
    // [<Disable>]
    [<FunctionName("ToolUpdatePersonWorker")>]
    let toolUpdatePersonWorker
        ([<QueueTrigger("tool-update-person")>] item: string,
         log: ILogger) = 
         
         let logUpdate =
            sprintf "Updated Tool AD Group: %A"
            >> log.LogInformation
         
         let workflow = pipeline {
             let! update = tryDeserializeAsync<ToolPersonUpdate> item
             do! data.LogADGroupUpdate update
             do! data.UpdateADGroup update
             logUpdate update
             return ()
         }
         
         sprintf "Processing tool person update %s" item |> log.LogInformation
         execute workflow