namespace Functions.Common

open Chessie.ErrorHandling
open Types
open Util
open Dapper
open Npgsql
open Newtonsoft.Json

module Database =

    type IdFilter = { Id: Id }
    type NetIdFilter = { NetId: NetId }
    type SimpleSearchQuery = { Term: string }

    let private like (term:string)  = 
        term.Replace("[", "[[]").Replace("%", "[%]") 
        |> sprintf "%%%s%%"

    let sqlConnection connectionString =
        SimpleCRUD.SetDialect(SimpleCRUD.Dialect.PostgreSQL)
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores <- true
        new NpgsqlConnection(connectionString)

    let private queryAll'<'T> connStr (query:string) description = asyncTrial {
        let fn() = async {
            use cn = sqlConnection connStr
            let! seq = cn.QueryAsync<'T>(query) |> Async.AwaitTask
            return ok seq
        }
        let errMsg = sprintf "Error when executing '%s'" description
        return! tryfAsync Status.InternalServerError errMsg fn
    }

    /// Fetch a user given a netid (e.g. 'jhoerr')
    let queryPersonByNetId connStr netId = asyncTrial {
        let idParam = {NetId=netId}
        let query = "SELECT * FROM people WHERE netid = @NetId"
        let fn () = async {
            use cn = sqlConnection connStr
            let! seq = cn.QuerySingleOrDefaultAsync<Person>(query, idParam) |> Async.AwaitTask
            return ok seq
        }
        return! tryfAsync Status.InternalServerError "Failed to fetch user by netId" fn
    }

    /// Get the profile for a given user ID
    let queryUserProfile connStr id = asyncTrial {
        let idParam = {Id=id}
        let query = """
-- The person
SELECT * FROM people WHERE id = @Id;
-- Units to which this person belongs
SELECT m.unit_id AS unit_id, m.person_id AS person_id, m.title, m.role, m.percentage, m.tools, u.name, p.photo_url, u.description
FROM unit_members m
JOIN units u ON u.id = m.unit_id
JOIN people p on p.id = m.person_id
WHERE m.person_id = @Id
ORDER BY m.Role DESC, u.Name ASC;
-- The person's HR department
SELECT d.* FROM people p
JOIN departments d ON d.id = p.department_id
WHERE p.id = @Id;
"""
        let fn() = async {
            use cn = sqlConnection connStr
            let! result = cn.QueryMultipleAsync(query, idParam) |> Async.AwaitTask
            let! user = result.ReadSingleOrDefaultAsync<Person>() |> Async.AwaitTask
            let! units = result.ReadAsync<UnitMember>() |> Async.AwaitTask
            let! department = result.ReadSingleOrDefaultAsync<Department>() |> Async.AwaitTask
            return ok (user, units, department)
        }
        let! (user, units, department) = fn |> tryfAsync Status.InternalServerError "Query error: queryPersonProfile"
        let expertise = if isNull user.Expertise then [""] else (user.Expertise.Split("|") |> Array.toList) 
        let responsibilities = user.Responsibilities |> mapFlagsToSeq
        let tools = user.Tools |> mapFlagsToSeq
        let unitMemberships = units |> Seq.map (fun m -> 
              { Id=m.UnitId 
                Name=m.Name
                Description=m.Description
                Title=m.Title
                Role=m.Role
                Percentage=m.Percentage
                PhotoUrl=m.PhotoUrl
                Tools=m.Tools |> mapFlagsToSeq
              })
        return {
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
            Department=department
            UnitMemberships=unitMemberships
        }
    }

    /// Get all people, departments, and units whose name matches a given search term
    let querySimpleSearch connStr term = asyncTrial {
        let likeParam = {Term=like term}
        let query = """
-- Search units
SELECT id, name, description FROM units WHERE name ILIKE @Term OR description ILIKE @Term ORDER BY name ASC;
-- Search depatments
SELECT id, name, description FROM departments WHERE name ILIKE @Term OR description ILIKE @Term ORDER BY name ASC;
-- Search people
SELECT id, name, netid AS description FROM people WHERE name ILIKE @Term OR netid ILIKE @Term ORDER BY name ASC;
"""
        let fn() = async {
            use cn = sqlConnection connStr
            let! result = cn.QueryMultipleAsync(query, likeParam) |> Async.AwaitTask
            let! units = result.ReadAsync<Entity>() |> Async.AwaitTask
            let! depts = result.ReadAsync<Entity>() |> Async.AwaitTask
            let! people = result.ReadAsync<Entity>() |> Async.AwaitTask
            return ok { Users=people; Units=units; Departments=depts }
        }
        return! fn |> tryfAsync Status.InternalServerError "Query error: querySimpleSearch"
    }

    /// Get a list of all units
    let queryUnits connStr = asyncTrial {
        return! queryAll'<Unit> connStr "SELECT * from units" "queryAllUnits"
    }

    /// Get a single unit by ID
    let queryUnit connStr id = asyncTrial {
        let idParam = {Id=id}
        let query = """
-- The unit
SELECT * FROM units WHERE id = @Id;
-- The people in this unit
SELECT m.unit_id, m.person_id, m.title, m.role, m.percentage, m.tools, p.name, p.photo_url, p.netid as description
FROM unit_members m
JOIN units u ON u.id = m.unit_id
JOIN people p on p.id = m.person_id
WHERE m.unit_id = @Id
ORDER BY m.Role DESC, p.Name ASC;
-- The departments supported by this unit
SELECT d.id, d.name, d.description 
FROM departments d
JOIN supported_departments sd on d.id = sd.department_id 
WHERE sd.unit_id = @Id
GROUP BY d.id, d.name, d.description
ORDER BY d.name ASC;
-- The parent of this unit
SELECT up.id , up.name, up.description, up.url
FROM unit_relations ur
LEFT JOIN units up on up.id = ur.parent_id
WHERE ur.child_id = @Id
ORDER BY up.name ASC;
-- The children of this unit
SELECT up.id , up.name, up.description, up.url
FROM unit_relations ur
LEFT JOIN units up on up.id = ur.child_id
WHERE ur.parent_id = @Id
ORDER BY up.name ASC;
"""
        let fn() = async {
            use cn = sqlConnection connStr
            let! result = cn.QueryMultipleAsync(query, idParam) |> Async.AwaitTask
            let! unit = result.ReadSingleOrDefaultAsync<Unit>() |> Async.AwaitTask
            let! members = result.ReadAsync<UnitMember>() |> Async.AwaitTask
            let! supportedDepartments = result.ReadAsync<Department>() |> Async.AwaitTask
            let! parent = result.ReadSingleOrDefaultAsync<Unit>() |> Async.AwaitTask
            let! children = result.ReadAsync<Unit>() |> Async.AwaitTask
            return ok (unit, members, supportedDepartments, parent, children)
        }
        let! (unit, members, supportedDepartments, parent, children) = 
            fn |> tryfAsync Status.InternalServerError "Query error: queryUnit"
        let memberships = members |> Seq.map (fun m -> 
              { Id=m.PersonId 
                Name=m.Name
                Description=m.Description
                Title=m.Title
                Role=m.Role
                Percentage=m.Percentage
                PhotoUrl=m.PhotoUrl
                Tools=m.Tools |> mapFlagsToSeq
              })
        return {
            Id=unit.Id
            Name=unit.Name
            Description=unit.Description
            Url=unit.Url
            Members=memberships
            SupportedDepartments=supportedDepartments
            Children=children
            Parent=Some(parent)
        }
    }

    /// Get a list of all departments
    let queryDepartments connStr = asyncTrial {
        return! queryAll'<Department> connStr "SELECT * from departments" "queryAllDepartments"
    }

    /// Get a single department by ID
    let queryDepartment connStr id = asyncTrial {
        let idParam = {Id=id}
        let query = """
-- The department
SELECT * FROM departments WHERE id = @Id;
-- The people in this department
SELECT p.id, p.name, p.netid as description 
FROM people p
WHERE p.department_id = @Id
ORDER BY p.name ASC;
-- The units in this department
SELECT u.id, u.name, u.description, u.url
FROM units u
JOIN unit_members m on u.id = m.unit_Id
JOIN people p on p.Id = m.person_id
WHERE p.department_id = @Id 
GROUP BY u.id, u.name, u.description
ORDER BY u.name ASC;
-- The units supporting this department
SELECT u.id, u.name, u.description, u.url
FROM units u
JOIN supported_departments sd on u.id = sd.unit_id
WHERE sd.department_id = @Id 
GROUP BY u.id, u.name, u.description
ORDER BY u.name ASC;
"""
        let fn() = async {
            use cn = sqlConnection connStr
            let! result = cn.QueryMultipleAsync(query, idParam) |> Async.AwaitTask
            let! dept = result.ReadSingleOrDefaultAsync<Department>() |> Async.AwaitTask
            let! members = result.ReadAsync<Entity>() |> Async.AwaitTask
            let! unitsInDept = result.ReadAsync<Unit>() |> Async.AwaitTask
            let! unitsSupportingDept = result.ReadAsync<Unit>() |> Async.AwaitTask
            return ok (dept, members, unitsInDept, unitsSupportingDept)
        }
        let! (dept, members, unitsInDepartment, unitsSupportingDept) = 
            fn |> tryfAsync Status.InternalServerError "Query error: queryDepartment"
        return {
            Id=dept.Id
            Name=dept.Name
            Description=dept.Description
            Units = unitsInDepartment
            Members = members
            SupportingUnits=unitsSupportingDept
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