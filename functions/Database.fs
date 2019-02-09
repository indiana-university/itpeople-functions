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

module OptionHandler =

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


module QueryHelpers = 

    type IdFilter = { Id: Id }
    type NetIdFilter = { NetId: NetId }
    type SimpleSearchQuery = { Term: string }

    let like (term:string)  = 
        term.Replace("[", "[[]").Replace("%", "[%]") 
        |> sprintf "%%%s%%"

    let sqlConnection connectionString =
        new NpgsqlConnection(connectionString)

    let dbFail operation resource (exn:System.Exception) =  
        let msg = sprintf "Database error on %s %s: %s" operation resource exn.Message
        fail (Status.InternalServerError, msg)

    /// Query all items from the database
    let queryAll<'T> connStr (query:string) = async {
        try
            use cn = sqlConnection connStr
            let! result = cn.QueryAsync<'T>(query) |> awaitTask
            return ok result
        with exn -> return dbFail "query all" (typedefof<'T>.Name) exn
    }

    /// Query all items from the database matching some parameter
    let queryAll'<'T> connStr (query:string) param = async {
        try
            use cn = sqlConnection connStr
            let! result = cn.QueryAsync<'T>(query, param) |> awaitTask
            return ok result
        with exn -> return dbFail "query all" (typedefof<'T>.Name) exn
    }

    let queryOne<'T> connStr query parameters = async {
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

    let queryExactlyOne<'T> connStr query parameters = async {
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

    let insert<'T> connStr query (record:'T) (map:int -> 'T) = async {
        try
            use db = sqlConnection connStr
            let! id = db.ExecuteAsync(query, record) |> awaitTask
            return id |> map |> ok
        with exn -> return dbFail "insert " (typedefof<'T>.Name) exn
    }

    let update<'T> connStr query (record:'T) = async {
        try
            use db = sqlConnection connStr
            let! _ = db.ExecuteAsync(query, record) |> awaitTask
            return ok record
        with exn -> return dbFail "update" (typedefof<'T>.Name) exn
    }

    let delete<'T> connStr query (id:Id) = async {
        try
            use db = sqlConnection connStr
            let! _ = db.ExecuteAsync(query, {Id=id}) |> awaitTask
            return ok ()
        with exn -> return dbFail "delete" (typedefof<'T>.Name) exn
    }

module Database =

    open QueryHelpers

    let init() = 
        SimpleCRUD.SetDialect(SimpleCRUD.Dialect.PostgreSQL)
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores <- true
        OptionHandler.RegisterTypes()


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
        return! queryAll<Unit> connStr queryUnitsSql
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

    let deleteUnitSql = """DELETE FROM units WHERE id=@Id"""
    let deleteUnit connStr id = async {
        return! delete<Unit> connStr deleteUnitSql id
    }

    let queryUnitChildrenSql = """SELECT * FROM units WHERE parent_id=@Id"""
    let queryUnitChildren connStr id = async {
        return! queryAll'<Unit> connStr queryUnitChildrenSql {Id=id}
    }

    let queryUnitMembersSql = """
        SELECT * FROM unit_members
        WHERE unit_id = @Id"""
    let queryUnitMemberPeopleSql = """
        SELECT p.* FROM unit_members m
        LEFT JOIN people p on p.id = m.person_id
        WHERE m.unit_id = @Id"""
    let queryUnitMembers connStr id = async {
        try
            use cn = sqlConnection connStr
            let! members = cn.QueryAsync<UnitMember>(queryUnitMembersSql, {Id=id}) |> awaitTask
            let! people = cn.QueryAsync<Person>(queryUnitMemberPeopleSql, {Id=id}) |> awaitTask
            let associateWithPerson m = 
                let p = people |> Seq.tryFind (fun p -> p.Id = m.PersonId)
                { m with Person=p }
            return members |> Seq.map associateWithPerson |> ok
        with exn -> return dbFail "query all" "unit members" exn
    }

    let queryUnitSupportRelationshipSql = """
        SELECT * FROM support_relationships
        WHERE unit_id = @Id"""
    let queryUnitSupportRelationshipDepartmentSql = """
        SELECT d.* FROM support_relationships s
        JOIN departments d on d.id = s.department_id
        WHERE s.unit_id = @Id"""
    let queryUnitSupportedDepartments connStr id = async {
        try
            use cn = sqlConnection connStr
            let! relations = cn.QueryAsync<SupportRelationship>(queryUnitSupportRelationshipSql, {Id=id}) |> awaitTask
            let! departments = cn.QueryAsync<Department>(queryUnitSupportRelationshipDepartmentSql, {Id=id}) |> awaitTask
            let associateWithDepartment (s:SupportRelationship) = 
                let d = departments |> Seq.find (fun d -> d.Id = s.DepartmentId)
                { s with Department=d }
            return relations |> Seq.map associateWithDepartment |> ok
        with exn -> return dbFail "query all" "unit supported departments" exn
    }

    // ***********
    // Departments
    // ***********

    let mapDepartment (department:Department) id = {department with Id=id}

    let queryDepartmentsSql = """SELECT * FROM departments"""
    let queryDepartments connStr = async {
        return! queryAll<Department> connStr queryDepartmentsSql
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

    let queryDeptSupportRelationshipSql = """
        SELECT * FROM support_relationships
        WHERE department_id = @Id"""
    let queryDeptSupportRelationshipUnitsSql = """
        SELECT u.* FROM support_relationships s
        JOIN units u on u.id = s.unit_id
        WHERE s.department_id = @Id"""
    let queryDeptSupportingUnits connStr id = async {
        try
            use cn = sqlConnection connStr
            let! relations = cn.QueryAsync<SupportRelationship>(queryDeptSupportRelationshipSql, {Id=id}) |> awaitTask
            let! units = cn.QueryAsync<Unit>(queryDeptSupportRelationshipUnitsSql, {Id=id}) |> awaitTask
            let associateWithUnit (s:SupportRelationship) = 
                let u = units |> Seq.find (fun u -> u.Id = s.UnitId)
                { s with Unit=u }
            return relations |> Seq.map associateWithUnit |> ok
        with exn -> return dbFail "query all" "unit supported departments" exn
    }

    let queryDeptMemberUnitsSql = """
        SELECT DISTINCT ON (u.id) u.* FROM units     u
        JOIN unit_members m ON m.unit_id = u.id
        JOIN people p on p.id = m.person_id
        WHERE p.department_id = @Id"""
    let queryDeptMemberUnits connStr id = async {
        return! queryAll'<Unit> connStr queryDeptMemberUnitsSql {Id=id}
    }


    // ***********
    // People
    // ***********

    let queryPeopleSql = """SELECT * FROM people"""
    let queryPeople connStr = async {
        return! queryAll<Person> connStr queryPeopleSql
    }

    let queryPersonSql = """SELECT * FROM people WHERE id=@Id LIMIT 1"""
    let queryPerson connStr id = async {
        return! queryExactlyOne<Person> connStr queryPersonSql {Id=id}
    }

    let queryPersonMembershipsSql = """
        SELECT * FROM unit_members
        WHERE person_id = @Id"""
    let queryPersonMembershipUnitsSql = """
        SELECT u.* FROM unit_members m
        JOIN units u on u.id = m.unit_id
        WHERE m.person_id = @Id"""
    let queryPersonMemberships connStr id = async {
        try
            use cn = sqlConnection connStr
            let! members = cn.QueryAsync<UnitMember>(queryPersonMembershipsSql, {Id=id}) |> awaitTask
            let! units = cn.QueryAsync<Unit>(queryPersonMembershipUnitsSql, {Id=id}) |> awaitTask
            let associateWithUnit m = 
                let u = units |> Seq.find (fun u -> u.Id = m.UnitId)
                { m with Unit=u }
            return members |> Seq.map associateWithUnit |> ok
        with exn -> return dbFail "query all" "person memberships" exn
    }

    // ***********
    // Memberships
    // ***********

    let mapUnitMember (unitMember:UnitMember) id = {unitMember with Id=id}

    let queryMembershipsSql = """SELECT * FROM unit_members"""
    let queryMemberships connStr = async {
        return! queryAll<UnitMember> connStr queryMembershipsSql
    }

    let queryMembershipSql = """SELECT * FROM unit_members WHERE id=@Id LIMIT 1"""
    let queryMembership connStr id = async {
        return! queryExactlyOne<UnitMember> connStr queryMembershipSql {Id=id}
    }

    let insertMembershipSql = """
        INSERT INTO unit_members(unit_id, person_id, title, role, permissions, percentage, tools)
        VALUES (@UnitId, @PersonId, @Title, @Role, @Permissions, @Percentage, @Tools)"""
    let insertMembership connStr unitMember = async {
        return! insert<UnitMember> connStr insertMembershipSql unitMember (mapUnitMember unitMember)
    }

    let updateMembershipSql = """
        UPDATE unit_members
        SET unit_id=@UnitId, person_id=@PersonId, title=@Title, 
            role=@Role, permissions=@Permissions, percentage=@Percentage, tools=@Tools
        WHERE id=@Id"""
    let updateMembership connStr id unitMember = async {
        return! update<UnitMember> connStr updateMembershipSql (mapUnitMember unitMember id)
    }

    let deleteMembershipSql = """DELETE FROM unit_members WHERE id=@Id"""
    let deleteMembership connStr id = async {
        return! delete<UnitMember> connStr deleteMembershipSql id
    }


    // *********************
    // Support Relationships
    // *********************

    let mapSupportRelationship (supportRelationship:SupportRelationship) id = {supportRelationship with Id=id}

    let querySupportRelationshipsSql = """SELECT * FROM support_relationships"""
    let querySupportRelationships connStr = async {
        return! queryAll<SupportRelationship> connStr querySupportRelationshipsSql
    }

    let querySupportRelationshipSql = """SELECT * FROM support_relationships WHERE id=@Id LIMIT 1"""
    let querySupportRelationship connStr id = async {
        return! queryExactlyOne<SupportRelationship> connStr querySupportRelationshipSql {Id=id}
    }

    let insertSupportRelationshipSql = """
        INSERT INTO support_relationships(unit_id, department_id)
        VALUES (@UnitId, @DepartmentId)"""
    let insertSupportRelationship connStr supportRelationship = async {
        return! insert<SupportRelationship> connStr insertSupportRelationshipSql supportRelationship (mapSupportRelationship supportRelationship)
    }

    let updateSupportRelationshipSql = """
        UPDATE support_relationships
        SET unit_id=@UnitId, department_id=@DepartmentId
        WHERE id=@Id"""
    let updateSupportRelationship connStr id supportRelationship = async {
        return! update<SupportRelationship> connStr updateSupportRelationshipSql (mapSupportRelationship supportRelationship id)
    }

    let deleteSupportRelationshipSql = """DELETE FROM support_relationships WHERE id=@Id"""
    let deleteSupportRelationship connStr id = async {
        return! delete<SupportRelationship> connStr deleteSupportRelationshipSql id
    }
    

    /// A SQL Database implementation of IDatabaseRespository
    type DatabaseRepository(connectionString:string) =
        let connStr = connectionString
        do init() 

        interface IDataRepository with 
            member this.TryGetPersonId netId = queryPersonByNetId connStr netId
            
            member this.GetPeople query = queryPeople connStr
            member this.GetPerson id = queryPerson connStr id
            member this.GetPersonMemberships personId = queryPersonMemberships connStr personId
            
            member this.GetUnits query = queryUnits connStr
            member this.GetUnit id = queryUnit connStr id
            member this.CreateUnit unit = insertUnit connStr unit
            member this.UpdateUnit id unit = updateUnit connStr id unit
            member this.DeleteUnit id = deleteUnit connStr id
            member this.GetUnitChildren id = queryUnitChildren connStr id
            member this.GetUnitMembers id = queryUnitMembers connStr id
            member this.GetUnitSupportedDepartments id = queryUnitSupportedDepartments connStr id
            
            member this.GetDepartments query = queryDepartments connStr
            member this.GetDepartment id = queryDepartment connStr id
            member this.GetDepartmentMemberUnits id = queryDeptMemberUnits connStr id
            member this.GetDepartmentSupportingUnits id = queryDeptSupportingUnits connStr id

            member this.GetMemberships () = queryMemberships connStr 
            member this.GetMembership id = queryMembership connStr id
            member this.CreateMembership membership = insertMembership connStr membership
            member this.UpdateMembership id membership = updateMembership connStr id membership
            member this.DeleteMembership id = deleteMembership connStr id
            
            member this.GetSupportRelationships () = querySupportRelationships connStr 
            member this.GetSupportRelationship id = querySupportRelationship connStr id
            member this.CreateSupportRelationship supportRelationship = insertSupportRelationship connStr supportRelationship
            member this.UpdateSupportRelationship id supportRelationship = updateSupportRelationship connStr id supportRelationship
            member this.DeleteSupportRelationship id = deleteMembership connStr id
