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
    type SearchFilter = { Query: string }

    let like (term:string) = sprintf "%%%s%%" term

    let isDefault<'T when 'T:equality> result = 
        result = Unchecked.defaultof<'T>

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
    let queryAll'<'T> connStr (query:string) (param:obj) = async {
        try
            use cn = sqlConnection connStr
            let! result = cn.QueryAsync<'T>(query, param) |> awaitTask
            return ok result
        with exn -> return dbFail "query all" (typedefof<'T>.Name) exn
    }

    let queryExactlyOne<'T when 'T:equality> connStr id = async {
        try
            use cn = sqlConnection connStr
            let! result = cn.GetAsync<'T>(id) |> awaitTask
            if (result |> isDefault<'T>)
            then return fail (Status.NotFound, (sprintf "No %s found with ID %d." typedefof<'T>.Name id))
            else return ok result
        with exn -> return dbFail "query one" (typedefof<'T>.Name) exn
    }

    let insert<'T> connStr (record:'T) (map:int -> 'T) = async {
        try
            use db = sqlConnection connStr
            let! inserted = db.InsertAsync<'T>(record) |> awaitTask
            return inserted.GetValueOrDefault() |> map |> ok
        with exn -> return dbFail "insert " (typedefof<'T>.Name) exn
    }

    let update<'T> connStr (record:'T) = async {
        try
            use db = sqlConnection connStr
            let! _ = db.UpdateAsync<'T>(record) |> awaitTask
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

    let queryUnitsSql = """
        SELECT * FROM units 
        WHERE parent_id IS NULL"""
    let queryUnitsSearchSql = """
        SELECT * FROM units 
        WHERE name ILIKE @Query OR description ILIKE @Query"""
    let queryUnits connStr query = async {
        return! match query with 
                | None -> queryAll<Unit> connStr queryUnitsSql
                | Some(q) -> queryAll'<Unit> connStr queryUnitsSearchSql {Query=like q}
    }

    let queryUnit connStr id = async {
        return! queryExactlyOne<Unit> connStr id
    }

    let insertUnit connStr unit = async {
        return! insert<Unit> connStr unit (mapUnit unit)
    }

    let updateUnit connStr id unit = async {
        return! update<Unit> connStr (mapUnit unit id)
    }

    let deleteUnitSql = """
        DELETE FROM unit_members WHERE unit_id=@Id;
        DELETE FROM support_relationships WHERE unit_id=@Id;
        DELETE FROM units WHERE id=@Id"""
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
            let! unit = cn.GetAsync<Unit>(id) |> awaitTask
            let! members = cn.QueryAsync<UnitMember>(queryUnitMembersSql, {Id=id}) |> awaitTask
            let! people = cn.QueryAsync<Person>(queryUnitMemberPeopleSql, {Id=id}) |> awaitTask
            let associateWithPerson m = 
                let p = people |> Seq.tryFind (fun p -> p.Id = m.PersonId)
                { m with Person=p; Unit=unit }
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
            let! unit = cn.GetAsync<Unit>(id) |> awaitTask
            let! relations = cn.QueryAsync<SupportRelationship>(queryUnitSupportRelationshipSql, {Id=id}) |> awaitTask
            let! departments = cn.QueryAsync<Department>(queryUnitSupportRelationshipDepartmentSql, {Id=id}) |> awaitTask
            let associateWithDepartment (s:SupportRelationship) = 
                let d = departments |> Seq.find (fun d -> d.Id = s.DepartmentId)
                { s with Department=d; Unit=unit }
            return relations |> Seq.map associateWithDepartment |> ok
        with exn -> return dbFail "query all" "unit supported departments" exn
    }

    // ***********
    // Departments
    // ***********

    let mapDepartment (department:Department) id = {department with Id=id}

    let queryDepartmentsSql = """SELECT * FROM departments"""
    let queryDepartmentsSearchSql = queryDepartmentsSql + """ WHERE name ILIKE @Query OR description ILIKE @Query"""
    let queryDepartments connStr query = async {
        return! match query with 
                | None -> queryAll<Department> connStr queryDepartmentsSql
                | Some(q) -> queryAll'<Department> connStr queryDepartmentsSearchSql {Query=like q}
    }

    let queryDepartment connStr id = async {
        return! queryExactlyOne<Department> connStr id
    }

    let insertDepartment connStr department = async {
        return! insert<Department> connStr department (mapDepartment department)
    }

    let updateDepartment connStr id department = async {
        return! update<Department> connStr (mapDepartment department id)
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
    let queryPeopleSearchSql = queryPeopleSql + """ WHERE name ILIKE @Query OR description ILIKE @Query"""
    let queryPeople connStr query = async {
        return! match query with
                | None -> queryAll<Person> connStr queryPeopleSql
                | Some(q) -> queryAll'<Person> connStr queryPeopleSearchSql {Query=like q}
    }

    let queryPerson connStr id = async {
        return! queryExactlyOne<Person> connStr id
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

    let queryMembership connStr id = async {
        return! queryExactlyOne<UnitMember> connStr id
    }

    let insertMembership connStr unitMember = async {
        return! insert<UnitMember> connStr unitMember (mapUnitMember unitMember)
    }

    let updateMembership connStr id unitMember = async {
        return! update<UnitMember> connStr (mapUnitMember unitMember id)
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

    let querySupportRelationship connStr id = async {
        return! queryExactlyOne<SupportRelationship> connStr id
    }

    let insertSupportRelationship connStr supportRelationship = async {
        return! insert<SupportRelationship> connStr supportRelationship (mapSupportRelationship supportRelationship)
    }

    let updateSupportRelationship connStr id supportRelationship = async {
        return! update<SupportRelationship> connStr (mapSupportRelationship supportRelationship id)
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
            
            member this.GetPeople query = queryPeople connStr query
            member this.GetPerson id = queryPerson connStr id
            member this.GetPersonMemberships personId = queryPersonMemberships connStr personId
            
            member this.GetUnits query = queryUnits connStr query
            member this.GetUnit id = queryUnit connStr id
            member this.CreateUnit unit = insertUnit connStr unit
            member this.UpdateUnit id unit = updateUnit connStr id unit
            member this.DeleteUnit id = deleteUnit connStr id
            member this.GetUnitChildren id = queryUnitChildren connStr id
            member this.GetUnitMembers id = queryUnitMembers connStr id
            member this.GetUnitSupportedDepartments id = queryUnitSupportedDepartments connStr id
            
            member this.GetDepartments query = queryDepartments connStr query
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
