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
        FetchLatestHRPerson: NetId -> Async<Result<HrPerson option, Error>>
        UpdatePerson: HrPerson -> Async<Result<Person, Error>>
        GetAllTools: unit -> Async<Result<seq<Tool>, Error>>
        GetADGroupMembers: ADPath -> Async<Result<seq<NetId>, Error>>
        GetAllToolUsers: Tool -> Async<Result<seq<NetId>, Error>>
        UpdateADGroup: ToolPersonUpdate -> Async<Result<ToolPersonUpdate, Error>>
        GetPersonMemberships: NetId -> Async<Result<seq<HistoricalPersonUnitMetadata>, Error>>
        InsertHistoricalPersonAndDeletePerson: NetId -> seq<HistoricalPersonUnitMetadata> -> Async<Result<NetId, Error>>
        FetchAllHrPeople: unit -> Async<Result<seq<HrPerson>, Error>>
        UpdateHrPeople: seq<HrPerson> -> Async<Result<unit, Error>> }

module DataRepository =
    open Types
    open Core.Types
    open Database.Command
    open Dapper
    open System
    open Novell.Directory.Ldap
    open Newtonsoft.Json
    open Oracle.ManagedDataAccess.Client

    // ORACLE Stuff
    let fetchAllHrPeople oracleConnStr = async {
        printfn "%s Fetching HR people..." (DateTime.Now.ToLongTimeString())
        let sql = """
            SELECT
                PRSN_PRM_1ST_LST_35_NM as name,
                LOWER(PRSN_USER_ID) as netid,
                POS_DESC as position,
                JOB_DEPT_ID as hr_department,
                JOB_LOC_DESC as campus,
                PRSN_CMP_PHN_NBR as campus_phone,
                PRSN_CMP_EMAIL_ADDR as campus_email
            FROM DSS.HRS_IT_JOB_CUR_GT
            WHERE
                PRSN_USER_ID IS NOT NULL
                AND JOB_PRM_2ND_IND = 'P'
            ORDER BY PRSN_UNIV_ID"""
        try
            use db = new OracleConnection(oracleConnStr)
            let! people =
                sql
                |> db.QueryAsync<HrPerson> 
                |> Async.AwaitTask
            printfn "%s Fetched HR people..." (DateTime.Now.ToLongTimeString())
            return Ok people
        with 
        | exn -> return Error (Status.InternalServerError, (sprintf "Failed to query for users: %s." exn.Message ))
    }

    // DB Stuff

    let getAllNetIds connStr =
        let sql = "SELECT netid FROM people;"
        fetch connStr (fun cn -> cn.QueryAsync<NetId>(sql))

    let fetchLatestHrPerson connStr netid = async {
        let sql = """SELECT * FROM hr_people WHERE netid=@NetId"""
        let param = {NetId = netid}
        let! result = fetch connStr (fun cn -> cn.QueryAsync<HrPerson>(sql, param))
        return
            match result with
            | Ok(people) -> people |> Seq.tryHead |> Ok
            | Error(msg) -> Error(msg)
    }

    let updatePerson connStr (person:HrPerson) = 
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

    let updateHrPeople psqlConnStr (hrPeople:seq<HrPerson>) =
        let sql = """
            INSERT INTO hr_people (name, netid, position, campus, campus_phone, campus_email, hr_department)
            VALUES (@Name, @NetId, @Position, @Campus, @CampusPhone, @CampusEmail, @HrDepartment)
            ON CONFLICT (netid)
            DO UPDATE SET
                name=@Name,
                position=@Position,
                campus=@Campus,
                campus_phone=@CampusPhone,
                campus_email=@CampusEmail,
                hr_department=@HrDepartment"""
        execute psqlConnStr sql hrPeople

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

    let getPersonMemberships connStr netid =
        let sql = """
            SELECT u.id, u.name as unit, um.title, um.role, um.permissions, '' as notes, d.name as hr_department
            FROM people p
            JOIN departments d on d.id = p.department_id
            JOIN unit_members um ON p.id = um.person_id
            JOIN units u ON u.id = um.unit_id
            where p.netid = @NetId"""
        let param = {NetId=netid}
        fetch connStr (fun cn -> cn.QueryAsync<HistoricalPersonUnitMetadata>(sql, param))

    let insertHistoricalPersonAndDeletePerson connStr netid metadata = 
        let sql = """
            -- begin a transaction to log the historical person and 
            --   delete all person records as an atomic operation
            BEGIN;
            -- add a row to the historical_people table
            INSERT INTO historical_people (netid, metadata, removed_on)
            VALUES (@NetId, CAST(@Metadata as json), @RemovedOn);
            -- delete any unit member tool assignments
            DELETE FROM unit_member_tools
            WHERE id IN (
                SELECT umt.id FROM unit_member_tools umt
                JOIN unit_members um on um.id = umt.membership_id
                JOIN people p on p.id = um.person_id
                WHERE p.netid = @NetId);
            -- delete any unit memberships
            DELETE FROM unit_members
            WHERE id IN (
                SELECT um.id FROM unit_members um 
                JOIN people p on p.id = um.person_id
                WHERE p.netid = @NetId);
            -- delete person record
            DELETE FROM people WHERE netid = @NetId;
            -- commit the transaction (rolling back changes if anything fails.)
            COMMIT;
            -- return the netid of the deleted person
            SELECT @NetId;"""
        let json = JsonConvert.SerializeObject(metadata, Core.Json.JsonSettings)
        let param = {NetId=netid; Metadata=json; RemovedOn=DateTime.UtcNow}
        fetch connStr (fun cn -> cn.QuerySingleAsync<NetId>(sql, param))

    let Repository psqlConnStr oracleConnStr adUser adPassword =
     { GetAllNetIds = fun () -> getAllNetIds psqlConnStr
       FetchLatestHRPerson = fetchLatestHrPerson psqlConnStr
       UpdatePerson = updatePerson psqlConnStr
       GetAllTools = fun () -> getAllTools psqlConnStr 
       GetADGroupMembers = getADGroupMembers adUser adPassword 
       GetAllToolUsers = getAllToolUsers psqlConnStr 
       UpdateADGroup = updateADGroup adUser adPassword
       GetPersonMemberships = getPersonMemberships psqlConnStr
       InsertHistoricalPersonAndDeletePerson = insertHistoricalPersonAndDeletePerson psqlConnStr
       FetchAllHrPeople = fun () -> fetchAllHrPeople oracleConnStr
       UpdateHrPeople = updateHrPeople psqlConnStr }

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
        let psqlConnectionString = Environment.GetEnvironmentVariable("DbConnectionString")
        let oracleConnectionString = Environment.GetEnvironmentVariable("OracleConnectionString")
        let adUser = Environment.GetEnvironmentVariable("AdUser")
        let adPassword = Environment.GetEnvironmentVariable("AdPassword")
        Database.Command.init()
        DataRepository.Repository psqlConnectionString oracleConnectionString adUser adPassword

    /// This module defines the bindings and triggers for all functions in the project
    [<FunctionName("PingGet")>]
    let ping
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")>] req:HttpRequestMessage) =
        req.CreateResponse(HttpStatusCode.OK, "pong!")

    // Enqueue the netids of all the people for whom we need to update
    // canonical HR data.
    [<FunctionName("PeopleUpdateHrTable")>]
    let peopleUpdateHrTable
        ([<TimerTrigger("0 30 13 * * 1-5", RunOnStartup=true)>] timer: TimerInfo,
         log: ILogger) = 
        
        let workflow = 
            data.FetchAllHrPeople
            >=> data.UpdateHrPeople

        execute workflow ()

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

        let logRemovedPerson netid = 
            netid
            |> sprintf "HR data not found for %s. The 'person' record for this netid has been removed. Any unit memberships have been logged to the 'historical_people' table."
            |> log.LogInformation
            ok ()

        let processHRResult result =
            match result with
            | Some(person) ->
                data.UpdatePerson person
                >>= logUpdatedPerson
            | None ->
                data.GetPersonMemberships netid
                >>= data.InsertHistoricalPersonAndDeletePerson netid
                >>= logRemovedPerson

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