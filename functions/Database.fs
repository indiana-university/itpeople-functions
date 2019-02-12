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
            then return fail(Status.NotFound, "No resource found with with that ID.")
            else return result |> ok
        with 
        | exn -> return dbFail "fetch one" (typedefof<'T>.Name) exn   
    }

    let fetchOne'<'T when 'T:equality> connStr sql parameters = async {
        try
            use cn = sqlConnection connStr
            let! result = cn.QueryFirstOrDefaultAsync<'T>(sql, parameters) |> awaitTask
            if isDefault<'T> result
            then return fail(Status.NotFound, "No resource found with with that ID.")
            else return result |> ok
        with 
        | exn -> return dbFail "fetch one" (typedefof<'T>.Name) exn   
    }

    let fetchOneMultimap<'T when 'T:equality> connStr id (getById: int -> Cn -> Task<seq<'T>>) = async {
        try
            use cn = sqlConnection connStr
            let! result = cn |> getById id|> awaitTask
            if Seq.isEmpty result
            then return fail(Status.NotFound, "No resource found with with that ID.")
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

    let update<'T> connStr id (obj:'T) = async {
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
        return! fetchOne' connStr "SELECT id FROM people WHERE netid = @NetId" {NetId=netId}
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

    let updateMembership connStr id unitMember = async {
        return! updateMultimap<UnitMember> connStr id {unitMember with Id=id} multimapMembership
    }

    let deleteMembership connStr id = async {
        return! delete<UnitMember> connStr id
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

    let updateSupportRelationship connStr id supportRelationship = async {
        return! updateMultimap<SupportRelationship> connStr id {supportRelationship with Id=id} multimapRelation
    }

    let deleteSupportRelationship connStr id = async {
        return! delete<SupportRelationship> connStr id
    }
   

    // **********
    // Units
    // **********

    let mapUnit (unit:Unit) id = {unit with Id=id}

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

    let updateUnit connStr id unit = async {
        return! update<Unit> connStr id {unit with Id=id}
    }

    let deleteUnitSql = """
        DELETE FROM unit_members WHERE unit_id=@Id;
        DELETE FROM support_relationships WHERE unit_id=@Id;
        DELETE FROM units WHERE id=@Id"""
    let deleteUnit connStr id = async {
        return! execute connStr deleteUnitSql {Id=id}    
    }

    let queryUnitChildren connStr id = async {
        return! fetchAll''<Unit> connStr "WHERE parent_id=@Id" (Some({Id=id}:>obj))
    }

    let queryUnitMembers connStr id = async {
        return! fetchAllMultimap connStr (multimapMemberships' "WHERE u.id=@Id" {Id=id})
    }

    let queryUnitSupportedDepartments connStr id = async {
        return! fetchAllMultimap connStr (multimapRelations' "WHERE u.id=@Id" {Id=id})
    }


    // ***********
    // Departments
    // ***********

    let mapDepartment (department:Department) id = {department with Id=id}

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
        return! update<Department> connStr id {department with Id=id}
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
            member this.DeleteSupportRelationship id = deleteSupportRelationship connStr id
