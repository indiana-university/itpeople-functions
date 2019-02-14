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
    type Sql = string
    type WhereClause = string

    type MapMany<'T> = Cn -> Task<seq<'T>>
    type MapOne<'T> = int -> MapMany<'T>

    type Filter =
        | Unfiltered
        | Where of WhereClause
        | WhereId of WhereClause * Id
        | WhereParam of WhereClause * obj

    type QueryOneParams<'T> =
        | MapOne of (int -> Cn -> Task<seq<'T>>)

    let like = sprintf "%%%s%%"
    let where = sprintf "%s WHERE %s"

    let parseQueryAndParam sql filter = 
        match filter with
        | Unfiltered -> (sql, ():>obj)
        | Where clause -> ((where sql clause), ():>obj)
        | WhereId (clause,id)-> ((where sql clause+"=@Id"), {Id=id}:>obj)
        | WhereParam (clause,param)-> ((where sql clause), param)

    let dbOp connStr name resource op = async {
        try
            use cn = new NpgsqlConnection(connStr)
            return! op cn
        with 
        | exn -> 
            let msg = sprintf "Database error on %s %s: %s" name resource exn.Message
            return fail (Status.InternalServerError, msg)
    } 

    let fetchAll<'T> connStr (mapper:MapMany<'T>) = async {
        return! dbOp connStr "fetch all" (typedefof<'T>.Name) (fun cn -> async {
            let! result = cn |> mapper |> awaitTask
            return ok result
        })
    }

    let fetchOne<'T> connStr (mapper:MapOne<'T>) id  = async {
        return! dbOp connStr "fetch one" (typedefof<'T>.Name) (fun cn -> async {
            let! result = mapper id cn |> awaitTask
            if Seq.isEmpty result 
            then return fail(Status.NotFound, sprintf "No %s was found with ID %d." (typedefof<'T>.Name) id)
            else return result |> Seq.head |> ok
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
            obj
            |> await (insertImpl connStr id)
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
            obj
            |> await (updateImpl connStr id)
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

    // **************
    // Authentication
    // **************


    // ***********
    // Memberships
    // ***********
    let queryUnitMemberSql = """
        SELECT m.*, u.*, p.*
        FROM unit_members m
        JOIN units u on u.id = m.unit_id
        LEFT JOIN people p on p.id = m.person_id """

    let mapUnitMembers filter (cn:Cn) = 
        let (query, param) = parseQueryAndParam queryUnitMemberSql filter
        let mapper m u p = 
            let person = if isNull (box p) then None else Some(p)
            {m with Unit=u; Person=person}
        cn.QueryAsync<UnitMember, Unit, Person, UnitMember>(query, mapper, param)

    let mapUnitMember id = mapUnitMembers (WhereId("m.id", id))

    let queryMemberships connStr = async {
        return! fetchAll connStr (mapUnitMembers Unfiltered)
    }

    let queryMembership connStr id = async {
        return! fetchOne connStr mapUnitMember id 
    }

    let insertMembership connStr (unitMember:UnitMember) = async {
        return! insert connStr unitMember mapUnitMember
    }

    let updateMembership connStr (unitMember:UnitMember) = async {
        return! update connStr unitMember.Id unitMember mapUnitMember
    }

    let deleteMembership connStr (unitMember:UnitMember) = async {
        return! delete<UnitMember> connStr unitMember.Id
    }    


    // *********************
    // Support Relationships
    // *********************

    let querySupportRelationshipSql = """
        SELECT s.*, d.*, u.*
        FROM support_relationships s
        JOIN departments d on d.id = s.department_id
        JOIN units u on u.id = s.unit_id """

    let mapSupportRelationships filter (cn:Cn) = 
        let (query, param) = parseQueryAndParam querySupportRelationshipSql filter
        let mapper s d u = {s with Unit=u; Department=d}
        cn.QueryAsync<SupportRelationship, Department, Unit, SupportRelationship>(query, mapper, param)

    let mapSupportRelationship id = mapSupportRelationships (WhereId("s.id", id))

    let querySupportRelationships connStr = async {
        return! fetchAll<SupportRelationship> connStr (mapSupportRelationships Unfiltered)
    }

    let querySupportRelationship connStr id = async {
        return! fetchOne connStr mapSupportRelationship id
    }

    let insertSupportRelationship connStr supportRelationship = async {
        return! insert<SupportRelationship> connStr supportRelationship mapSupportRelationship
    }

    let updateSupportRelationship connStr (supportRelationship:SupportRelationship) = async {
        return! update<SupportRelationship> connStr supportRelationship.Id supportRelationship mapSupportRelationship
    }

    let deleteSupportRelationship connStr (supportRelationship:SupportRelationship) = async {
        return! delete<SupportRelationship> connStr supportRelationship.Id
    }
   

    // **********
    // Units
    // **********

    let queryUnitsSql = """
        SELECT u.*, p.*
        FROM units u
        LEFT JOIN units p ON p.id = u.parent_id"""

    let mapUnits' sql filter (cn:Cn) = 
        let (query, param) = parseQueryAndParam sql filter
        let mapper u p = 
            let parent = if isNull (box p) then None else Some(p)
            {u with Parent=parent}
        cn.QueryAsync<Unit, Unit, Unit>(query, mapper, param)
    let mapUnits = mapUnits' queryUnitsSql
    let mapUnit id = mapUnits (WhereId("u.id", id))

    let queryUnits connStr query = async {
        let filter = 
            match query with 
            | None -> Where("u.parent_id IS NULL")
            | Some(q) -> WhereParam("u.name ILIKE @Query OR u.description ILIKE @Query", {Query=like q})
        return! fetchAll<Unit> connStr (mapUnits(filter))
    }

    let queryUnit connStr id = async {
        return! fetchOne<Unit> connStr mapUnit id
    }

    let insertUnit connStr unit = async {
        return! insert<Unit> connStr unit mapUnit
    }

    let updateUnit connStr (unit:Unit) = async {
        return! update<Unit> connStr unit.Id unit mapUnit
    }

    let deleteUnitSql = """
        DELETE FROM unit_members WHERE unit_id=@Id;
        DELETE FROM support_relationships WHERE unit_id=@Id;
        DELETE FROM units WHERE id=@Id"""
    let deleteUnit connStr (unit:Unit) = async {
        return! execute connStr deleteUnitSql {Id=unit.Id}    
    }

    let queryUnitChildren connStr (unit:Unit) = async {
        return! fetchAll<Unit> connStr (mapUnits(WhereId("u.parent_id", unit.Id)))
    }

    let queryUnitMembers connStr (unit:Unit) = async {
        return! fetchAll connStr (mapUnitMembers (WhereId("u.id", unit.Id)))
    }

    let queryUnitSupportedDepartments connStr (unit:Unit) = async {
        return! fetchAll connStr (mapSupportRelationships(WhereId("u.id", unit.Id)))
    }


    // ***********
    // Departments
    // ***********

    let queryDepartmentsSql = """
        SELECT d.* FROM departments d"""

    let mapDepartments filter (cn:Cn) = 
        parseQueryAndParam queryDepartmentsSql filter
        |> cn.QueryAsync<Department>

    let mapDepartment id = mapDepartments (WhereId("d.id", id))

    let queryDepartments connStr query = async {
        let filter = 
            match query with 
            | None -> Unfiltered
            | Some(q) -> WhereParam("name ILIKE @Query OR description ILIKE @Query", {Query=like q})
        return! fetchAll<Department> connStr (mapDepartments filter)
            
    }

    let queryDepartment connStr id = async {
        return! fetchOne<Department> connStr mapDepartment id
    }

    let insertDepartment connStr department = async {
        return! insert<Department> connStr department mapDepartment
    }

    let updateDepartment connStr (department:Department) = async {
        return! update<Department> connStr department.Id department mapDepartment
    }

    let queryDeptSupportingUnits connStr id = async {
        return! fetchAll connStr (mapSupportRelationships (WhereId("d.id", id)))
    }

    let queryDeptMemberUnitsSql = """
        SELECT DISTINCT ON (u.id) u.*, pu.* FROM units u
        LEFT JOIN units pu on pu.id = u.parent_id
        JOIN unit_members m ON m.unit_id = u.id
        JOIN people p on p.id = m.person_id"""
    let queryDeptMemberUnits connStr id = async {
        let mapDeptMemberUnits = mapUnits' queryDeptMemberUnitsSql
        return! fetchAll<Unit> connStr (mapDeptMemberUnits(WhereId("p.department_id", id)))
    }


    // ***********
    // People
    // ***********

    let queryPersonSql = """
        SELECT p.*, d.*
        FROM people p
        JOIN departments d on d.id = p.department_id """

    let mapPeople filter (cn:Cn) = 
        let (query, param) = parseQueryAndParam queryPersonSql filter
        let mapper (p:Person) d = {p with Department=d}
        cn.QueryAsync<Person, Department, Person>(query, mapper, param)

    let mapPerson id = mapPeople (WhereId("p.id", id))

    let queryPeople connStr query = async {
        let filter = 
            match query with
            | None ->  Unfiltered
            | Some(q) -> WhereParam("p.name ILIKE @Query OR p.netid ILIKE @Query", {Query=like q})
        return! fetchAll connStr (mapPeople(filter))
    }

    let queryPerson connStr id = async {
        return! fetchOne<Person> connStr mapPerson id
    }

    let queryPersonByNetId connStr netId = async {
        let! people = fetchAll<Person> connStr (mapPeople(WhereParam("netid = @NetId", {NetId=netId})))
        match people with
        | Ok(result,_) ->
            match result |> Seq.tryHead with
            | Some(p) -> return ok (netId, Some(p.Id))
            | None -> return ok (netId, None)
        | Bad(msgs) -> return Bad(msgs)
    }

    let queryPersonMemberships connStr id = async {
        return! fetchAll connStr (mapUnitMembers(WhereId("p.id", id)))
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
