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
        GetAllTools: unit -> Async<Result<seq<Tool>, Error>>
        GetADGroupMembers: ADPath -> Async<Result<seq<NetId>, Error>>
        GetAllToolUsers: Tool -> Async<Result<seq<NetId>, Error>>
        UpdateADGroup: ToolPersonUpdate -> Async<Result<ToolPersonUpdate, Error>>
        FetchAllHrPeople: unit -> Async<Result<seq<HrPerson>, Error>>
        UpdateHrPeople: seq<HrPerson> -> Async<Result<unit, Error>>
        SyncDepartments: unit -> Async<Result<unit, Error>>
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
        
    [<CLIMutable>]
    type ProfileReponse =
      { page: ProfilePage 
        employees: seq<ProfileEmployee>
        affiliates: seq<ProfileEmployee>
        foundations: seq<ProfileEmployee> }

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

    let concatResult s1 r2 = 
        match r2 with
        | Ok(s2) -> Seq.append s1 s2 |> Ok
        | _ -> r2
    
    let concatReslts r1 r2 =
        match (r1, r2) with
        | (Ok(s1), Ok(s2)) -> Seq.append s1 s2 |> Ok
        | (Error(e1), _) -> Error e1
        | (_, Error(e2)) -> Error e2

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

    let getProfilePage hrDataUrl affiliationType token page = 
        let uri = sprintf "%s?affiliationType=%s&page=%d&pageSize=7500" hrDataUrl affiliationType page |> Uri
        let req = new HttpRequestMessage(HttpMethod.Get, uri)
        req.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)
        sendAsync<ProfileReponse> req

        
    let getAllEmployeesOfType hrDataUrl (jwt:JwtResponse) affiliationType =
        let concatAll (resp:ProfileReponse) =
            (if isNull resp.affiliates then Seq.empty<ProfileEmployee> else resp.affiliates)
            |> Seq.append (if isNull resp.employees then Seq.empty<ProfileEmployee> else resp.employees) 
            |> Seq.append (if isNull resp.foundations then Seq.empty<ProfileEmployee> else resp.foundations)
        // recursively page through all employees
        let rec getAllEmployeesOfType  page = async {
            // get the requested page of employees
            match! getProfilePage hrDataUrl affiliationType jwt.access_token page with
            | Ok(resp) ->
                sprintf "Fetched page %d from HR source." page |> consoleLog
                // if this is the last page, return the set to caller
                sprintf "\n\ttot: %d\n\tcur: %s\n\tlst: %s" resp.page.totalRecords resp.page.currentPage resp.page.lastPage |> consoleLog
                if resp.page.currentPage = resp.page.lastPage
                then return resp |> concatAll |> Ok
                else
                    // recurse
                    let! next = getAllEmployeesOfType (page+1)
                    // return the combined sequences, shortcircuiting on error.
                    return concatResult (concatAll resp) next
            | Error(msg) -> return Error(msg)
        }
        // fetch first page and kick off recursion
        getAllEmployeesOfType 0

    let getAllEmployees hrDataUrl (jwt:JwtResponse) = async {
        "Fetching affiliates from HR source..." |> consoleLog
        let! affiliates = getAllEmployeesOfType hrDataUrl jwt "affiliate"
        "Fetching foundation folk from HR source..." |> consoleLog
        let! foundation = getAllEmployeesOfType hrDataUrl jwt "foundation"
        "Fetching employees from HR source..." |> consoleLog
        let! employees = getAllEmployeesOfType hrDataUrl jwt "employee"
        return 
            employees
            |> concatReslts affiliates
            |> concatReslts foundation
    }

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
              NameFirst=e.firstName
              NameLast=e.lastName
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
            LEFT JOIN departments d on d.id = p.department_id
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
                name_first = @NameFirst,
                name_last = @NameLast,
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
            sprintf "%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n" p.Name p.NameFirst p.NameLast p.NetId p.Position p.Campus p.CampusPhone p.CampusEmail p.HrDepartment p.HrDepartmentDescription
        executeRaw psqlConnStr (fun cn ->
            cn.Open()
            // truncate the existing
            cn.Execute("DELETE FROM hr_people;") |> ignore
            // bulk insert the new rows
            use writer = cn.BeginTextImport("COPY hr_people (name, name_first, name_last, netid, position, campus, campus_phone, campus_email, hr_department, hr_department_desc) FROM STDIN")
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

    let fetchAllBuildings url user password =
        getDenodoResponse url user password
        >>= mapToDomainBuilding
    
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
        

    let Repository psqlConnStr uaaUrl hrDataUrl adUser adPassword uaaUser uaaPassword buildingUrl buildingUser buildingPassword =
     { GetAllNetIds = fun () -> getAllNetIds psqlConnStr
       FetchLatestPersonData = fetchLatestPersonData psqlConnStr
       UpdatePerson = updatePerson psqlConnStr
       GetAllTools = fun () -> getAllTools psqlConnStr 
       GetADGroupMembers = getADGroupMembers adUser adPassword 
       GetAllToolUsers = getAllToolUsers psqlConnStr 
       UpdateADGroup = updateADGroup adUser adPassword
       FetchAllHrPeople = fetchAllHrPeople uaaUrl hrDataUrl uaaUser uaaPassword
       UpdateHrPeople = updateHrPeople psqlConnStr
       SyncDepartments = fun () -> syncDepartments psqlConnStr 
       FetchAllBuildings = fun () -> fetchAllBuildings buildingUrl buildingUser buildingPassword
       UpdateBuildings = updateBuildings psqlConnStr }

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

    open Core.Util
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

        let workflow =
            data.FetchAllBuildings
            >=> tap logBuildingCount
            >=> data.UpdateBuildings

        execute workflow ()

    // Enqueue the netids of all the people for whom we need to update
    // canonical HR data.
    // [<Disable>]
    [<FunctionName("PeopleUpdateHrTable")>]
    let peopleUpdateHrTable
        ([<TimerTrigger("0 */15 * * * *")>] timer: TimerInfo,
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
    // [<Disable>]
    [<FunctionName("PeopleUpdateWorker")>]
    let peopleUpdateWorker
        ([<QueueTrigger("people-update")>] netid: string,
         [<Queue("people-update-notification")>] queue: ICollector<string>,
         log: ILogger) =

        let logUpdateAttempt (person:HrPerson) =
            person
            |> sprintf "Updating directory record with HR data %A."
            |> log.LogInformation
            ok person

        let logUpdateSuccess (person:Person) = 
            person
            |> sprintf "Updated directory record as %A."
            |> log.LogInformation
            ok person

        let logHrDataNotFound (person:Person) = 
            person.NetId
            |> sprintf "HR data not found for %s. The directory record for this netid should be removed."
            |> log.LogInformation
            ok person

        let logDepartmentChange (person:Person) (hrPerson:HrPerson)=
            sprintf "HR department has changed for %s. Directory record is %A. HR Record is %A. The unit memberships and tool assignments for this person should be revoked." person.NetId person hrPerson
            |> log.LogInformation

        let logPositionChange (person:Person) (hrPerson:HrPerson)=
            sprintf "Postion has changed for %s. Directory record is %A. HR Record is %A. The unit memberships and tool assignments for this person should be revoked." person.NetId person hrPerson
            |> log.LogInformation

        let departmentHasChanged (person:Person) (hrPerson:HrPerson) =
            (not(isNull(box(person.Department))) 
                && hrPerson.HrDepartment <> person.Department.Name)

        let positionHasChanged (person:Person) (hrPerson:HrPerson) =
            hrPerson.Position <> person.Position         

        let updateDirectoryRecord hrPerson =
            logUpdateAttempt hrPerson
            >>= data.UpdatePerson
            >>= logUpdateSuccess

        let processHRResult (person:Person, hrPersonOpt:HrPerson option) =
            match hrPersonOpt with
            // The person has changed HR Departments
            | Some(hrPerson) when departmentHasChanged person hrPerson ->
                logDepartmentChange person hrPerson
                updateDirectoryRecord hrPerson
            // The person has changed positions
            | Some(hrPerson) when positionHasChanged person hrPerson ->
                logPositionChange person hrPerson
                updateDirectoryRecord hrPerson
            // The person is still in the same role
            | Some(hrPerson) ->
                updateDirectoryRecord hrPerson
            // The person is no longer in the HR data eed
            | None -> logHrDataNotFound person

        let workflow = 
            data.FetchLatestPersonData
            >=> processHRResult

        sprintf "Processing person update for netid %s" netid |> log.LogInformation
        execute workflow netid

        // Enqueue the tools for which permissions need to be updated.
    // [<Disable>]
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
    // [<Disable>]
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