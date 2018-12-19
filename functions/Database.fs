// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open Types
open Util
open Json
open Chessie.ErrorHandling
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

    let dbFail description (exn:System.Exception) =  
        let msg = sprintf "Query error on '%s': %s" description exn.Message
        fail (Status.InternalServerError, msg)

    let private queryAll'<'T> connStr (query:string) description = async {
        try
            use cn = sqlConnection connStr
            let! result = cn.QueryAsync<'T>(query) |> awaitTask
            return ok result
        with exn -> return dbFail description exn
    }

    /// Fetch a user given a netid (e.g. 'jhoerr')
    let queryPersonByNetId connStr netId = async {
        let idParam = {NetId=netId}
        let query = "SELECT id FROM people WHERE netid = @NetId"
        try
            use cn = sqlConnection connStr
            let! id = cn.QuerySingleOrDefaultAsync<int>(query, idParam) |> awaitTask
            return (netId, id) |> ok
        with exn -> return dbFail "queryPersonByNetId" exn
    }

    /// Get the profile for a given user ID
    let queryUserProfile connStr id = async {
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
        try
            use cn = sqlConnection connStr
            let! result = cn.QueryMultipleAsync(query, idParam) |> awaitTask
            let! user = result.ReadSingleOrDefaultAsync<Person>() |> awaitTask
            let! units = result.ReadAsync<UnitMember>() |> awaitTask
            let! department = result.ReadSingleOrDefaultAsync<Department>() |> awaitTask
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
            return ok {
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
        with exn -> return dbFail "queryPersonProfile" exn

    }

    /// Get all people, departments, and units whose name matches a given search term
    let querySimpleSearch connStr term = async {
        let likeParam = {Term=like term}
        let query = """
-- Search units
SELECT id, name, description FROM units WHERE name ILIKE @Term OR description ILIKE @Term ORDER BY name ASC;
-- Search depatments
SELECT id, name, description FROM departments WHERE name ILIKE @Term OR description ILIKE @Term ORDER BY name ASC;
-- Search people
SELECT id, name, netid AS description FROM people WHERE name ILIKE @Term OR netid ILIKE @Term ORDER BY name ASC;
"""
        try
            use cn = sqlConnection connStr
            let! result = cn.QueryMultipleAsync(query, likeParam) |> awaitTask
            let! units = result.ReadAsync<Entity>() |> awaitTask
            let! depts = result.ReadAsync<Entity>() |> awaitTask
            let! people = result.ReadAsync<Entity>() |> awaitTask
            return ok { Users=people; Units=units; Departments=depts }
        with exn -> return dbFail "querySimpleSearch" exn
    }

    /// Get a list of all top-level units
    let queryUnits connStr = async {
        let query = """
SELECT * from units u
WHERE u.id NOT IN
( SELECT child_id from unit_relations );
"""
        return! queryAll'<Unit> connStr query "queryAllUnits"
    }

    /// Get a single unit by ID
    let queryUnit connStr id = async {
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
        try
            use cn = sqlConnection connStr
            let! result = cn.QueryMultipleAsync(query, idParam) |> awaitTask
            let! unit = result.ReadSingleOrDefaultAsync<Unit>() |> awaitTask
            let! members = result.ReadAsync<UnitMember>() |> awaitTask
            let! supportedDepartments = result.ReadAsync<Department>() |> awaitTask
            let! parent = result.ReadSingleOrDefaultAsync<Unit>() |> awaitTask
            let! children = result.ReadAsync<Unit>() |> awaitTask
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
            return ok {
                Id=unit.Id
                Name=unit.Name
                Description=unit.Description
                Url=unit.Url
                Members=memberships
                SupportedDepartments=supportedDepartments
                Children=children
                Parent=Some(parent)
            }
        with exn -> return dbFail "queryUnit" exn
    }

    /// Get a list of all departments
    let queryDepartments connStr = async {
        return! queryAll'<Department> connStr "SELECT * from departments" "queryAllDepartments"
    }

    /// Get a single department by ID
    let queryDepartment connStr id = async {
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
        try
            use cn = sqlConnection connStr
            let! result = cn.QueryMultipleAsync(query, idParam) |> awaitTask
            let! dept = result.ReadSingleOrDefaultAsync<Department>() |> awaitTask
            let! members = result.ReadAsync<Entity>() |> awaitTask
            let! unitsInDepartment = result.ReadAsync<Unit>() |> awaitTask
            let! unitsSupportingDept = result.ReadAsync<Unit>() |> awaitTask
            return ok 
              { Id=dept.Id
                Name=dept.Name
                Description=dept.Description
                Units = unitsInDepartment
                Members = members
                SupportingUnits=unitsSupportingDept }
        with exn -> return dbFail "queryDepartment" exn

    }

    /// A SQL Database implementation of IDatabaseRespository
    type DatabaseRepository(connectionString:string) =
        let connStr = connectionString

        interface IDataRepository with 
            member this.TryGetPersonId netId = queryPersonByNetId connStr netId
            member this.GetProfile id = queryUserProfile connStr id
            member this.GetSimpleSearchByTerm term = querySimpleSearch connStr term
            member this.GetUnits () = queryUnits connStr
            member this.GetUnit id = queryUnit connStr id
            member this.GetDepartments () = queryDepartments connStr 
            member this.GetDepartment id = queryDepartment connStr id