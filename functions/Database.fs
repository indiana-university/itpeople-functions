// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open Types
open Util
open Chessie.ErrorHandling
open Dapper
open Npgsql

module OptionHandler =

    type OptionHandler<'T> () =
        inherit SqlMapper.TypeHandler<'T option> ()

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

    open System.Threading.Tasks
    
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

    type Cn = NpgsqlConnection

    let fetchAll<'T> connStr = async {
        try
            use cn = sqlConnection connStr
            let! result = cn.GetListAsync<'T>() |> awaitTask
            return ok result
        with 
        | exn -> return dbFail "fetch all" (typedefof<'T>.Name) exn   
    }

    let fetchAll'<'T> connStr sql parameters = async {
        try
            use cn = sqlConnection connStr
            let! result = cn.QueryAsync<'T>(sql, parameters) |> awaitTask
            return ok result
        with 
        | exn -> return dbFail "fetch all" (typedefof<'T>.Name) exn   
    }

    let fetchAll''<'T> connStr filter (parameters:obj option) = async {
        try
            use cn = sqlConnection connStr
            let! result = 
                match parameters with
                | Some(p) -> cn.GetListAsync<'T>(conditions=filter, parameters=p) |> awaitTask
                | None -> cn.GetListAsync<'T>(conditions=filter) |> awaitTask
            return ok result
        with 
        | exn -> return dbFail "fetch all" (typedefof<'T>.Name) exn   
    }

    let fetchAllMultimap<'T> connStr (getAll: Cn -> Task<seq<'T>>) = async {
        try
            use cn = sqlConnection connStr
            let! result = cn |> getAll |> awaitTask
            return ok result
        with 
        | exn -> return dbFail "fetch all" (typedefof<'T>.Name) exn   
    }

    let fetchOne<'T when 'T:equality> connStr id = async {
        try
            use cn = sqlConnection connStr
            let! result = cn.GetAsync<'T>(id) |> awaitTask
            if isDefault<'T> result
            then return fail(Status.NotFound, sprintf "No %s was found with that ID %d." (typedefof<'T>.Name) id)
            else return result |> ok
        with 
        | exn -> return dbFail "fetch one" (typedefof<'T>.Name) exn   
    }

    let fetchOneMultimap<'T when 'T:equality> connStr id (getById: int -> Cn -> Task<seq<'T>>) = async {
        try
            use cn = sqlConnection connStr
            let! result = cn |> getById id|> awaitTask
            if Seq.isEmpty result
            then return fail(Status.NotFound, sprintf "No %s was found with that ID %d." (typedefof<'T>.Name) id)
            else return result |> Seq.head |> ok
        with 
        | exn -> return dbFail "fetch one" (typedefof<'T>.Name) exn   
    }

    let insert<'T> connStr (obj:'T) = async {
        try
            use cn = sqlConnection connStr
            let! id = cn.InsertAsync<'T>(obj) |> awaitTask
            let! inserted = cn.GetAsync<'T>(id) |> awaitTask
            return inserted |> ok
        with 
        | exn -> return dbFail "insert" (typedefof<'T>.Name) exn   
    }

    let insertMultimap<'T> connStr (obj:'T) (getById: int -> Cn -> Task<seq<'T>>) = async {
        try
            use cn = sqlConnection connStr
            let! id = cn.InsertAsync<'T>(obj) |> awaitTask
            let! inserted = cn |> getById (id.GetValueOrDefault()) |> awaitTask
            return inserted |> Seq.head |> ok
        with 
        | exn -> return dbFail "insert" (typedefof<'T>.Name) exn   
    }

    let update<'T> connStr (obj:'T) = async {
        try
            use cn = sqlConnection connStr
            let! _ = cn.UpdateAsync<'T>(obj) |> awaitTask
            return obj |> ok
        with 
        | exn -> return dbFail "update" (typedefof<'T>.Name) exn   
    }

    let updateMultimap<'T> connStr id (obj:'T) (getById: int -> Cn -> Task<seq<'T>>) = async {
        try
            use cn = sqlConnection connStr
            let! _ = cn.UpdateAsync<'T>(obj) |> awaitTask
            let! updated = cn |> getById id |> awaitTask
            return updated |> Seq.head |> ok
        with 
        | exn -> return dbFail "update" (typedefof<'T>.Name) exn   
    }

    let delete<'T> connStr (id:int) = async {
        try
            use cn = sqlConnection connStr
            let! _ = cn.DeleteAsync<'T>(id) |> awaitTask
            return () |> ok
        with 
        | exn -> return dbFail "delete" (typedefof<'T>.Name) exn   
    }

    let execute connStr sql parameters = async {
        try
            use cn = sqlConnection connStr
            let! _ = cn.ExecuteAsync(sql, parameters) |> awaitTask
            return () |> ok
        with 
        | exn -> return dbFail "execute" "anonymous" exn   
    }

module Database =

    open QueryHelpers
    
    let init() = 
        SimpleCRUD.SetDialect(SimpleCRUD.Dialect.PostgreSQL)
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores <- true
        OptionHandler.RegisterTypes()

    let queryUnitMemberSql = """
        SELECT m.*, u.*, p.*
        FROM unit_members m
        JOIN units u on u.id = m.unit_id
        LEFT JOIN people p on p.id = m.person_id """

    let querySupportRelationshipSql = """
        SELECT s.*, d.*, u.*
        FROM support_relationships s
        JOIN departments d on d.id = s.department_id
        JOIN units u on u.id = s.unit_id """

    let queryPersonSql = """
        SELECT p.*, d.*
        FROM people p
        JOIN departments d on d.id = p.department_id """

    // **************
    // Authentication
    // **************

    let queryPersonByNetId connStr netId = async {
        let! people = fetchAll''<Person> connStr "WHERE netid = @NetId LIMIT 1" (Some({NetId=netId}:>obj))
        match people with
        | Ok(result,_) ->
            match result |> Seq.tryHead with
            | Some(p) -> return ok (netId, Some(p.Id))
            | None -> return ok (netId, None)
        | Bad(msgs) -> return Bad(msgs)
    }

    // ***********
    // Memberships
    // ***********

    let mapMember m u p = 
        let person = if (box p = null) then None else Some(p)
        {m with Unit=u; Person=person}
    let multimapMemberships = fun (cn:Cn) -> cn.QueryAsync<UnitMember, Unit, Person, UnitMember>(queryUnitMemberSql, mapMember)
    let multimapMemberships' (filter:string) param = fun (cn:Cn) -> cn.QueryAsync<UnitMember, Unit, Person, UnitMember>(queryUnitMemberSql+" "+filter, mapMember, param)
    let multimapMembership (id:int) = multimapMemberships' "WHERE m.id=@Id" {Id=id}

    let queryMemberships connStr = async {
        return! fetchAllMultimap connStr multimapMemberships
    }

    let queryMembership connStr id = async {
        return! fetchOneMultimap connStr id multimapMembership
    }

    let insertMembership connStr unitMember = async {
        return! insertMultimap connStr unitMember multimapMembership
    }

    let updateMembership connStr (unitMember:UnitMember) = async {
        return! updateMultimap<UnitMember> connStr unitMember.Id unitMember multimapMembership
    }

    let deleteMembership connStr (unitMember:UnitMember) = async {
        return! delete<UnitMember> connStr unitMember.Id
    }    

    
    // *********************
    // Support Relationships
    // *********************

    let mapRelation s d u = {s with Unit=u; Department=d}
    let multimapRelations = fun (cn:Cn) -> cn.QueryAsync<SupportRelationship, Department, Unit, SupportRelationship>(querySupportRelationshipSql, mapRelation)
    let multimapRelations' (filter:string) param = fun (cn:Cn) -> cn.QueryAsync<SupportRelationship, Department, Unit, SupportRelationship>(querySupportRelationshipSql+" "+filter, mapRelation, param)
    let multimapRelation (id:int) = multimapRelations' "WHERE s.id=@Id" {Id=id}

    let querySupportRelationships connStr = async {
        return! fetchAllMultimap connStr multimapRelations
    }

    let querySupportRelationship connStr id = async {
        return! fetchOneMultimap connStr id multimapRelation
    }

    let insertSupportRelationship connStr supportRelationship = async {
        return! insertMultimap connStr supportRelationship multimapRelation
    }

    let updateSupportRelationship connStr (supportRelationship:SupportRelationship) = async {
        return! updateMultimap<SupportRelationship> connStr supportRelationship.Id supportRelationship multimapRelation
    }

    let deleteSupportRelationship connStr (supportRelationship:SupportRelationship) = async {
        return! delete<SupportRelationship> connStr supportRelationship.Id
    }
   

    // **********
    // Units
    // **********

    let queryUnits connStr query = async {
        return! 
            match query with 
            | None -> fetchAll''<Unit> connStr "WHERE parent_id IS NULL" None
            | Some(q) -> fetchAll''<Unit> connStr "WHERE name ILIKE @Query OR description ILIKE @Query" (Some({Query=like q}:>obj))
    }

    let queryUnit connStr id = async {
        return! fetchOne<Unit> connStr id
    }

    let insertUnit connStr unit = async {
        return! insert<Unit> connStr unit
    }

    let updateUnit connStr (unit:Unit) = async {
        return! update<Unit> connStr unit
    }

    let deleteUnitSql = """
        DELETE FROM unit_members WHERE unit_id=@Id;
        DELETE FROM support_relationships WHERE unit_id=@Id;
        DELETE FROM units WHERE id=@Id"""
    let deleteUnit connStr (unit:Unit) = async {
        return! execute connStr deleteUnitSql {Id=unit.Id}    
    }

    let queryUnitChildren connStr (unit:Unit) = async {
        return! fetchAll''<Unit> connStr "WHERE parent_id=@Id" (Some({Id=unit.Id}:>obj))
    }

    let queryUnitMembers connStr (unit:Unit) = async {
        return! fetchAllMultimap connStr (multimapMemberships' "WHERE u.id=@Id" {Id=unit.Id})
    }

    let queryUnitSupportedDepartments connStr (unit:Unit) = async {
        return! fetchAllMultimap connStr (multimapRelations' "WHERE u.id=@Id" {Id=unit.Id})
    }


    // ***********
    // Departments
    // ***********

    let queryDepartments connStr query = async {
        return! 
            match query with 
            | None -> fetchAll<Department> connStr
            | Some(q) -> fetchAll''<Department> connStr "WHERE name ILIKE @Query OR description ILIKE @Query" (Some({Query=like q}:>obj))
    }

    let queryDepartment connStr id = async {
        return! fetchOne<Department> connStr id
    }

    let insertDepartment connStr department = async {
        return! insert<Department> connStr department
    }

    let updateDepartment connStr id department = async {
        return! update<Department> connStr {department with Id=id}
    }

    let queryDeptSupportingUnits connStr id = async {
        return! fetchAllMultimap connStr (multimapRelations' "WHERE d.id = @Id" {Id=id})
    }

    let queryDeptMemberUnitsSql = """
        SELECT DISTINCT ON (u.id) u.* FROM units u
        JOIN unit_members m ON m.unit_id = u.id
        JOIN people p on p.id = m.person_id
        WHERE p.department_id = @Id"""
    let queryDeptMemberUnits connStr id = async {
        return! fetchAll'<Unit> connStr queryDeptMemberUnitsSql {Id=id}
    }


    // ***********
    // People
    // ***********

    let mapPerson (p:Person) d = {p with Department=d}
    let multimapPeople = fun (cn:Cn) -> cn.QueryAsync<Person, Department, Person>(queryPersonSql, mapPerson)
    let multimapPeople' (filter:string) param = fun (cn:Cn) -> cn.QueryAsync<Person, Department, Person>(queryPersonSql+" "+filter, mapPerson, param)
    let multimapPerson (id:int) = multimapPeople' "WHERE p.id=@Id" {Id=id}

    let queryPeople connStr query = async {
        return! 
            match query with
            | None -> fetchAllMultimap connStr multimapPeople
            | Some(q) -> fetchAllMultimap connStr (multimapPeople' "WHERE p.name ILIKE @Query OR p.netid ILIKE @Query" {Query=like q})
    }

    let queryPerson connStr id = async {
        return! fetchOneMultimap connStr id multimapPerson
    }
    
    let queryPersonMemberships connStr id = async {
        return! fetchAllMultimap connStr (multimapMemberships' "WHERE p.id=@Id" {Id=id})
    }    

    let People(connStr) = {
        TryGetId = queryPersonByNetId connStr
        GetAll = queryPeople connStr
        Get = queryPerson connStr
        GetMemberships = queryPersonMemberships connStr
    }

    let Units(connStr) = {
        GetAll = queryUnits connStr
        Get = queryUnit connStr 
        Create = insertUnit connStr
        Update = updateUnit connStr
        Delete = deleteUnit connStr
        GetChildren = queryUnitChildren connStr
        GetMembers = queryUnitMembers connStr
        GetSupportedDepartments = queryUnitSupportedDepartments connStr
    }

    let Departments (connStr) = {
        GetAll = queryDepartments connStr
        Get = queryDepartment connStr
        GetMemberUnits = queryDeptMemberUnits connStr
        GetSupportingUnits = queryDeptSupportingUnits connStr
    }

    let Memberships (connStr) : MembershipRepository = {
        GetAll = fun () -> queryMemberships connStr
        Get = queryMembership connStr
        Create = insertMembership connStr
        Update = updateMembership connStr
        Delete = deleteMembership connStr
    }

    let SupportRelationshipsRepository(connStr) = {
        GetAll = fun () -> querySupportRelationships connStr 
        Get = querySupportRelationship connStr
        Create = insertSupportRelationship connStr
        Update = updateSupportRelationship connStr
        Delete = deleteSupportRelationship connStr
    }

    let DatabaseRepository(connStr) = {
        People = People(connStr)
        Departments = Departments(connStr)
        Units = Units(connStr)
        Memberships = Memberships(connStr)
        SupportRelationships = SupportRelationshipsRepository(connStr)
    }
