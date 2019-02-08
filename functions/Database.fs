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


type OptionHandler<'T> () =
    inherit SqlMapper.TypeHandler<option<'T>> ()

    override __.SetValue (param, value) =
        let valueOrNull =
            match value with
            | Some x -> box x
            | None   -> null

        param.Value <- valueOrNull

    override __.Parse value =
        if 
            System.Object.ReferenceEquals(value, null) || 
            value = box System.DBNull.Value
        then None
        else Some (value :?> 'T)

module OptionHandler =
    
    let RegisterTypes () =
        SqlMapper.AddTypeHandler (OptionHandler<Id>())
        SqlMapper.AddTypeHandler (OptionHandler<UnitId>())
        SqlMapper.AddTypeHandler (OptionHandler<PersonId>())
        SqlMapper.AddTypeHandler (OptionHandler<DepartmentId>())
        SqlMapper.AddTypeHandler (OptionHandler<bool>())
        SqlMapper.AddTypeHandler (OptionHandler<byte>())
        SqlMapper.AddTypeHandler (OptionHandler<sbyte>())
        SqlMapper.AddTypeHandler (OptionHandler<int16>())
        SqlMapper.AddTypeHandler (OptionHandler<uint16>())
        SqlMapper.AddTypeHandler (OptionHandler<int32>())
        SqlMapper.AddTypeHandler (OptionHandler<uint32>())
        SqlMapper.AddTypeHandler (OptionHandler<int64>())
        SqlMapper.AddTypeHandler (OptionHandler<uint64>())
        SqlMapper.AddTypeHandler (OptionHandler<single>())
        SqlMapper.AddTypeHandler (OptionHandler<float>())
        SqlMapper.AddTypeHandler (OptionHandler<double>())
        SqlMapper.AddTypeHandler (OptionHandler<decimal>())
        SqlMapper.AddTypeHandler (OptionHandler<char>())
        SqlMapper.AddTypeHandler (OptionHandler<string>())
        SqlMapper.AddTypeHandler (OptionHandler<obj>())

module Database =

    type IdFilter = { Id: Id }
    type NetIdFilter = { NetId: NetId }
    type SimpleSearchQuery = { Term: string }


    let init() = 
        SimpleCRUD.SetDialect(SimpleCRUD.Dialect.PostgreSQL)
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores <- true
        OptionHandler.RegisterTypes()

    let private like (term:string)  = 
        term.Replace("[", "[[]").Replace("%", "[%]") 
        |> sprintf "%%%s%%"

    let sqlConnection connectionString =
        new NpgsqlConnection(connectionString)

    let dbFail operation resource (exn:System.Exception) =  
        let msg = sprintf "Database error on %s %s: %s" operation resource exn.Message
        fail (Status.InternalServerError, msg)

    let private queryAll'<'T> connStr (query:string) description = async {
        try
            use cn = sqlConnection connStr
            let! result = cn.QueryAsync<'T>(query) |> awaitTask
            return ok result
        with exn -> return dbFail "query all" (typedefof<'T>.Name) exn
    }


    let private queryOne<'T> connStr query parameters = async {
        try
            use cn = sqlConnection connStr
            let! result = cn.QueryAsync<'T>(query, parameters) |> awaitTask
            return 
                match result |> Seq.length with 
                | 1 -> result |> Seq.head |> Some |> ok
                | 0 -> fail (Status.NotFound, sprintf "No resource found matching query %A." parameters)
                | _ -> fail (Status.BadRequest, sprintf "More than one resource found matching query %A." parameters)
        with exn -> return dbFail "query one" (typedefof<'T>.Name) exn
    }

    let private queryExactlyOne<'T> connStr query parameters = async {
        try
            use cn = sqlConnection connStr
            let! result = cn.QueryAsync<'T>(query, parameters) |> awaitTask
            return 
                match result |> Seq.length with 
                | 1 -> result |> Seq.head |> ok
                | 0 -> fail (Status.NotFound, sprintf "No resource found matching query %A." parameters)
                | _ -> fail (Status.BadRequest, sprintf "More than one resource found matching query %A." parameters)
        with exn -> return dbFail "query one" (typedefof<'T>.Name) exn
    }

    let private insert<'T> connStr query (record:'T) (map:int -> 'T) = async {
        try
            use db = sqlConnection connStr
            let! id = db.ExecuteAsync(query, record) |> awaitTask
            return id |> map |> ok
        with exn -> return dbFail "insert " (typedefof<'T>.Name) exn
    }

    let private update<'T> connStr query (record:'T) = async {
        try
            use db = sqlConnection connStr
            let! _ = db.ExecuteAsync(query, record) |> awaitTask
            return ok record
        with exn -> return dbFail "update" (typedefof<'T>.Name) exn
    }

    let private delete<'T> connStr query (id:Id) = async {
        try
            use db = sqlConnection connStr
            let! _ = db.ExecuteAsync(query, {Id=id}) |> awaitTask
            return ok ()
        with exn -> return dbFail "delete" (typedefof<'T>.Name) exn
    }

    // **************
    // Authentication
    // **************

    let queryPersonByNetIdSql = "SELECT id FROM people WHERE netid = @NetId LIMIT 1"
    let queryPersonByNetId connStr netId = async {
        try
            use cn = sqlConnection connStr
            let! result = cn.QueryAsync<'T>(queryPersonByNetIdSql, {NetId=netId}) |> awaitTask
            return (netId, result |> Seq.tryHead) |> ok
        with exn -> return dbFail "query one" "person by netId " exn
    }

    // **********
    // Units
    // **********

    let mapUnit (unit:Unit) id = {unit with Id=id}

    let queryUnitsSql = """SELECT * FROM units"""
    let queryUnits connStr = async {
        return! queryAll'<Unit> connStr queryUnitsSql "queryUnits"
    }

    let queryUnitSql = """SELECT * FROM units WHERE id=@Id LIMIT 1"""
    let queryUnit connStr id = async {
        return! queryExactlyOne<Unit> connStr queryUnitSql {Id=id}
    }

    let insertUnitSql = """
        INSERT INTO units(name, description, url, parent_id)
        VALUES (@Name, @Description, @Url, @ParentId)"""
    let insertUnit connStr unit = async {
        return! insert<Unit> connStr insertUnitSql unit (mapUnit unit)
    }

    let updateUnitSql = """
        UPDATE units
        SET name=@Name, description=@Description, url=@Url, parent_id=@ParentId
        WHERE id=@Id"""
    let updateUnit connStr id unit = async {
        return! update<Unit> connStr updateUnitSql (mapUnit unit id)
    }

    let deleteUnitSql = """
        DELETE units
        WHERE id=@Id"""
    let deleteUnit connStr id = async {
        return! delete<Unit> connStr updateUnitSql id
    }

    // ***********
    // Departments
    // ***********

    let mapDepartment (department:Department) id = {department with Id=id}

    let queryDepartmentsSql = """SELECT * FROM departments"""
    let queryDepartments connStr = async {
        return! queryAll'<Department> connStr queryDepartmentsSql "queryDepartments"
    }

    let queryDepartmentSql = """SELECT * FROM Departments WHERE id=@Id LIMIT 1"""
    let queryDepartment connStr id = async {
        return! queryExactlyOne<Department> connStr queryDepartmentSql {Id=id}
    }

    let insertDepartmentSql = """
        INSERT INTO departments(name, description)
        VALUES (@Name, @Description)"""
    let insertDepartment connStr department = async {
        return! insert<Department> connStr insertDepartmentSql department (mapDepartment department)
    }

    let updateDepartmentSql = """
        UPDATE departments
        SET name=@Name, description=@Description
        WHERE id=@Id"""
    let updateDepartment connStr id department = async {
        return! update<Department> connStr updateUnitSql (mapDepartment department id)
    }


//     /// Get the profile for a given user ID
//     let queryUserProfile connStr id = async {
//         let idParam = {Id=id}
//         let query = """
// -- The person
// SELECT * FROM people WHERE id = @Id;
// -- Units to which this person belongs
// SELECT m.unit_id AS unit_id, m.person_id AS person_id, m.title, m.role, m.percentage, m.tools, u.name, p.photo_url, u.description
// FROM unit_members m
// JOIN units u ON u.id = m.unit_id
// JOIN people p on p.id = m.person_id
// WHERE m.person_id = @Id
// ORDER BY m.Role DESC, u.Name ASC;
// -- The person's HR department
// SELECT d.* FROM people p
// JOIN departments d ON d.id = p.department_id
// WHERE p.id = @Id;
// """
//         try
//             use cn = sqlConnection connStr
//             let! result = cn.QueryMultipleAsync(query, idParam) |> awaitTask
//             let! person = result.ReadSingleOrDefaultAsync<Person>() |> awaitTask
//             // let! units = result.ReadAsync<UnitMember>() |> awaitTask
//             // let! department = result.ReadSingleOrDefaultAsync<Department>() |> awaitTask
//             let expertise = if isNull person.Expertise then [""] else (person.Expertise.Split("|") |> Array.toList) 
//             let responsibilities = person.Responsibilities |> mapFlagsToSeq
//             let tools = person.Tools |> mapFlagsToSeq
//             return ok person
//         with exn -> return dbFail "" "queryPersonProfile" exn

//     }

//     /// Get a list of all departments
//     let queryDepartments connStr = async {
//         return! queryAll'<Department> connStr "SELECT * from departments" "queryAllDepartments"
//     }

//     /// Get a single department by ID
//     let queryDepartment connStr id = async {
//         let idParam = {Id=id}
//         let query = """
// -- The department
// SELECT * FROM departments WHERE id = @Id;
// -- The people in this department
// SELECT p.id, p.name, p.netid as description 
// FROM people p
// WHERE p.department_id = @Id
// ORDER BY p.name ASC;
// -- The units in this department
// SELECT u.id, u.name, u.description, u.url
// FROM units u
// JOIN unit_members m on u.id = m.unit_Id
// JOIN people p on p.Id = m.person_id
// WHERE p.department_id = @Id 
// GROUP BY u.id, u.name, u.description
// ORDER BY u.name ASC;
// -- The units supporting this department
// SELECT u.id, u.name, u.description, u.url
// FROM units u
// JOIN supported_departments sd on u.id = sd.unit_id
// WHERE sd.department_id = @Id 
// GROUP BY u.id, u.name, u.description
// ORDER BY u.name ASC;
// """
//         try
//             use cn = sqlConnection connStr
//             let! result = cn.QueryMultipleAsync(query, idParam) |> awaitTask
//             let! dept = result.ReadSingleOrDefaultAsync<Department>() |> awaitTask
//             // let! members = result.ReadAsync<MiniPerson>() |> awaitTask
//             // let! unitsInDepartment = result.ReadAsync<Unit>() |> awaitTask
//             // let! unitsSupportingDept = result.ReadAsync<Unit>() |> awaitTask
//             return ok dept
//         with exn -> return dbFail "" "queryDepartment" exn

//     }

    /// A SQL Database implementation of IDatabaseRespository
    type DatabaseRepository(connectionString:string) =
        let connStr = connectionString
        do init() 

        interface IDataRepository with 
            member this.TryGetPersonId netId = queryPersonByNetId connStr netId
            
            member this.GetPeople query = stub Seq.empty<Person>
            member this.GetPerson id = stub (Seq.empty<Person> |> Seq.head)
            member this.GetPersonMemberships personId = stub Seq.empty<UnitMember>
            
            member this.GetUnits query = queryUnits connStr
            member this.GetUnit id = queryUnit connStr id
            member this.GetUnitMembers id = stub Seq.empty<UnitMember>
            member this.GetUnitSupportedDepartments id = stub Seq.empty<SupportRelationship>
            member this.GetUnitChildren id = stub Seq.empty<Unit>
            member this.CreateUnit unit = insertUnit connStr unit
            member this.UpdateUnit id unit = updateUnit connStr id unit
            member this.DeleteUnit id = deleteUnit connStr id
            
            member this.GetDepartments query = queryDepartments connStr
            member this.GetDepartment id = queryDepartment connStr id
            member this.GetDepartmentMemberUnits id = stub Seq.empty<Unit>
            member this.GetDepartmentSupportingUnits id = stub Seq.empty<SupportRelationship>

            member this.GetMembership id = stub (Seq.empty<UnitMember> |> Seq.head)
            member this.GetMemberships () = stub Seq.empty<UnitMember> 
            member this.CreateMembership membership = stub membership
            member this.UpdateMembership id membership = stub membership
            member this.DeleteMembership id = stub ()
            
            member this.GetSupportRelationships () = stub Seq.empty<SupportRelationship> 
            member this.GetSupportRelationship id = stub (Seq.empty<SupportRelationship> |> Seq.head)
            member this.CreateSupportRelationship supportRelationship = stub supportRelationship
            member this.UpdateSupportRelationship id supportRelationship = stub supportRelationship
            member this.DeleteSupportRelationship id = stub ()
