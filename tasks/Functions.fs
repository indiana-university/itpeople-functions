// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause    

namespace Tasks 

module Types=
    open Core.Types

    type ADPath = string

    type ADGroupMember = NetId * ADPath
    
    type ToolPersonUpdate =
    | Add of ADGroupMember
    | Remove of ADGroupMember

    type IDataRepository =
      { GetAllNetIds: unit -> Async<Result<seq<NetId>, Error>>
        FetchLatestHRPerson: NetId -> Async<Result<Person option, Error>>
        UpdatePerson: Person -> Async<Result<Person, Error>>
        GetAllTools: unit -> Async<Result<seq<Tool>, Error>>
        GetADGroupMembers: ADPath -> Async<Result<seq<NetId>, Error>>
        GetAllToolUsers: Tool -> Async<Result<seq<NetId>, Error>>
        UpdateADGroup: ToolPersonUpdate -> Async<Result<ToolPersonUpdate, Error>> }

module FakeRepository=

    open Types
    open Core.Types
    open Core.Fakes

    let Respository = 
     { GetAllNetIds = fun () -> ["rswanso"] |> List.toSeq |> ok
       FetchLatestHRPerson = fun _ -> Some(swanson) |> ok
       UpdatePerson = ok
       GetAllTools = fun () -> [tool] |> List.toSeq |> ok
       GetADGroupMembers = fun _ -> ["rswanso"] |> List.toSeq |> ok
       GetAllToolUsers = fun _ -> ["lknope"] |> List.toSeq |> ok 
       UpdateADGroup = ok }

module DataRepository =
    open Types
    open Core.Types
    open Core.Util
    open Database.Command
    open Dapper
    open System.Net.Http
    open System.Net.Http.Headers
    open Novell.Directory.Ldap

    // DB Stuff

    let getAllNetIds connStr =
        let sql = "SELECT netid FROM people;"
        fetch connStr (fun cn -> cn.QueryAsync<NetId>(sql))

    let fetchLatestHrPerson sharedSecret netid =
        let url = sprintf "https://itpeople-adapter.apps.iu.edu/people/%s" netid
        let msg = new HttpRequestMessage(HttpMethod.Get, url)      
        msg.Headers.Authorization <-  AuthenticationHeaderValue("Bearer", sharedSecret)
        sendAsync<seq<Person>> msg
        >>= (Seq.tryFind (fun p -> p.NetId=netid) >> ok)

    let updatePerson connStr (person:Person) = 
        let sql = """
            UPDATE people 
            SET name = @Name,
                position = @Position,
                campus = @Campus,
                campus_phone = @CampusPhone,
                campus_email = @CampusEmail
            WHERE netid = @NetId
            RETURNING *;"""
        fetch connStr (fun cn -> cn.QuerySingleAsync<Person>(sql, person))

    let getAllTools connStr =
        let sql = "SELECT * FROM tools"
        fetch connStr (fun cn -> cn.QueryAsync<Tool>(sql))

    let getAllToolUsers connStr (tool:Tool) =
        let sql = """
            SELECT DISTINCT p.netid FROM people p
            JOIN unit_members um ON um.person_id = p.id
            JOIN unit_member_tools umt ON umt.membership_id = um.id
            WHERE umt.tool_id = @Id"""
        let param = {Id=tool.Id}
        fetch connStr (fun cn -> cn.QueryAsync<NetId>(sql, param))

    // LDAP Stuff

    let searchBase = "ou=Accounts,dc=ads,dc=iu,dc=edu"
    let searchFilter dn = 
        sprintf "(memberOf=%s)" dn

    let memberAttribute netid = 
        let value = sprintf "cn=%s,%s" netid searchBase
        LdapAttribute("member", value)

    let doLdapAction adUser adsPassword action = 
            use ldap = new LdapConnection()
            ldap.Connect("ads.iu.edu", 389)
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
                | Add(netid, dn) -> dn, LdapModification(LdapModification.ADD, memberAttribute netid)
                | Remove(netid, dn) -> dn, LdapModification(LdapModification.DELETE, memberAttribute netid)
            ldap.Modify(dn, modification)
            update
        try
            updateADGroup' |> doLdapAction adUser adPassword         
        with exn -> 
            if exn.Message = "No Such Object"
            then ok update // This user isn't in AD. There's nothing we can do about it. Squelch
            else 
                let msg = sprintf "Group modification failed for %A:\n%A" update exn
                error(Status.InternalServerError, msg)

    let Repository connStr sharedSecret adUser adPassword =
     { GetAllNetIds = fun () -> getAllNetIds connStr
       FetchLatestHRPerson = fetchLatestHrPerson sharedSecret
       UpdatePerson = updatePerson connStr
       GetAllTools = fun () -> getAllTools connStr 
       GetADGroupMembers = getADGroupMembers adUser adPassword 
       GetAllToolUsers = getAllToolUsers connStr 
       UpdateADGroup = updateADGroup adUser adPassword }

