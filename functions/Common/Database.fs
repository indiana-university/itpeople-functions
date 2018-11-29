namespace Functions.Common

open Chessie.ErrorHandling
open Types
open Util
open Dapper
open Npgsql
open Newtonsoft.Json

module Database =
    let private like (term:string)  = 
        term.Replace("[", "[[]").Replace("%", "[%]") 
        |> sprintf "%%%s%%"

    type IdFilter = {
        Id: Id
    }

    type NetIdFilter = {
        NetId: NetId
    }

    let sqlConnection connectionString =
        new NpgsqlConnection(connectionString)

    /// Fetch a user given a netid (e.g. 'jhoerr')
    let queryPersonByNetId connStr netId = asyncTrial {
        let fn () = async {
            use cn = sqlConnection connStr
            let! seq = cn.GetListAsync<Person>({NetId=netId}) |> Async.AwaitTask
            return ok seq
        }
        let! result = tryfAsync Status.InternalServerError "Failed to fetch user by netId" fn
        let! head = tryGetFirst result (sprintf "No user found with netid '%s'" netId)
        return head
    }

    /// Fetch a single 'T given an ID
    let queryTypeById<'T> connStr id = asyncTrial {
        let fn () = async {
            use cn = sqlConnection connStr
            let! result = cn.GetAsync<'T>(id) |> Async.AwaitTask
            match box result with
            | null -> return fail (Status.NotFound, sprintf "No %s found with id %d" (typeof<'T>.Name) id)
            | _ -> return ok result
        }
        return! tryfAsync Status.InternalServerError (sprintf "Failed to fetch %s by id %d" (typeof<'T>.Name) id) fn
    }

    let queryAll'<'T> connStr (query:string) description = asyncTrial {
        let fn() = async {
            use cn = sqlConnection connStr
            let! seq = cn.QueryAsync<'T>(query) |> Async.AwaitTask
            return ok seq
        }
        let errMsg = sprintf "Error when executing '%s'" description
        return! tryfAsync Status.InternalServerError errMsg fn
    }

    let queryAll<'T> connStr query description (parameters:obj) = asyncTrial {
        let fn() = async {
            use cn = sqlConnection connStr
            let! seq = cn.QueryAsync<'T>(query, parameters) |> Async.AwaitTask
            return ok seq
        }
        let errMsg = sprintf "Error when executing '%s'" description
        return! tryfAsync Status.InternalServerError errMsg fn
    }

    /// Fetch all 'T
    let queryAllById<'T> connStr query description id = asyncTrial {
        return! queryAll<'T> connStr query description {Id=id}
    }

    /// Fetch one or no 'T
    let queryOneById<'T> connStr query msg id = asyncTrial {
        let! result = queryAllById<'T> connStr query msg id
        return result |> Seq.tryHead
    }

    /// Get all departments supported by a given unit ID
    let queryDepartmentsSupportedByUnit connStr unitId = asyncTrial {
        let query = """
SELECT d.id, d.name, d.description 
FROM departments d
JOIN supported_departments sd on d.id = sd.department_id 
WHERE sd.unit_id = @Id
GROUP BY d.id, d.name, d.description
ORDER BY d.name ASC"""
        return! queryAllById<Department> connStr query "queryDepartmentsSupportedByUnit" unitId
    }

    /// Get all organizational units that exist within a given department ID
    let queryOrgUnitsInDepartment connStr deptId = asyncTrial {
        let query = """
SELECT u.id, u.name, u.description, u.url
FROM units u
JOIN unit_members m on u.id = m.unit_Id
JOIN people p on p.Id = m.person_id
WHERE p.department_id = @Id 
GROUP BY u.id, u.name, u.description
ORDER BY u.name ASC"""
        return! queryAllById<Unit> connStr query "queryOrgUnitsInDepartment" deptId
    }

    /// Get all supporting units for a given department ID

    let queryUnitsSupportingDepartment connStr deptId = asyncTrial {
        let query = """
SELECT u.id, u.name, u.description, u.url
FROM units u
JOIN supported_departments sd on u.id = sd.unit_id
WHERE sd.department_id = @Id 
GROUP BY u.id, u.name, u.description
ORDER BY u.name ASC"""
        return! queryAllById<Unit> connStr query "queryUnitsSupportingDepartment" deptId
    }

    /// Get all people with an HR relationship to a given department ID

    let queryPeopleInDepartment connStr deptId = asyncTrial {
        let query = """
SELECT p.id, p.name, p.netid as description 
FROM people p
WHERE p.department_id = @Id
ORDER BY p.name ASC"""
        return! queryAllById<Member> connStr query "queryPeopleInDepartment" deptId
    }

    let mapUnitMemberRecordsToDto members = 
        members 
        |> Seq.map (fun m -> {
            Id=m.PersonId 
            Name=m.Name
            Description=m.Description
            Title=m.Title
            Role=m.Role
            Percentage=m.Percentage
            PhotoUrl=m.PhotoUrl
            Tools=m.Tools |> mapFlagsToSeq
        })


    /// Get all people with a relationship to a given unit ID
    let queryPeopleInUnit connStr unitId = asyncTrial {
        let query = """
SELECT u.id as unit_id, m.person_id, p.name, m.title, m.role, m.percentage, m.tools, p.photo_url, p.netid as description
FROM units u
LEFT JOIN unit_members m ON u.id = m.unit_id
LEFT JOIN people p on p.id = m.person_id
WHERE m.unit_id = @Id
ORDER BY m.Role DESC, p.Name ASC """
        let! members = queryAllById<UnitMember> connStr query "queryPeopleInUnit" unitId
        return members |> mapUnitMemberRecordsToDto
    }

    /// Get all units with a relationship to a given person ID
    let queryUnitMemberships connStr userId = asyncTrial {
        let query = """
SELECT u.id as unit_id, m.person_id, u.name, m.title, m.role, m.percentage, m.tools, p.photo_url, u.description
FROM units u
LEFT JOIN unit_members m ON u.id = m.unit_id
LEFT JOIN people p on p.id = m.person_id
WHERE m.person_id = @Id
ORDER BY m.Role DESC, p.Name ASC"""
        let! members = queryAllById<UnitMember> connStr query "queryUnitMemberships" userId
        return members |> mapUnitMemberRecordsToDto
    }

    /// Get all units with a relationship to a given person ID
    let queryUnitParent connStr unitId = asyncTrial {
        let query = """
SELECT up.id , up.name, up.description, up.url
FROM unit_relations ur
LEFT JOIN units up on up.id = ur.parent_id
WHERE ur.child_id = @Id
ORDER BY up.name ASC"""
        return! queryOneById<Unit> connStr query "queryUnitParent" unitId
    }

        /// Get all units with a relationship to a given person ID
    let queryUnitChildren connStr unitId = asyncTrial {
        let query = """
SELECT up.id , up.name, up.description, up.url
FROM unit_relations ur
LEFT JOIN units up on up.id = ur.child_id
WHERE ur.parent_id = @Id
ORDER BY up.name ASC"""
        return! queryAllById<Unit> connStr query "queryUnitChildren" unitId
    }
        
    /// Get the profile for a given user ID
    let queryUserProfile connStr id = asyncTrial {
        let! user = queryTypeById<Person> connStr id
        let! unitMemberships = queryUnitMemberships connStr id
        let! dept = queryTypeById<Department> connStr user.HrDepartmentId
        let expertise = if isNull user.Expertise then [""] else (user.Expertise.Split("|") |> Array.toList) 
        let responsibilities = user.Responsibilities |> mapFlagsToSeq
        let tools = user.Tools |> mapFlagsToSeq
        let profile = {
            PersonDto.Id=user.Id
            NetId=user.NetId
            Name=user.Name
            Position=user.Position
            Location=user.Location
            Campus=user.Campus
            CampusEmail=user.CampusEmail
            CampusPhone=user.CampusPhone
            Expertise=expertise
            Notes=user.Notes
            PhotoUrl=user.PhotoUrl
            Responsibilities=responsibilities
            Tools=tools
            Department=dept
            UnitMemberships=unitMemberships
        }       
        return profile
    }

    type SimpleSearchQuery = {
        Term: string
    }

    /// Get all people, departments, and units whose name matches a given search term
    let querySimpleSearch connStr term = asyncTrial {
        let likeParam = {Term=like term}
        let! units = queryAll<Entity> connStr "SELECT id, name, description FROM units WHERE name ILIKE @Term OR description ILIKE @Term ORDER BY name ASC" "searchUnits" likeParam
        let! depts = queryAll<Entity> connStr "SELECT id, name, description FROM departments WHERE name ILIKE @Term OR description ILIKE @Term ORDER BY name ASC" "searchDepartments" likeParam
        let! people = queryAll<Entity> connStr "SELECT id, name, netid AS description FROM people WHERE name ILIKE @Term OR netid ILIKE @Term ORDER BY name ASC" "searchPeople" likeParam
        return { Users=people; Units=units; Departments=depts }
    }

    /// Get a list of all units
    let queryUnits connStr = asyncTrial {
        return! queryAll'<Unit> connStr "SELECT * from units" "queryAllUnits"
    }

    /// Get a single unit by ID
    let queryUnit connStr id = asyncTrial {
        let! unit = queryTypeById<Unit> connStr id
        let! members = queryPeopleInUnit connStr id
        let! supportedDepartments = queryDepartmentsSupportedByUnit connStr id
        let! parent = queryUnitParent connStr id
        let! children = queryUnitChildren connStr id
        return {
            Id=unit.Id
            Name=unit.Name
            Description=unit.Description
            Url=unit.Url
            Members=members
            SupportedDepartments=supportedDepartments
            Children=children
            Parent=parent
        }
    }

    /// Get a list of all departments
    let queryDepartments connStr = asyncTrial {
        return! queryAll'<Department> connStr "SELECT * from departments" "queryAllDepartments"
    }

    /// Get a single department by ID
    let queryDepartment connStr id = asyncTrial {
        let! dept = queryTypeById<Department> connStr id
        let! members = queryPeopleInDepartment connStr id
        let! orgUnits = queryOrgUnitsInDepartment connStr id
        let! supportingUnits = queryUnitsSupportingDepartment connStr id
        return {
            Id=dept.Id
            Name=dept.Name
            Description=dept.Description
            Units = orgUnits
            Members = members
            SupportingUnits=supportingUnits
        }
    }

    /// A SQL Database implementation of IDatabaseRespository
    type DatabaseRepository(connectionString:string) =
        let connStr = connectionString

        interface IDataRepository with 
            member this.GetUserByNetId netId = queryPersonByNetId connStr netId
            member this.GetProfile id = queryUserProfile connStr id
            member this.GetSimpleSearchByTerm term = querySimpleSearch connStr term
            member this.GetUnits () = queryUnits connStr
            member this.GetUnit id = queryUnit connStr id
            member this.GetDepartments () = queryDepartments connStr 
            member this.GetDepartment id = queryDepartment connStr id