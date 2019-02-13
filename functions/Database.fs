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

    type Cn = NpgsqlConnection
    type Query = string
    type WhereClause = string
    type MultimapMany<'T> = Cn -> Task<seq<'T>>
    type MultimapOne<'T> = int -> Cn -> Task<seq<'T>>

    type QueryAllParams<'T> = 
        | AllFromTable
        | RawQuery of Query
        | ParameterizedQuery of Query * obj
        | RawFilter of WhereClause
        | ParameterizedFilter of WhereClause * obj
        | QueryMultimapped of MultimapMany<'T>

    type QueryOneParams<'T> =
        | SimpleObject
        | ComplexObject of MultimapOne<'T>

    let like (term:string) = sprintf "%%%s%%" term

    let sqlConnection connectionString =
        new NpgsqlConnection(connectionString)

    let dbOp connStr name resource op = async {
        try
            use cn = sqlConnection connStr
            return! op cn
        with 
        | exn -> 
            let msg = sprintf "Database error on %s %s: %s" name resource exn.Message
            return fail (Status.InternalServerError, msg)
    } 

    let fetchAll<'T> connStr queryParams = async {
        return! dbOp connStr "fetch one" (typedefof<'T>.Name) (fun cn -> async {
            let fetch = 
                match queryParams with
                | AllFromTable -> cn.GetListAsync<'T>()
                | RawQuery sql -> cn.QueryAsync<'T>(sql)  
                | ParameterizedQuery (sql, param) -> cn.QueryAsync<'T>(sql, param)  
                | RawFilter filter -> cn.GetListAsync<'T>(conditions=filter) 
                | ParameterizedFilter (filter, param) -> cn.GetListAsync<'T>(conditions=filter, parameters=param) 
                | QueryMultimapped (mapper) -> mapper cn 
            let! result = fetch |> awaitTask
            return ok result
        })
    }

    let fetchOneSimple<'T when 'T:equality> (cn:Cn) id = async {
        let! result = cn.GetAsync<'T>(id) |> awaitTask
        return if result = Unchecked.defaultof<'T> then None else Some(result)
    }
    let fetchOneComplex (cn:Cn) id mapper  = async { 
        let! result = mapper id cn |> awaitTask
        return Seq.tryHead result
    }

    let fetchOne<'T when 'T:equality> connStr queryParams id  = async {
        return! dbOp connStr "fetch one" (typedefof<'T>.Name) (fun cn -> async {
            let! result = 
                match queryParams with
                | SimpleObject -> fetchOneSimple<'T> cn id
                | ComplexObject(mapper) -> fetchOneComplex cn id mapper
            return
                match result with
                | Some(r) -> ok r
                | None -> fail(Status.NotFound, sprintf "No %s was found with that ID %d." (typedefof<'T>.Name) id)
        })
    }

    let insertImpl<'T> connStr id (obj:'T) = async {
        return! dbOp connStr "insert" (typedefof<'T>.Name) (fun cn -> async {
            let! result = cn.InsertAsync<'T>(obj) |> awaitTask
            return ok (result.GetValueOrDefault())
        })
    }

    let insert<'T when 'T:equality> connStr (obj:'T) writeParams = async {
        return 
            await (insertImpl connStr id) obj
            >>= await (fetchOne<'T> connStr writeParams)
    }

    let updateImpl<'T> connStr id (obj:'T) = async {
        return! dbOp connStr "insert" (typedefof<'T>.Name) (fun cn -> async {
            let! _ = cn.UpdateAsync<'T>(obj) |> awaitTask
            return ok id            
        })
    }

    let update<'T when 'T:equality> connStr id (obj:'T) writeParams = async {
        return 
            await (updateImpl connStr id) obj
            >>= await (fetchOne<'T> connStr writeParams)
    }

    let delete<'T> connStr (id:int) = async {
        return! dbOp connStr "delete" (typedefof<'T>.Name) (fun cn -> async {
            let! _ = cn.DeleteAsync<'T>(id) |> awaitTask
            return () |> ok
        })
    }

    let execute connStr sql parameters = async {
        return! dbOp connStr "execute" "" (fun cn -> async {
            let! _ = cn.ExecuteAsync(sql, parameters) |> awaitTask
            return () |> ok
        })
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
        let! people = fetchAll<Person> connStr (ParameterizedFilter("WHERE netid = @NetId LIMIT 1", {NetId=netId}))
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
        return! fetchAll connStr (QueryMultimapped(multimapMemberships))
    }

    let queryMembership connStr id = async {
        return! fetchOne connStr (ComplexObject(multimapMembership)) id 
    }

    let insertMembership connStr (unitMember:UnitMember) = async {
        return! insert connStr unitMember (ComplexObject(multimapMembership))
    }

    let updateMembership connStr (unitMember:UnitMember) = async {
        return! update connStr unitMember.Id unitMember (ComplexObject(multimapMembership))
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
        return! fetchAll connStr (QueryMultimapped(multimapRelations))
    }

    let querySupportRelationship connStr id = async {
        return! fetchOne connStr (ComplexObject(multimapRelation)) id
    }

    let insertSupportRelationship connStr supportRelationship = async {
        return! insert<SupportRelationship> connStr supportRelationship (ComplexObject(multimapRelation))
    }

    let updateSupportRelationship connStr (supportRelationship:SupportRelationship) = async {
        return! update<SupportRelationship> connStr supportRelationship.Id supportRelationship (ComplexObject(multimapRelation))
    }

    let deleteSupportRelationship connStr (supportRelationship:SupportRelationship) = async {
        return! delete<SupportRelationship> connStr supportRelationship.Id
    }
   

    // **********
    // Units
    // **********

    let queryUnits connStr query = async {
        let filter = 
            match query with 
            | None -> RawFilter("WHERE parent_id IS NULL")
            | Some(q) -> ParameterizedFilter ("WHERE name ILIKE @Query OR description ILIKE @Query", {Query=like q})
        return! fetchAll<Unit> connStr filter
    }

    let queryUnit connStr id = async {
        return! fetchOne<Unit> connStr SimpleObject id
    }

    let insertUnit connStr unit = async {
        return! insert<Unit> connStr unit SimpleObject
    }

    let updateUnit connStr (unit:Unit) = async {
        return! update<Unit> connStr unit.Id unit SimpleObject
    }

    let deleteUnitSql = """
        DELETE FROM unit_members WHERE unit_id=@Id;
        DELETE FROM support_relationships WHERE unit_id=@Id;
        DELETE FROM units WHERE id=@Id"""
    let deleteUnit connStr (unit:Unit) = async {
        return! execute connStr deleteUnitSql {Id=unit.Id}    
    }

    let queryUnitChildren connStr (unit:Unit) = async {
        return! fetchAll<Unit> connStr (ParameterizedFilter("WHERE parent_id=@Id", {Id=unit.Id}))
    }

    let queryUnitMembers connStr (unit:Unit) = async {
        return! fetchAll connStr (QueryMultimapped(multimapMemberships' "WHERE u.id=@Id" {Id=unit.Id}))
    }

    let queryUnitSupportedDepartments connStr (unit:Unit) = async {
        return! fetchAll connStr (QueryMultimapped(multimapRelations' "WHERE u.id=@Id" {Id=unit.Id}))
    }


    // ***********
    // Departments
    // ***********

    let queryDepartments connStr query = async {
        return! 
            match query with 
            | None -> fetchAll<Department> connStr AllFromTable
            | Some(q) -> fetchAll<Department> connStr (ParameterizedFilter("WHERE name ILIKE @Query OR description ILIKE @Query", {Query=like q}))
    }

    let queryDepartment connStr id = async {
        return! fetchOne<Department> connStr SimpleObject id
    }

    let insertDepartment connStr department = async {
        return! insert<Department> connStr department SimpleObject
    }

    let updateDepartment connStr (department:Department) = async {
        return! update<Department> connStr department.Id department SimpleObject
    }

    let queryDeptSupportingUnits connStr id = async {
        return! fetchAll connStr (QueryMultimapped(multimapRelations' "WHERE d.id = @Id" {Id=id}))
    }

    let queryDeptMemberUnitsSql = """
        SELECT DISTINCT ON (u.id) u.* FROM units u
        JOIN unit_members m ON m.unit_id = u.id
        JOIN people p on p.id = m.person_id
        WHERE p.department_id = @Id"""
    let queryDeptMemberUnits connStr id = async {
        return! fetchAll<Unit> connStr (ParameterizedQuery(queryDeptMemberUnitsSql, {Id=id}))
    }


    // ***********
    // People
    // ***********

    let mapPerson (p:Person) d = {p with Department=d}
    let multimapPeople = fun (cn:Cn) -> cn.QueryAsync<Person, Department, Person>(queryPersonSql, mapPerson)
    let multimapPeople' (filter:string) param = fun (cn:Cn) -> cn.QueryAsync<Person, Department, Person>(queryPersonSql+" "+filter, mapPerson, param)
    let multimapPerson (id:int) = multimapPeople' "WHERE p.id=@Id" {Id=id}

    let queryPeople connStr query = async {
        let filter = 
            match query with
            | None ->  multimapPeople
            | Some(q) -> multimapPeople' "WHERE p.name ILIKE @Query OR p.netid ILIKE @Query" {Query=like q}
        return! fetchAll connStr (QueryMultimapped(filter))
    }

    let queryPerson connStr id = async {
        return! fetchOne<Person> connStr (ComplexObject(multimapPerson)) id
    }
    
    let queryPersonMemberships connStr id = async {
        return! fetchAll connStr (QueryMultimapped(multimapMemberships' "WHERE p.id=@Id" {Id=id}))
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