module Functions=

    open Core.Types
    open Core.Json

    open System
    open System.Net
    open System.Net.Http
    open Microsoft.Azure.WebJobs
    open Microsoft.Azure.WebJobs.Extensions.Http
    open Microsoft.Extensions.Logging

    open Types

    let execute (workflow:'a -> Async<Result<'b,Error>>) (arg:'a)= 
        async {
            let! result = workflow arg
            match result with
            | Ok(_) -> ()
            | Error(msg) -> 
                msg
                |> sprintf "Workflow failed with error: %A"
                |> System.Exception
                |> raise
        } |> Async.RunSynchronously

    let data = 
        let connStr = Environment.GetEnvironmentVariable("DbConnectionString")
        let sharedSecret = Environment.GetEnvironmentVariable("SharedSecret")
        let adUser = Environment.GetEnvironmentVariable("AdUser")
        let adPassword = Environment.GetEnvironmentVariable("AdPassword")
        Database.Command.init()
        DataRepository.Repository connStr sharedSecret adUser adPassword

    /// This module defines the bindings and triggers for all functions in the project
    [<FunctionName("PingGet")>]
    let ping
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")>] req:HttpRequestMessage) =
        req.CreateResponse(HttpStatusCode.OK, "pong!")

    // Enqueue the netids of all the people for whom we need to update
    // canonical HR data.
    [<FunctionName("PeopleUpdateBatcher")>]
    let peopleUpdateBatcher
        ([<TimerTrigger("0 0 14 * * *")>] timer: TimerInfo,
         [<Queue("people-update")>] queue: ICollector<string>,
         log: ILogger) = 
        
        let enqueueAllNetIds =
            Seq.iter queue.Add

        let logEnqueuedNumber = 
            Seq.length
            >> sprintf "Enqueued %d netids for update."
            >> log.LogInformation

        let workflow = 
            data.GetAllNetIds
            >=> tap enqueueAllNetIds
            >=> tap logEnqueuedNumber

        execute workflow ()

    // Pluck a netid from the queue, fetch that person's HR data from the API, 
    // and update it in the DB.
    [<FunctionName("PeopleUpdateWorker")>]
    let peopleUpdateWorker
        ([<QueueTrigger("people-update")>] netid: string,
         log: ILogger) =

        let logUpdatedPerson (person:Person) = 
            person.NetId
            |> sprintf "Updated HR data for %s."
            |> log.LogInformation
            ok ()

        let logPersonNotFound () = 
            netid
            |> sprintf "HR data not found for %s."
            |> log.LogWarning
            ok ()

        let processHRResult result =
            match result with
            | Some(person) ->
                data.UpdatePerson person
                >>= logUpdatedPerson
            | None ->
                logPersonNotFound ()

        let workflow = 
            data.FetchLatestHRPerson
            >=> processHRResult

        execute workflow netid

    let enqueueAll (queue:ICollector<string>) =
        Seq.map serialize
        >> Seq.iter queue.Add

        // Enqueue the tools for which permissions need to be updated.
    [<FunctionName("ToolUpdateBatcher")>]
    let toolUpdateBatcher
        ([<TimerTrigger("0 */15 * * * *")>] timer: TimerInfo,
         [<Queue("tool-update")>] queue: ICollector<string>,
         log: ILogger) =

         let logEnqueuedTools = 
            Seq.map (fun t -> sprintf "%s: %s" t.Name t.ADPath)
            >> String.concat "\n"
            >> sprintf "Enqueued tool permission updates for: %s"
            >> log.LogInformation

         let workfow =
            data.GetAllTools
            >=> tap (enqueueAll queue)
            >=> tap logEnqueuedTools
         
         execute workfow ()

    // Pluck a tool from the queue. 
    // Fetch all the people that should have access to this tool, then fetch 
    // all the people currently in the AD group associated with this tool. 
    // Determine which people should be added/removed from that AD group
    // and enqueue and add/remove message for each.
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
                |> Seq.map (fun a -> Add(a, tool.ADPath))
            let removeFromAD = 
                ad 
                |> Seq.except db 
                |> Seq.map (fun a -> Remove(a, tool.ADPath))
            let result = Seq.append addToAD removeFromAD 
            result |> ok
                   
         let workflow =
            tryDeserializeAsync<Tool>
            >=> fetchNetids
            >=> generateADActions
            >=> tap (enqueueAll queue)

         execute workflow item

    // Pluck a tool-person from the queue. 
    // Add/remove the person to/from the specified AD group.
    [<FunctionName("ToolUpdatePersonWorker")>]
    let toolUpdatePersonWorker
        ([<QueueTrigger("tool-update-person")>] item: string,
         log: ILogger) = 
         
         let logUpdate =
            sprintf "Updated Tool AD Group: %A"
            >> log.LogInformation
         
         let workflow =  
            tryDeserializeAsync<ToolPersonUpdate>
            >=> data.UpdateADGroup
            >=> tap logUpdate
         
         execute workflow item