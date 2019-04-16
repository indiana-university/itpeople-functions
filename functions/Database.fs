// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open Types
open Util
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
        | Param of obj
        | Where of WhereClause
        | WhereId of WhereClause * Id
        | WhereParam of WhereClause * obj

    let like = sprintf "%%%s%%"
    let where = sprintf "%s WHERE %s"

    let parseQueryAndParam sql filter = 
        match filter with
        | Unfiltered -> (sql, ():>obj)
        | Param param -> (sql, param)
        | Where clause -> ((where sql clause), ():>obj)
        | WhereId (clause,id)-> ((where sql clause+"=@Id"), {Id=id}:>obj)
        | WhereParam (clause,param)-> ((where sql clause), param)

    let handleDbExn name resource (exn:System.Exception) = 
        let msg = sprintf "Database error on %s %s: %s" name resource exn.Message
        Error (Status.InternalServerError, msg)


    let fetchAll<'T> connStr (mapper:MapMany<'T>) = async {
        try
            use cn = new NpgsqlConnection(connStr)
            let! result = cn |> mapper |> Async.AwaitTask
            return Ok result
        with exn -> return handleDbExn "fetch all" (typedefof<'T>.Name) exn
    }

    let fetchOne<'T> connStr (mapper:MapOne<'T>) id  = async {
        try
            use cn = new NpgsqlConnection(connStr)
            let! result = mapper id cn |> Async.AwaitTask
            if Seq.isEmpty result 
            then return Error(Status.NotFound, sprintf "No %s was found with ID %d." (typedefof<'T>.Name) id)
            else return result |> Seq.head |> Ok
        with exn -> return handleDbExn "fetch one" (typedefof<'T>.Name) exn
    }

    let insertImpl<'T> connStr (obj:'T) = async {
        try
            use cn = new NpgsqlConnection(connStr)
            let! result = cn.InsertAsync<'T>(obj) |> Async.AwaitTask
            return Ok (result.GetValueOrDefault())
        with exn -> return handleDbExn "insert" (typedefof<'T>.Name) exn
    }

    let insert<'T> connStr writeParams =
        insertImpl<'T> connStr
        >=> fetchOne<'T> connStr writeParams

    let updateImpl<'T> connStr id (obj:^T) = async {
        try
            use cn = new NpgsqlConnection(connStr)
            let! _ = cn.UpdateAsync<'T>(obj) |> Async.AwaitTask
            return Ok id
        with exn -> return handleDbExn "update" (typedefof<'T>.Name) exn
    }

    let update<'T> connStr writeParams id  = 
        updateImpl<'T> connStr id
        >=> fetchOne<'T> connStr writeParams

    let delete<'T> connStr (id:int) = async {
        try
            use cn = new NpgsqlConnection(connStr)
            let! _ = cn.DeleteAsync<'T>(id) |> Async.AwaitTask
            return () |> Ok
        with exn -> return handleDbExn "update" (typedefof<'T>.Name) exn
    }

    let execute connStr sql parameters = async {
        try
            use cn = new NpgsqlConnection(connStr)
            let! _ = cn.ExecuteAsync(sql, parameters) |> Async.AwaitTask
            return () |> Ok
        with exn -> return handleDbExn "execute" "" exn
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

    let queryMemberships connStr =
        fetchAll connStr (mapUnitMembers Unfiltered)

    let queryMembership connStr id =
        fetchOne connStr mapUnitMember id 

    let insertMembership connStr =
        insert<UnitMember> connStr mapUnitMember

    let updateMembership connStr (unitMember:UnitMember) =
        update<UnitMember> connStr mapUnitMember unitMember.Id unitMember

    let deleteMembership connStr unitMember =
        delete<UnitMember> connStr (identity unitMember)


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

    let mapSupportRelationship id = 
        mapSupportRelationships (WhereId("s.id", id))

    let querySupportRelationships connStr =
        fetchAll<SupportRelationship> connStr (mapSupportRelationships Unfiltered)

    let querySupportRelationship connStr id =
        fetchOne connStr mapSupportRelationship id

    let insertSupportRelationship connStr  =
        insert<SupportRelationship> connStr mapSupportRelationship

    let updateSupportRelationship connStr (supportRelationship:SupportRelationship) =
        update<SupportRelationship> connStr mapSupportRelationship supportRelationship.Id supportRelationship

    let deleteSupportRelationship connStr supportRelationship =
        delete<SupportRelationship> connStr (identity supportRelationship)
   

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

    let queryUnits connStr query =
        let filter = 
            match query with 
            | None -> Where("u.parent_id IS NULL")
            | Some(q) -> WhereParam("u.name ILIKE @Query OR u.description ILIKE @Query", {Query=like q})
        fetchAll<Unit> connStr (mapUnits(filter))

    let queryUnit connStr =
        fetchOne<Unit> connStr mapUnit

    let insertUnit connStr =
        insert<Unit> connStr mapUnit

    let updateUnit connStr (unit:Unit) =
        update<Unit> connStr mapUnit unit.Id unit

    let deleteUnitSql = """
        DELETE FROM unit_members WHERE unit_id=@Id;
        DELETE FROM support_relationships WHERE unit_id=@Id;
        DELETE FROM units WHERE id=@Id"""

    let deleteUnit connStr (unit:Unit) =
        execute connStr deleteUnitSql {Id=unit.Id}

    let queryUnitChildren connStr (unit:Unit) =
        fetchAll<Unit> connStr (mapUnits(WhereId("u.parent_id", unit.Id)))

    let queryUnitMembers connStr (unit:Unit) =
        fetchAll connStr (mapUnitMembers (WhereId("u.id", unit.Id)))

    let queryUnitSupportedDepartments connStr (unit:Unit) =
        fetchAll connStr (mapSupportRelationships(WhereId("u.id", unit.Id)))
    


    // This query is recursive. (Whoa.)
    // Given some unit id (ChildId) it will recurse to 
    //  find every parent, grandparent, etc of that unit
    //  until it reaches the top of the org chart.
    // If it finds the second specified unit id (ParentId) 
    //  anywhere in that hierarchy, they query will return
    //  information for the ChildId unit. 
    // Otherwise it returns nothing.
    let queryUnitParentageSql = """
    WITH RECURSIVE parentage AS (
     -- first row
     SELECT id, name, parent_id
     FROM units
     WHERE id = @ChildId -- DCD, 157
     UNION
     -- recurse
     SELECT u.id, u.name, u.parent_id
     FROM units u
     INNER JOIN parentage p ON p.parent_id = u.id
    ) 
    SELECT * FROM parentage
    WHERE id = @ChildId
    AND EXISTS (
    	SELECT id FROM parentage 
    	WHERE id = @ParentId -- UITS, 1
    )"""

    type Descendant = 
      { ParentId: Id
        ChildId: Id }

    let tryGetFirstResult seq = seq |> Seq.tryHead |> Ok |> async.Return

    let queryUnitGetDescendantOfParent connStr  =
        let makeMapper (parentId, childId) =
            let param = {ParentId=parentId; ChildId=childId}
            (fun (cn:Cn) -> cn.QueryAsync<Unit>(queryUnitParentageSql, param)) |> Ok |> async.Return
        makeMapper    
        >=> fetchAll connStr
        >=> tryGetFirstResult

    // ***********
    // Departments
    // ***********

    let queryDepartmentsSql = """
        SELECT d.* FROM departments d"""

    let mapDepartments filter (cn:Cn) = 
        parseQueryAndParam queryDepartmentsSql filter
        |> cn.QueryAsync<Department>

    let mapDepartment id = 
        mapDepartments (WhereId("d.id", id))

    let queryDepartments connStr query =
        let filter = 
            match query with 
            | None -> Unfiltered
            | Some(q) -> WhereParam("name ILIKE @Query OR description ILIKE @Query", {Query=like q})
        fetchAll<Department> connStr (mapDepartments filter)

    let queryDepartment connStr id =
        fetchOne<Department> connStr mapDepartment id

    let queryDeptSupportingUnits connStr department = 
        fetchAll connStr (mapSupportRelationships (WhereId("d.id", (identity department))))

    let queryDeptMemberUnitsSql = """
        SELECT DISTINCT ON (u.id) u.*, pu.* FROM units u
        LEFT JOIN units pu on pu.id = u.parent_id
        JOIN unit_members m ON m.unit_id = u.id
        JOIN people p on p.id = m.person_id"""
    let queryDeptMemberUnits connStr department =
        let mapDeptMemberUnits = mapUnits' queryDeptMemberUnitsSql
        fetchAll<Unit> connStr (mapDeptMemberUnits(WhereId("p.department_id", (identity department))))

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

    let mapPerson id = 
        mapPeople (WhereId("p.id", id))

    let queryPeople connStr query =
        let filter = 
            match query with
            | None ->  Unfiltered
            | Some(q) -> WhereParam("p.name ILIKE @Query OR p.netid ILIKE @Query", {Query=like q})
        fetchAll connStr (mapPeople(filter))

    let queryPerson connStr id =
        fetchOne<Person> connStr mapPerson id

    let queryPersonByNetId connStr netId = async {
        let! people = fetchAll<Person> connStr (mapPeople(WhereParam("netid = @NetId", {NetId=netId})))
        let result = 
            match people with
            | Ok result ->
                match result |> Seq.tryHead with
                | Some(p) -> Ok (netId, Some(p.Id))
                | None -> Ok (netId, None)
            | Error(msgs) -> Error(msgs)
        return result
    }

    let queryPersonMemberships connStr id =
        fetchAll connStr (mapUnitMembers(WhereId("p.id", id)))  

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
        GetDescendantOfParent = queryUnitGetDescendantOfParent connStr
        GetToolGroups = fun unit -> stub Seq.empty
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

    let MemberToolsRepository (connStr) : MemberToolsRepository = {
        // Get = fun id -> stub knopeMembership
        GetAll = fun () -> stub Seq.empty
        // Create = fun req -> stub knopeMembership
        // Update = fun req -> stub knopeMembership
        // Delete = fun id -> stub ()
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
        MemberTools = MemberToolsRepository(connStr)
        SupportRelationships = SupportRelationshipsRepository(connStr)
    }
