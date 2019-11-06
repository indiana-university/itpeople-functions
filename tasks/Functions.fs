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

    type UnitRemoval =
      { Name: string
        NetId: string
        UnitName: string
        UnitId: int }

    type IDataRepository =
      { GetAllNetIds: unit -> Async<Result<seq<NetId>, Error>>
        FetchLatestPersonData: NetId -> Async<Result<Person * HrPerson option, Error>>
        UpdatePerson: HrPerson -> Async<Result<Person, Error>>
        GetAllUnitLeadershipEmails: UnitRemoval -> Async<Result<UnitRemoval * string seq, Error>>
        GetAllTools: unit -> Async<Result<seq<Tool>, Error>>
        GetADGroupMembers: ADPath -> Async<Result<seq<NetId>, Error>>
        GetAllToolUsers: Tool -> Async<Result<seq<NetId>, Error>>
        UpdateADGroup: ToolPersonUpdate -> Async<Result<ToolPersonUpdate, Error>>
        GetPersonMemberships: Person -> Async<Result<Person * seq<HistoricalPersonUnitMetadata>, Error>>
        InsertHistoricalPersonAndRemoveMemberships: Person * seq<HistoricalPersonUnitMetadata> -> Async<Result<Person * seq<HistoricalPersonUnitMetadata>, Error>>
        InsertHistoricalPersonAndDeletePerson: Person *  seq<HistoricalPersonUnitMetadata> -> Async<Result<Person, Error>>
        FetchAllHrPeople: unit -> Async<Result<seq<HrPerson>, Error>>
        UpdateHrPeople: seq<HrPerson> -> Async<Result<unit, Error>>
        SyncDepartments: unit -> Async<Result<unit, Error>> }

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

    type ProfilePage =
      { totalRecords: int 
        currentPage: string
        lastPage: string }
    type ProfileJob = 
      { jobStatus: string 
        jobDepartmentId: string
        jobDepartmentDesc: string
        position: string }
    type ProfileContact =
      { phoneNumber: string 
        campusCode: string }
    type ProfileEmployee =
      { lastName: string
        firstName: string
        username: string
        email: string
        jobs: seq<ProfileJob>
        contacts: seq<ProfileContact> }
    type ProfileReponse =
      { page: ProfilePage 
        employees: seq<ProfileEmployee> }

    let concatResult s1 r2 = 
        match r2 with
        | Ok(s2) -> Seq.append s1 s2 |> Ok
        | _ -> r2

    let consoleLog msg = 
        printfn "%s %s" (DateTime.Now.ToLongTimeString()) msg

    let getUaaToken (uaaUrl:string) username password =
        "Fetching UAA token..." |> consoleLog
        let content =
            dict [
                "grant_type", "client_credentials"
                "client_id", username
                "client_secret", password
            ]
            |> Collections.Generic.Dictionary
            |> (fun d-> new FormUrlEncodedContent(d))
        postAsync<JwtResponse> uaaUrl content

    let getProfilePage hrDataUrl token page = 
        let uri = sprintf "%s?affiliationType=employee&page=%d&pageSize=7500" hrDataUrl page |> Uri
        let req = new HttpRequestMessage(HttpMethod.Get, uri)
        req.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)
        sendAsync<ProfileReponse> req

    let getAllEmployees hrDataUrl (jwt:JwtResponse) =
        // recursively page through all employees
        let rec getAllEmployeesRec page = async {
            // get the requested page of employees
            match! getProfilePage hrDataUrl jwt.access_token page with
            | Ok(resp) ->
                sprintf "Fetched page %d from HR source." page |> consoleLog
                // if this is the last page, return the set to caller
                sprintf "\n\tcur: %s\n\tlst: %s" resp.page.currentPage resp.page.lastPage |> consoleLog
                if resp.page.currentPage = resp.page.lastPage
                then return Ok resp.employees
                else
                    // recurse
                    let! next = getAllEmployeesRec (page+1)
                    // return the combined sequences, shortcircuiting on error.
                    return concatResult resp.employees next
            | Error(msg) -> return Error(msg)
        }
        // fetch first page and kick off recursion
        "Fetching employees from HR source..." |> consoleLog
        getAllEmployeesRec 0

    let mapEmployeesToDomainRecords (list:seq<ProfileEmployee>) = 
        printfn "%s Fetched %d people from HR source." (DateTime.Now.ToLongTimeString()) (list |> Seq.length)
        let toDomainRecord e =
            let (position, deptName, deptDesc) = 
                match e.jobs |> Seq.tryFind (fun j -> j.jobStatus = "P") with
                | Some(job) -> (job.position, job.jobDepartmentId, job.jobDepartmentDesc)
                | None -> ("","","")
            let (phone, campus) = 
                match e.contacts |> Seq.tryHead with
                | Some(contact) -> (contact.phoneNumber, contact.campusCode)
                | None -> ("","")                                
            { Id=0
              Name=sprintf "%s %s" e.firstName e.lastName
              NetId=e.username.ToLower()
              Position=position
              HrDepartment=deptName
              HrDepartmentDescription=deptDesc
              Campus=campus
              CampusEmail=e.email
              CampusPhone=phone }
        let validRecord (r:HrPerson) = 
            hasValue r.HrDepartment && hasValue r.CampusEmail 
        let domain = list |> Seq.map toDomainRecord 
        let dupes = 
            domain 
            |> Seq.countBy(fun r -> r.NetId)
            |> Seq.filter(fun (_,count) -> count > 1)
            |> Seq.map (fun (key,_) -> key)
        sprintf "Found %d duplicate netids: %s" 
            (dupes |> Seq.length) 
            (dupes |> String.concat ", ") 
            |> consoleLog
        let distinct = domain |> Seq.distinctBy (fun r -> r.NetId)
        sprintf "Found %d distinct netids." 
            (distinct |> Seq.length) |> consoleLog
        let invalid = distinct |> Seq.filter (validRecord >> not)
        sprintf "Found %d invalid records due to missing email or HR dept: %s" 
            (invalid |> Seq.length) 
            (invalid |> Seq.map (fun r -> r.NetId) |> String.concat ", ")
            |> consoleLog
        let valid = distinct |> Seq.filter validRecord
        sprintf "Found %d valid records." 
            (valid |> Seq.length) |> consoleLog 
        valid |> ok

    // DENODO Stuff
    let fetchAllHrPeople uaaUrl hrDataUrl username password =
        fun () -> getUaaToken uaaUrl username password
        >=> getAllEmployees hrDataUrl
        >=> mapEmployeesToDomainRecords

    // DB Stuff

    let getAllNetIds connStr =
        let sql = "SELECT netid FROM people;"
        fetch (fun cn -> cn.QueryAsync<NetId>(sql)) connStr

    let fetchAll<'T> connStr sql param = fetch (fun cn -> cn.QueryAsync<'T>(sql, param)) connStr

    let fetchLatestPersonData connStr netid = async {
        let queryPersonSql = """
            SELECT DISTINCT p.*, d.*
            FROM people p
            JOIN departments d on d.id = p.department_id
            WHERE netid=@NetId"""
        let mapper (p:Person) d = {p with Department=d}
        let param = {NetId = netid}
        let! personSeq = fetch (fun cn -> cn.QueryAsync<Person, Department, Person>(queryPersonSql, mapper, param)) connStr
        let! hrPersonSeq = fetchAll<HrPerson> connStr "SELECT * FROM hr_people WHERE netid=@NetId" {NetId = netid}
        return
            match (personSeq, hrPersonSeq) with
            | Error(msg), _ -> Error(msg)
            | _, Error(msg) -> Error(msg)
            | Ok(p), Ok(hr) -> Ok (p |> Seq.head, hr |> Seq.tryHead)
    }

    let updatePerson connStr (person:HrPerson) = 
        let sql = """
            UPDATE people
            SET name = @Name,
                position = @Position,
                campus = @Campus,
                campus_phone = @CampusPhone,
                campus_email = @CampusEmail,
                department_id = (SELECT id FROM departments WHERE name=@HrDepartment)
            WHERE netid = @NetId
            RETURNING *;"""
        fetch (fun cn -> cn.QuerySingleAsync<Person>(sql, person)) connStr

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

    let syncDepartments connStr =
        let sql = """
            -- 1. Add any new hr departments
            INSERT INTO departments (name, description)
            SELECT DISTINCT hr_department, hr_department_desc
	        FROM hr_people
	        WHERE hr_department IS NOT NULL
            ON CONFLICT (name)
            DO NOTHING;
            -- 2. Update department descriptions 
            UPDATE departments d
            SET description = hr_department_desc
            FROM hr_people hr
            WHERE d.name = hr.hr_department"""
        execute connStr sql ()

    let updateHrPeople psqlConnStr (hrPeople:seq<HrPerson>) =
        // convert the hr person to a formatting string representing the table row data.
        let toRow (p:HrPerson) = 
            sprintf "%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n" p.Name p.NetId p.Position p.Campus p.CampusPhone p.CampusEmail p.HrDepartment p.HrDepartmentDescription
        executeRaw psqlConnStr (fun cn ->
            cn.Open()
            // truncate the existing
            cn.Execute("DELETE FROM hr_people;") |> ignore
            // bulk insert the new rows
            use writer = cn.BeginTextImport("COPY hr_people (name, netid, position, campus, campus_phone, campus_email, hr_department, hr_department_desc) FROM STDIN")
            hrPeople |> Seq.map toRow  |> Seq.iter writer.Write
            // flush the writer to finish the bulk insert
            writer.Flush()
        )

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

    let getPersonMemberships connStr (person:Person) = async {
        let sql = """
            SELECT u.id, u.name as unit, u.id as unit_id, um.title, um.role, um.permissions, '' as notes, d.name as hr_department
            FROM people p
            JOIN departments d on d.id = p.department_id
            JOIN unit_members um ON p.id = um.person_id
            JOIN units u ON u.id = um.unit_id
            where p.netid = @NetId"""
        let param = {NetId=person.NetId}
        let! result = fetch (fun cn -> cn.QueryAsync<HistoricalPersonUnitMetadata>(sql, param)) connStr
        match result with
        | Error(msg) -> return Error(msg)
        | Ok(memberships) -> return Ok (person, memberships)
    }

    let insertHistoricalPersonAndRemoveMemberships connStr (person:Person, metadata:HistoricalPersonUnitMetadata seq) = async {
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
            COMMIT;
            -- return the netid of the deleted person
            SELECT @NetId;"""
        let json = JsonConvert.SerializeObject(metadata, Core.Json.JsonSettings)
        let param = {NetId=person.NetId; Metadata=json; RemovedOn=DateTime.UtcNow}
        let! result = fetch (fun cn -> cn.QuerySingleAsync<NetId>(sql, param)) connStr
        match result with
        | Error(msg) -> return Error(msg)
        | Ok(_) -> return Ok (person, metadata)
    }

    let insertHistoricalPersonAndDeletePerson connStr (person:Person, metadata:HistoricalPersonUnitMetadata seq) = async {
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
        let param = {NetId=person.NetId; Metadata=json; RemovedOn=DateTime.UtcNow}
        let! result = fetch (fun cn -> cn.QuerySingleAsync<NetId>(sql, param)) connStr
        match result with
        | Error(msg) -> return Error(msg)
        | Ok(_) -> return Ok person
    }

    let getAllUnitLeadershipEmails connStr (unitRemoval:UnitRemoval) = async {
        let sql = """
            SELECT DISTINCT p.campus_email FROM people p
            JOIN unit_members um ON um.person_id = p.id
            WHERE um.unit_id = @Id"""
        let param = {Id=unitRemoval.UnitId}
        let! result = fetch (fun cn -> cn.QueryAsync<string>(sql, param)) connStr
        match result with
        | Error(msg) -> return Error(msg)
        | Ok(emails) -> return Ok (unitRemoval, emails)
    }

    let Repository psqlConnStr uaaUrl hrDataUrl adUser adPassword uaaUser uaaPassword =
     { GetAllNetIds = fun () -> getAllNetIds psqlConnStr
       FetchLatestPersonData = fetchLatestPersonData psqlConnStr
       UpdatePerson = updatePerson psqlConnStr
       GetAllUnitLeadershipEmails = getAllUnitLeadershipEmails psqlConnStr
       GetAllTools = fun () -> getAllTools psqlConnStr 
       GetADGroupMembers = getADGroupMembers adUser adPassword 
       GetAllToolUsers = getAllToolUsers psqlConnStr 
       UpdateADGroup = updateADGroup adUser adPassword
       GetPersonMemberships = getPersonMemberships psqlConnStr
       InsertHistoricalPersonAndRemoveMemberships = insertHistoricalPersonAndRemoveMemberships psqlConnStr
       InsertHistoricalPersonAndDeletePerson = insertHistoricalPersonAndDeletePerson psqlConnStr
       FetchAllHrPeople = fetchAllHrPeople uaaUrl hrDataUrl uaaUser uaaPassword
       UpdateHrPeople = updateHrPeople psqlConnStr
       SyncDepartments = fun () -> syncDepartments psqlConnStr }

module Functions=

    open Core.Types
    open Core.Json

    open System
    open System.Net
    open System.Net.Http
    open Microsoft.Azure.WebJobs
    open Microsoft.Azure.WebJobs.Extensions.Http
    open Microsoft.Extensions.Logging
    open SendGrid.Helpers.Mail


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
        let hrDataUrl = Environment.GetEnvironmentVariable("HrDataUrl")
        let uaaUrl = Environment.GetEnvironmentVariable("UaaUrl")
        let uaaUser = Environment.GetEnvironmentVariable("UaaUser")
        let uaaPassword = Environment.GetEnvironmentVariable("UaaPassword")
        let adUser = Environment.GetEnvironmentVariable("AdUser")
        let adPassword = Environment.GetEnvironmentVariable("AdPassword")
        Database.Command.init()
        DataRepository.Repository psqlConnectionString uaaUrl hrDataUrl adUser adPassword uaaUser uaaPassword

    let enqueueAll (queue:ICollector<string>) =
        Seq.map serialize
        >> Seq.iter queue.Add

    /// This module defines the bindings and triggers for all functions in the project
    [<FunctionName("PingGet")>]
    let ping
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")>] req:HttpRequestMessage) =
        req.CreateResponse(HttpStatusCode.OK, "pong!")

    // Enqueue the netids of all the people for whom we need to update
    // canonical HR data.
    [<FunctionName("PeopleUpdateHrTable")>]
    let peopleUpdateHrTable
        ([<TimerTrigger("0 0 * * * *")>] timer: TimerInfo,
         [<Queue("people-update")>] queue: ICollector<string>,
         log: ILogger) = 

        let enqueueAllNetIds =
            Seq.iter queue.Add

        let logEnqueuedNumber netids = 
            sprintf "Enqueued %d netids for update." (Seq.length netids)
            |> log.LogInformation

        let workflow = 
            data.FetchAllHrPeople
            >=> data.UpdateHrPeople
            >=> data.SyncDepartments
            >=> data.GetAllNetIds
            >=> tap enqueueAllNetIds
            >=> tap logEnqueuedNumber

        execute workflow ()

    // Pluck a netid from the queue, fetch that person's HR data from the API, 
    // and update it in the DB.
    [<FunctionName("PeopleUpdateWorker")>]
    let peopleUpdateWorker
        ([<QueueTrigger("people-update")>] netid: string,
         [<Queue("people-update-notification")>] queue: ICollector<string>,
         log: ILogger) =

        let logUpdate (person:Person) = 
            person.NetId
            |> sprintf "Updated HR data for %s."
            |> log.LogInformation
            ok person

        let logRemovalFromDirectory (person:Person) = 
            person.NetId
            |> sprintf "HR data not found for %s. The 'person' record for this netid has been removed. Any existing unit memberships have been logged to the 'historical_people' table."
            |> log.LogInformation
            ok person

        let logRemovalFromUnits (person:Person, metadata: HistoricalPersonUnitMetadata seq) =
            person.NetId
            |> sprintf "HR department and/or postion has changed for %s. The unit memberships for this person have been removed. Any existing unit memberships have been logged to the 'historical_people' table."
            |> log.LogInformation
            ok (person, metadata)

        let enqueueNotifications (person:Person, metadata: HistoricalPersonUnitMetadata seq) =
            metadata
            |> Seq.map (fun m -> {Name=person.Name; NetId=person.NetId; UnitName=m.Unit; UnitId=m.UnitId})
            |> enqueueAll queue
            ok (person)

        let processHRResult (person:Person, hrPersonOpt:HrPerson option) =
            match hrPersonOpt with
            // The person has changed jobs or HR Departments
            | Some(hrPerson) when 
                hrPerson.HrDepartment <> person.Department.Name
                || hrPerson.Position <> person.Position ->
                data.UpdatePerson hrPerson
                >>= logUpdate
                >>= data.GetPersonMemberships
                >>= data.InsertHistoricalPersonAndRemoveMemberships
                >>= logRemovalFromUnits
                >>= enqueueNotifications
            // The person is still in the same role
            | Some(hrPerson) ->
                data.UpdatePerson hrPerson
                >>= logUpdate
            // The person is no longer working for IU
            | None ->
                data.GetPersonMemberships person
                >>= data.InsertHistoricalPersonAndDeletePerson
                >>= logRemovalFromDirectory

        let workflow = 
            data.FetchLatestPersonData
            >=> processHRResult

        sprintf "Processing person update for netid %s" netid |> log.LogInformation
        execute workflow netid


    // Send a notification to unit leaders/subleads when a person is removed from the directory.
    [<FunctionName("PeopleUpdateNotification")>]
    let peopleUpdateNotification
        ([<QueueTrigger("people-update-notification")>] unitRemoval: string,
         [<SendGrid(ApiKey = "SendGridApiKey")>] collector: ICollector<SendGridMessage> , 
         log: ILogger) =
        
        let generateEmailMessage (unit:UnitRemoval, leaders:string seq) =
            let from = "notifier@iu.edu"
            let toList = leaders |> Seq.map EmailAddress |> ResizeArray<EmailAddress>
            let subject = sprintf """[IT People] Membership updated for %s.""" unit.UnitName
            let body = sprintf """This is an automated notification from IT People.
            
%s (%s) was automatically removed from the %s unit (http://itpeople.apps.iu.edu/units/%d) due to a change in their position or HR reporting organization. You are receiving this message because you are listed as a leader or subleader of that unit. 

If you believe this removal was in error, or need further assistance, please contact IT Community Partnerships (talk2uits@iu.edu).""" unit.Name unit.NetId unit.UnitName unit.UnitId
            let msg = SendGridMessage(From=EmailAddress(from), Subject=subject, PlainTextContent=body)
            msg.AddTos(toList)
            ok msg
        
        let logEmailDelivery (msg:SendGridMessage) =
            let tos = 
                msg.Personalizations 
                |> Seq.collect (fun p -> p.Tos) 
                |> Seq.map (fun t -> t.Email)
                |> String.concat "; "
            sprintf "Sent notification to '%s' with subject '%s'" tos msg.Subject 
            |> log.LogInformation

        let workflow = 
            tryDeserializeAsync<UnitRemoval>
            >=> data.GetAllUnitLeadershipEmails
            >=> generateEmailMessage
            >=> tap collector.Add
            >=> tap logEmailDelivery

        sprintf "Processing notification %s" unitRemoval |> log.LogInformation
        if Environment.GetEnvironmentVariable("SendNotifications") |> bool.Parse            
        then execute workflow unitRemoval
        else log.LogInformation("Notification delivery is disabled for this environment")

        // Enqueue the tools for which permissions need to be updated.
    [<FunctionName("ToolUpdateBatcher")>]
    let toolUpdateBatcher
        ([<TimerTrigger("0 */15 * * * *")>] timer: TimerInfo,
         [<Queue("tool-update")>] queue: ICollector<string>,
         log: ILogger) =

         let logEnqueuedTools (tools:Tool seq) = 
            tools
            |> Seq.map (fun t -> sprintf "%s: %s" t.Name t.ADPath)
            |> String.concat "\n"
            |> sprintf "Enqueued tool permission updates for: %s"
            |> log.LogInformation

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

         sprintf "Processing tool update %s" item |> log.LogInformation
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
         
         sprintf "Processing tool person update %s" item |> log.LogInformation
         execute workflow item