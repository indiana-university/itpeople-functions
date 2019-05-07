// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open System.Net.Http
open System.Net.Http.Headers

open Core.Types
open Database.Command
open Api
open Dapper

module DatabaseRepository =

    // ***********
    // Memberships
    // ***********
    let queryUnitMemberSql = """
        SELECT m.*, u.*, p.*, umt.*
        FROM unit_members m
        JOIN units u on u.id = m.unit_id
        LEFT JOIN people p on p.id = m.person_id
        LEFT JOIN unit_member_tools umt on umt.membership_id=m.id"""


    let mapUnitMembers filter (cn:Cn) = 
        let (query, param) = parseQueryAndParam queryUnitMemberSql filter
        let mapper (m:UnitMember) u p umt = 
            let person = if isNull (box p) then None else Some(p)
            let tools = if isNull (box umt) then Seq.empty else Seq.ofList [umt]
            {m with Unit=u; Person=person; MemberTools=tools}
        cn.QueryAsync<UnitMember, Unit, Person, MemberTool, UnitMember>(query, mapper, param)

    let collectMemberTools (unitMembers:seq<UnitMember>) = 
        unitMembers
        |> Seq.groupBy (fun um -> um.Id)
        |> Seq.map (fun (_,vals) -> 
            let um = vals |> Seq.head
            let tools = vals |> Seq.collect (fun v -> v.MemberTools)
            {um with MemberTools=tools})
        |> Ok
        |> async.Return

    let requireOne seq = 
        if Seq.isEmpty seq
        then Error (Status.NotFound, "No membership was found with that ID") |> async.Return
        else Ok seq |> async.Return

    let head seq = Seq.head seq |> Ok |> async.Return

    let queryMemberships connStr =
        fetchAll connStr (mapUnitMembers(Unfiltered))
        >>= collectMemberTools

    let queryMembership connStr id =
        fetchAll connStr (mapUnitMembers (WhereId("m.id", id)))
        >>= requireOne
        >>= collectMemberTools
        >>= head
        
    let insertMembership connStr unitMember =
        insertImpl<UnitMember> connStr unitMember
        >>= queryMembership connStr

    let updateMembership connStr (unitMember:UnitMember) =
        updateImpl<UnitMember> connStr unitMember.Id unitMember
        >>= queryMembership connStr

    let deleteMembershipSql = """
        DELETE FROM unit_member_tools WHERE membership_id=@Id;
        DELETE FROM unit_members WHERE id=@Id;"""

    let deleteMembership connStr (unitMember:UnitMember) =
        execute connStr deleteMembershipSql {Id=unitMember.Id}


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
        >>= collectMemberTools

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

    let insertPerson connStr person =
        queryDepartments connStr (Some(person.Notes))
        >>= fun results -> 
                if Seq.isEmpty results
                then Error(Status.BadRequest, (sprintf "This person's department, '%s', is not known to the IT People directory." person.Notes)) |> ar
                else results |> Seq.head |> Ok |> ar
        >>= fun d -> { person with Notes=""; DepartmentId=d.Id } |> Ok |> ar
        >>= insert<Person> connStr mapPerson

    let queryPersonMemberships connStr id =
        fetchAll connStr (mapUnitMembers(WhereId("p.id", id)))
        >>= collectMemberTools

    // ***********
    // Tools
    // ***********

    let queryToolsSql = """SELECT * from tools t"""    
    let queryToolPermissionsSql = """
    SELECT DISTINCT
    	p.netid,
        t.name as tool_name,
    	COALESCE(d.name,'') as department_name
    FROM
        unit_member_tools umt
        JOIN unit_members um on um.id = umt.membership_id
        JOIN people p on p.id = um.person_id
        JOIN tools t on t.id = umt.tool_id
    	-- Join to departments for departmentally-scoped tools.
        LEFT JOIN support_relationships sr ON sr.unit_id = um.unit_id AND t.department_scoped = TRUE
        LEFT JOIN departments d ON d.id = sr.department_id
    WHERE 
    	(d.id IS NOT NULL) 	             -- departmentally-scoped tools
    	OR (t.department_scoped = FALSE) -- globally-scoped tools
    ORDER BY netid, tool_name, department_name"""    

    let map<'T> query filter (cn:Cn) = 
        parseQueryAndParam query filter
        |> cn.QueryAsync<'T>

    let mapTool id = 
        map<Tool> queryToolsSql (WhereId("t.id", id))

    let queryTools connStr =
        fetchAll<Tool> connStr (map queryToolsSql Unfiltered)

    let queryToolPermissions connStr =
        fetchAll<ToolPermission> connStr (map queryToolPermissionsSql Unfiltered)

    let queryTool connStr id =
        fetchOne<Tool> connStr mapTool id

    // *********************
    // Member Tools
    // *********************

    let queryMemberToolsSql = """
        SELECT * from unit_member_tools umt"""

    let mapMemberTools filter (cn:Cn) = 
        parseQueryAndParam queryMemberToolsSql filter
        |> cn.QueryAsync<MemberTool>

    let mapMemberTool id = 
        mapMemberTools (WhereId("umt.id", id))

    let queryMemberTools connStr =
        fetchAll<MemberTool> connStr (mapMemberTools Unfiltered)

    let queryMemberTool connStr id =
        fetchOne connStr mapMemberTool id

    let insertMemberTool connStr  =
        insert<MemberTool> connStr mapMemberTool

    let updateMemberTool connStr (memberTool:MemberTool) =
        update<MemberTool> connStr mapMemberTool memberTool.Id memberTool

    let deleteMemberTool connStr memberTool =
        delete<MemberTool> connStr (identity memberTool)

    let getMemberToolMember connStr (memberTool:MemberTool) =
        queryMembership connStr memberTool.MembershipId
        >>= fun membership -> ok (memberTool, membership)
   
    // *********************
    // HR Lookups
    // *********************

    let lookupCanonicalHrPeople sharedSecret (query:NetId option) =
        match query with
        | None -> Ok Seq.empty<Person> |> ar
        | Some(q) ->
            let url = sprintf "https://itpeople-adapter.apps.iu.edu/people/%s" q
            let msg = new HttpRequestMessage(HttpMethod.Get, url)
            msg.Headers.Authorization <-  AuthenticationHeaderValue("Bearer", sharedSecret)
            sendAsync<seq<Person>> msg


    let isServiceAdmin' netid = 
        ["jhoerr"; "kendjone"; "jerussel"; "brrund"; "johndoe"] 
        |> Seq.contains netid

    let isServiceAdmin netid =  isServiceAdmin' netid |> ok

    let hasCascadedUnitPermsSql = """
    WITH RECURSIVE parentage AS (
       -- first row
       SELECT u.id, u.name, u.parent_id
       FROM units u
       WHERE u.id = @UnitId
       UNION
       -- recurse
       SELECT u.id, u.name, u.parent_id
       FROM units u
       INNER JOIN parentage p ON p.parent_id = u.id
    ) 
    SELECT 
    	-- select 'true' if this person has the specified permissions
    	-- in this unit or any parent unit and 'false' otherwise.
    	CASE WHEN EXISTS (
    		SELECT pa.id, um.role, pe.netid
    		FROM parentage pa
    		JOIN unit_members um on pa.id = um.unit_id
    		JOIN people pe on pe.id = um.person_id 
    		WHERE
    			pe.netid = @NetId 
    			AND um.permissions = ANY(@UnitPermissions)
    	) 
    	THEN TRUE
    	ELSE FALSE
    	END"""

    type AuthParams = {UnitId: Id; NetId: NetId; UnitPermissions: int[]}

    let hasCascadedUnitPerms permissions connStr netid unitId  =
        let param = 
          { UnitId=unitId
            NetId=netid 
            UnitPermissions= permissions |> Seq.map int |> Seq.toArray }
        let query (cn:Cn) = cn.QuerySingleAsync<bool>(hasCascadedUnitPermsSql, param)
        fetch connStr query

    let isServiceAdminOrHasUnitPermissions permissions connStr netid unitId =
        if isServiceAdmin' netid
        then ok true
        else hasCascadedUnitPerms permissions connStr netid unitId

    let isUnitManager = 
        isServiceAdminOrHasUnitPermissions [ UnitPermissions.Owner; UnitPermissions.ManageMembers ]

    let isUnitToolManager = 
        isServiceAdminOrHasUnitPermissions [ UnitPermissions.Owner; UnitPermissions.ManageTools ]

    let People(connStr) = {
        TryGetId = queryPersonByNetId connStr
        GetAll = queryPeople connStr
        Get = queryPerson connStr
        Create = insertPerson connStr
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
        GetAll = fun () -> queryMemberTools connStr
        Get = queryMemberTool connStr
        Create = insertMemberTool connStr
        Update = updateMemberTool connStr
        Delete = deleteMemberTool connStr
        GetMember = getMemberToolMember connStr
    }

    let ToolsRepository (connStr) : ToolsRepository = {
        GetAll = fun () -> queryTools connStr
        GetAllPermissions = fun () -> queryToolPermissions connStr
        Get = queryTool connStr
    }

    let SupportRelationshipsRepository(connStr) = {
        GetAll = fun () -> querySupportRelationships connStr 
        Get = querySupportRelationship connStr
        Create = insertSupportRelationship connStr
        Update = updateSupportRelationship connStr
        Delete = deleteSupportRelationship connStr
    }

    let HrRepository(sharedSecret) = {
        GetAllPeople = lookupCanonicalHrPeople sharedSecret
    }

    let AuthorizationRepository(connStr) = {
        IsServiceAdmin = isServiceAdmin
        IsUnitManager = isUnitManager connStr
        IsUnitToolManager = isUnitToolManager connStr
    }

    let Repository(connStr, sharedSecret) = {
        People = People(connStr)
        Departments = Departments(connStr)
        Units = Units(connStr)
        Memberships = Memberships(connStr)
        MemberTools = MemberToolsRepository(connStr)
        Tools = ToolsRepository(connStr)
        SupportRelationships = SupportRelationshipsRepository(connStr)
        Hr = HrRepository(sharedSecret)
        Authorization = AuthorizationRepository(connStr)
    }
