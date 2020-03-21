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

    open Logging

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
    
    let consoleLog msg = 
        printfn "%s %s" (DateTime.Now.ToLongTimeString()) msg

    let private getUaaToken (log:Serilog.ILogger) (uaaUrl:string) username password =
        log |> logInfo (sprintf "Fetching UAA token from %s for client id %s" uaaUrl username) None
        let content =
            dict [
                "grant_type", "client_credentials"
                "client_id", username
                "client_secret", password
            ]
            |> Collections.Generic.Dictionary
            |> (fun d-> new FormUrlEncodedContent(d))
        postAsync<JwtResponse> uaaUrl content

    let private getProfilePage (log:Serilog.ILogger) hrDataUrl affiliationType token page = 
        let uri = sprintf "%s?affiliationType=%s&page=%d&pageSize=7500" hrDataUrl affiliationType page
        log |> logDebug (sprintf "Fetching data from %O..." uri) None
        let req = new HttpRequestMessage(HttpMethod.Get, Uri(uri))
        req.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)
        sendAsync<ProfileReponse> req
        
    let private getAllEmployeesOfType log hrDataUrl (jwt:JwtResponse) affiliationType =
        let concatAll (resp:ProfileReponse) =
            (if isNull resp.affiliates then Seq.empty<ProfileEmployee> else resp.affiliates)
            |> Seq.append (if isNull resp.employees then Seq.empty<ProfileEmployee> else resp.employees) 
            |> Seq.append (if isNull resp.foundations then Seq.empty<ProfileEmployee> else resp.foundations)
        // recursively page through all employees
        let rec getAllEmployeesOfType  page = async {
            // get the requested page of employees
            match! getProfilePage log hrDataUrl affiliationType jwt.access_token page with
            | Ok(resp) ->
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

    let private getAllEmployees (log:Serilog.ILogger) hrDataUrl (jwt:JwtResponse) = pipeline {
        log |> logDebug "Fetching IU employees..." None
        let! employees = getAllEmployeesOfType log hrDataUrl jwt "employee"
        log |> logDebug (sprintf "Fetched %d employees." (Seq.length employees)) None
        log |> logDebug "Fetching affiliates..." None
        let! affiliates = getAllEmployeesOfType log hrDataUrl jwt "affiliate"
        log |> logDebug (sprintf "Fetched %d affiliates." (Seq.length affiliates)) None
        log |> logDebug "Fetching Foundation employees..." None
        let! foundation = getAllEmployeesOfType log hrDataUrl jwt "foundation"
        log |> logDebug (sprintf "Fetched %d Foundation employees." (Seq.length foundation)) None
        return employees |> Seq.append affiliates |> Seq.append foundation
    }

    let strOrNah str = if String.IsNullOrWhiteSpace(str) then "" else str
    let toDomainRecord e =
        let (position, deptName, deptDesc) = 
            match e.jobs |> Seq.tryFind (fun j -> j.jobStatus = "P") with
            | Some(job) -> (job.position, job.jobDepartmentId, job.jobDepartmentDesc)
            | None -> ("","","")
        let (phone, campus) = 
            match e.contacts |> Seq.tryHead with
            | Some(contact) -> (strOrNah contact.phoneNumber, strOrNah contact.campusCode)
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

    let private mapEmployeesToDomainRecords (log:Serilog.ILogger) (list:seq<ProfileEmployee>) = 
        log |> logInfo (sprintf "Fetched %d records from HR source." (Seq.length list)) None
        let validRecord (r:HrPerson) = 
            hasValue r.HrDepartment && hasValue r.CampusEmail 
        let domain = list |> Seq.map toDomainRecord 
        let dupes = 
            domain 
            |> Seq.countBy(fun r -> r.NetId)
            |> Seq.filter(fun (_,count) -> count > 1)
            |> Seq.map (fun (key,_) -> key)
        log |> logDebug (sprintf "Found %d duplicate netids." (Seq.length dupes)) (Some(dupes))
        let distinct = domain |> Seq.distinctBy (fun r -> r.NetId)
        log |> logDebug (sprintf "Found %d distinct netids." (Seq.length distinct)) None
        let invalid = distinct |> Seq.filter (validRecord >> not) |> Seq.map (fun r -> r.NetId) |> Seq.sort
        log |> logDebug (sprintf "Found %d invalid records due to missing email or HR dept." (Seq.length invalid)) (Some(invalid))
        let valid = distinct |> Seq.filter validRecord
        log |> logInfo (sprintf "Found %d valid records." (Seq.length valid)) None   
        valid |> ok

    // DENODO Stuff
    let private fetchAllHrPeople (log:Serilog.ILogger) uaaUrl hrDataUrl uaaUsername uaaPassword = pipeline {
        let! uaaToken = getUaaToken log uaaUrl uaaUsername uaaPassword
        let! employees = getAllEmployees log hrDataUrl uaaToken
        return! mapEmployeesToDomainRecords log employees
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

    let fetchLatestDirectoryPerson connStr netid = pipeline {
        let queryPersonSql = """
            SELECT DISTINCT p.*, d.*
            FROM people p
            LEFT JOIN departments d on d.id = p.department_id
            WHERE netid=@NetId"""
        let mapper (p:Person) d = {p with Department=d}
        let param = {NetId = netid}
        let! results = fetch (fun cn -> cn.QueryAsync<Person, Department, Person>(queryPersonSql, mapper, param)) connStr
        return results |> Seq.head
    }

    let private fetchLatestHrPerson connStr netid = pipeline {
        let queryHrPersonSql = "SELECT * FROM hr_people WHERE netid=@NetId"
        let param = {NetId = netid}
        let! results = fetch (fun cn -> cn.QueryAsync<HrPerson>(queryHrPersonSql, param)) connStr        
        return results |> Seq.tryHead
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
            WHERE netid = @NetId"""
        execute connStr sql person

    let updateHrTable (queue:ICollector<string>) connStr hrDataUrl uaaUrl uaaUser uaaPassword (log:Serilog.ILogger)= pipeline {
        let! hrPeople = fetchAllHrPeople log uaaUrl hrDataUrl uaaUser uaaPassword        
        log |> logInfo "Replacing hr_people data with latest records." None
        do! updateHrPeople connStr hrPeople 
        log |> logInfo "Syncing departments from hr_people records." None
        do! syncDepartments connStr
        let! netids = getAllNetIds connStr
        log |> logDebug (sprintf "Found %d netids in directory." (Seq.length netids)) None
        netids |> Seq.iter queue.Add
        log |> logInfo (sprintf "Enqueued %d netids for update." (Seq.length netids)) None
        return ()
    }
  
    let updatePerson netid connStr (log:Serilog.ILogger) = pipeline {

        let logStart () = 
            let msg = sprintf "Processing person update for %s." netid
            log |> logInfo msg None

        let logUpdateSuccess (person:Person) = 
            let msg = sprintf "Updated directory record for %s." netid
            log |> logInfo msg (Some(person))

        let logHrDataNotFound () =  
            let msg = sprintf "HR data not found for %s. They should be removed from the directory." netid
            log |> logWarn msg None

        let logDepartmentChange () =
            let msg = sprintf "HR department has changed for %s. Unit memberships and tool assignments should be revoked." netid
            log |> logWarn msg None

        let logPositionChange ()  =
            let msg = sprintf "Postion has changed for %s. Unit memberships and tool assignments should be revoked." netid
            log |> logWarn msg None

        let departmentHasChanged (person:Person) (hrPerson:HrPerson) =
            (not(isNull(box(person.Department))) 
                && hrPerson.HrDepartment <> person.Department.Name)

        let positionHasChanged (person:Person) (hrPerson:HrPerson) =
            hrPerson.Position <> person.Position         

        let updateDirectoryRecord hrPerson = pipeline {
            log |> logDebug "Updating from HR person" (Some(hrPerson))
            do! updatePersonRecord connStr hrPerson
            let! person = fetchLatestDirectoryPerson connStr netid
            logUpdateSuccess person
            return ()
        }

        logStart ()

        let! dirPerson = fetchLatestDirectoryPerson connStr netid
        let! hrPersonOpt = fetchLatestHrPerson connStr netid

        do match hrPersonOpt with
            | Some(hrPerson) when departmentHasChanged dirPerson hrPerson -> logDepartmentChange ()
            | Some(hrPerson) when positionHasChanged dirPerson hrPerson -> logPositionChange ()
            | None -> logHrDataNotFound ()
            | _ -> () // no meaningful changes

        do! match hrPersonOpt with
            | Some(hrPerson) -> updateDirectoryRecord hrPerson
            | None -> ok () // no hr data; nothing to do.                

        return ()    
    }
