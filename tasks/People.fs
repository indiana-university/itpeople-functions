namespace Tasks

module People =

    open Core.Types
    open Core.Util
    open Database.Command
    open Dapper
    open System
    open System.Net.Http
    open System.Net.Http.Headers
    open Microsoft.Azure.WebJobs
    open Microsoft.Extensions.Logging

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

    let private getUaaToken (uaaUrl:string) username password =
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

    let private getProfilePage hrDataUrl affiliationType token page = 
        let uri = sprintf "%s?affiliationType=%s&page=%d&pageSize=7500" hrDataUrl affiliationType page |> Uri
        let req = new HttpRequestMessage(HttpMethod.Get, uri)
        req.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)
        sendAsync<ProfileReponse> req

        
    let private getAllEmployeesOfType hrDataUrl (jwt:JwtResponse) affiliationType =
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

    let private getAllEmployees hrDataUrl (jwt:JwtResponse) = async {
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

    let private mapEmployeesToDomainRecords (list:seq<ProfileEmployee>) = 
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
    let private fetchAllHrPeople uaaUrl hrDataUrl uaaUsername uaaPassword = pipeline {
        let! uaaToken = getUaaToken uaaUrl uaaUsername uaaPassword
        let! employees = getAllEmployees hrDataUrl uaaToken
        return! mapEmployeesToDomainRecords employees
    }

    let private syncDepartments connStr =
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

    let private updateHrPeople psqlConnStr (hrPeople:seq<HrPerson>) =
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
    let private getAllNetIds connStr =
        let sql = "SELECT netid FROM people;"
        fetch (fun cn -> cn.QueryAsync<NetId>(sql)) connStr

    let private fetchLatestPersonData connStr netid = async {
        let queryPersonSql = """
            SELECT DISTINCT p.*, d.*
            FROM people p
            LEFT JOIN departments d on d.id = p.department_id
            WHERE netid=@NetId"""
        let queryHrPersonSql = """
            SELECT * FROM hr_people WHERE netid=@NetId"""        
        let mapper (p:Person) d = {p with Department=d}
        let param = {NetId = netid}
        let! personSeq = fetch (fun cn -> cn.QueryAsync<Person, Department, Person>(queryPersonSql, mapper, param)) connStr
        let! hrPersonSeq = fetch (fun cn -> cn.QueryAsync<HrPerson>(queryHrPersonSql, param)) connStr
        return
            match (personSeq, hrPersonSeq) with
            | Error(msg), _ -> Error(msg)
            | _, Error(msg) -> Error(msg)
            | Ok(p), Ok(hr) -> Ok (p |> Seq.head, hr |> Seq.tryHead)
    }

    let private updatePersonRecord connStr (person:HrPerson) = 
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

    let updateHrTable (log:ILogger) (queue:ICollector<string>) connStr hrDataUrl uaaUrl uaaUser uaaPassword = pipeline {
        let! hrPeople = fetchAllHrPeople uaaUrl hrDataUrl uaaUser uaaPassword
        do! updateHrPeople connStr hrPeople 
        do! syncDepartments connStr
        let! netids = getAllNetIds connStr
        netids |> Seq.iter queue.Add
        sprintf "Enqueued %d netids for update." (Seq.length netids) |> log.LogInformation
        return ()
    }
  
    let updatePerson (log:ILogger) netid connStr = pipeline {

        let logStart () =
            sprintf "Processing person update for netid %s" netid 
            |> log.LogInformation

        let logUpdateAttempt (person:HrPerson) =
            person
            |> sprintf "Updating directory record with HR data %A."
            |> log.LogInformation

        let logUpdateSuccess (person:Person) = 
            person
            |> sprintf "Updated directory record as %A."
            |> log.LogInformation

        let logHrDataNotFound (person:Person) = 
            person.NetId
            |> sprintf "HR data not found for %s. The directory record for this netid should be removed."
            |> log.LogInformation

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

        let updateDirectoryRecord hrPerson = pipeline {
            logUpdateAttempt hrPerson
            let! person = updatePersonRecord connStr hrPerson
            logUpdateSuccess person
            return ()
        }

        let noOp = pipeline { return () }

        logStart ()
        let! (person, hrPersonOpt) = fetchLatestPersonData connStr netid
        do! match hrPersonOpt with
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
            | None -> 
                logHrDataNotFound person
                noOp                
        return ()    
    }
