// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open System.Net.Http

open Core.Types
open Core.Util
open Database.Command
open Dapper

module DatabaseRepository =

    // ***********
    // Memberships
    // ***********
    let queryUnitMemberSql = """
        SELECT m.*, u.*, p.*, d.*, umt.*
        FROM unit_members m
        JOIN units u on u.id = m.unit_id
        LEFT JOIN people p on p.id = m.person_id
        LEFT JOIN departments d on d.id = p.department_id
        LEFT JOIN unit_member_tools umt on umt.membership_id=m.id"""

    let mapUnitMembers filter (cn:Cn) = 
        let (query, param) = parseQueryAndParam queryUnitMemberSql filter
        let mapper (m:UnitMember) u (p:Person) d umt = 
            let person = if isNull (box p) then None else Some({p with Department=d})
            let tools = if isNull (box umt) then Seq.empty else Seq.ofList [umt]
            {m with Unit=u; Person=person; MemberTools=tools}
        cn.QueryAsync<UnitMember, Unit, Person, Department, MemberTool, UnitMember>(query, mapper, param)

    let stripNotes (unitMembers:seq<UnitMember>) =
        unitMembers
        |> Seq.map (fun um -> {um with Notes=""})

    let collectMemberTools (unitMembers:seq<UnitMember>) = 
        unitMembers
        |> Seq.groupBy (fun um -> um.Id)
        |> Seq.map (fun (_,vals) -> 
            let um = vals |> Seq.head
            let tools = vals |> Seq.collect (fun v -> v.MemberTools)
            {um with MemberTools=tools})
        |> Ok
        |> async.Return

    let requireOne description seq  = 
        if Seq.isEmpty seq
        then error (Status.NotFound, sprintf "No %s was found with that ID" description)
        else ok ()

    let head seq = Seq.head seq |> Ok |> async.Return

    let queryMemberships connStr = pipeline {
        let! members = fetchAll (mapUnitMembers(Unfiltered)) connStr
        return! members |> stripNotes |> collectMemberTools
    }

    let queryMembership connStr id = pipeline {
        let! members = fetchAll (mapUnitMembers (WhereId("m.id", id))) connStr
        do! requireOne "membership" members
        let! membersWithTools = members |> stripNotes |> collectMemberTools
        return membersWithTools |> Seq.head
    }
        
    let insertMembership connStr unitMember = pipeline {
        let! id = insertImpl<UnitMember> connStr unitMember
        return! queryMembership connStr id
    }

    let updateMembership connStr unitMember = pipeline {
        let! id = updateImpl<UnitMember> connStr unitMember
        return! queryMembership connStr id
    }

    let deleteMembershipSql = """
        DELETE FROM unit_member_tools WHERE membership_id=@Id;
        DELETE FROM unit_members WHERE id=@Id;"""

    let deleteMembership connStr id =
        execute connStr deleteMembershipSql {Id=id}


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

    let querySupportRelationships = fetchAll<SupportRelationship> (mapSupportRelationships Unfiltered)

    let querySupportRelationship = fetchOne<SupportRelationship> mapSupportRelationship

    let insertSupportRelationship = insert<SupportRelationship> mapSupportRelationship

    let updateSupportRelationship = update<SupportRelationship> mapSupportRelationship

    let deleteSupportRelationship = deleteById<SupportRelationship>
   

    // *********************
    // Building Relationships
    // *********************

    let queryBuildingRelationshipSql = """
        SELECT r.*, b.*, u.*
        FROM building_relationships r
        JOIN buildings b on b.id = r.building_id
        JOIN units u on u.id = r.unit_id """

    let mapBuildingRelationships filter (cn:Cn) = 
        let (query, param) = parseQueryAndParam queryBuildingRelationshipSql filter
        let mapper r b u = {r with Unit=u; Building=b}
        cn.QueryAsync<BuildingRelationship, Building, Unit, BuildingRelationship>(query, mapper, param)

    let mapBuildingRelationship id = mapBuildingRelationships (WhereId("r.id", id))

    let queryBuildingRelationships = fetchAll<BuildingRelationship> (mapBuildingRelationships Unfiltered)

    let queryBuildingRelationship = fetchOne<BuildingRelationship> mapBuildingRelationship

    let insertBuildingRelationship = insert<BuildingRelationship> mapBuildingRelationship

    let updateBuildingRelationship = update<BuildingRelationship> mapBuildingRelationship

    let deleteBuildingRelationship = deleteById<BuildingRelationship>
   

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
            | Some(q) -> WhereParam("u.name ILIKE @Query OR u.description ILIKE @Query ORDER BY u.name LIMIT 25", {Query=like q})
        fetchAll<Unit> (mapUnits(filter)) connStr

    let queryUnit = fetchOne<Unit> mapUnit

    let insertUnit = insert<Unit> mapUnit

    let updateUnit = update<Unit> mapUnit

    let deleteUnitSql = """
        DELETE FROM unit_members WHERE unit_id=@Id;
        DELETE FROM support_relationships WHERE unit_id=@Id;
        DELETE FROM units WHERE id=@Id"""

    let deleteUnit connStr unitId =
        execute connStr deleteUnitSql {Id=unitId}

    let queryUnitChildren connStr unitId =
        fetchAll<Unit> (mapUnits(WhereId("u.parent_id", unitId))) connStr

    let queryUnitMembers connStr options = pipeline {
        match options with
        | MembersWithoutNotes(unitId) ->
            let! members = fetchAll (mapUnitMembers (WhereId("u.id", unitId))) connStr
            return! members |> stripNotes |> collectMemberTools
        | MembersWithNotes(unitId) ->
            let! members = fetchAll (mapUnitMembers (WhereId("u.id", unitId))) connStr
            return! collectMemberTools members
    }

    let queryUnitSupportedDepartments connStr unitId =
        fetchAll (mapSupportRelationships(WhereId("u.id", unitId))) connStr

    let queryUnitSupportedBuildings connStr unitId =
        fetchAll (mapBuildingRelationships(WhereId("u.id", unitId))) connStr

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

    let queryUnitGetDescendantOfParent connStr (parentId, childId) = pipeline {
        let param = {ParentId=parentId; ChildId=childId}
        let mapper = fun (cn:Cn) -> cn.QueryAsync<Unit>(queryUnitParentageSql, param)
        let! descendants = fetchAll<Unit> mapper connStr
        return descendants |> Seq.tryHead
    }


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
            | Some(q) -> WhereParam("name ILIKE @Query OR description ILIKE @Query ORDER BY name LIMIT 25", {Query=like q})
        fetchAll<Department> (mapDepartments filter) connStr

    let queryDepartment = fetchOne<Department> mapDepartment

    let queryDeptSupportingUnits connStr departmentId = 
        fetchAll<SupportRelationship> (mapSupportRelationships (WhereId("d.id", departmentId))) connStr

    let queryDeptMemberUnitsSql = """
        SELECT DISTINCT ON (u.id) u.*, pu.* FROM units u
        LEFT JOIN units pu on pu.id = u.parent_id
        JOIN unit_members m ON m.unit_id = u.id
        JOIN people p on p.id = m.person_id"""

    let queryDeptMemberUnits connStr departmentId =
        let mapDeptMemberUnits = mapUnits' queryDeptMemberUnitsSql
        fetchAll<Unit> (mapDeptMemberUnits(WhereId("p.department_id", departmentId))) connStr

    // ***********
    // Buildings
    // ***********

    let queryBuildingsSql = """
        SELECT b.* FROM buildings b"""

    let mapBuildings filter (cn:Cn) = 
        parseQueryAndParam queryBuildingsSql filter
        |> cn.QueryAsync<Building>

    let mapBuilding id = 
        mapBuildings (WhereId("b.id", id))

    type BuildingQuery =
      { Query: string
        QueryNoDash: string }

    let queryBuildings connStr query =
        let filter = 
            match query with 
            | None -> Unfiltered
            | Some(q:string) -> 
                let param = {Query=like q; QueryNoDash=like (q.Replace("-",""))}
                WhereParam("name ILIKE @Query OR address ILIKE @Query OR code ILIKE @Query OR code ILIKE @QueryNoDash ORDER BY name LIMIT 25", param)
        fetchAll<Building> (mapBuildings filter) connStr

    let queryBuilding = fetchOne<Building> mapBuilding

    let queryBuildingSupportingUnits connStr building= 
        let filter = WhereId ("b.id", (identity building))
        fetchAll<BuildingRelationship> (mapBuildingRelationships filter) connStr

    // ***********
    // People
    // ***********

    let queryPersonSql = """
        SELECT DISTINCT p.*, d.*
        FROM people p
        JOIN departments d on d.id = p.department_id
        LEFT JOIN unit_members um on um.person_id = p.id"""

    let mapPeople filter (cn:Cn) = 
        let (query, param) = parseQueryAndParam queryPersonSql filter
        let mapper (p:Person) d = {p with Department=d}
        cn.QueryAsync<Person, Department, Person>(query, mapper, param)

    let mapPerson id = 
        mapPeople (WhereId("p.id", id))

    let queryPeople connStr (query:PeopleQuery) =
        let param =
          { Query = if query.Query = "" then "" else like query.Query
            Classes = query.Classes
            Roles = query.Roles
            Permissions = query.Permissions
            Interests = query.Interests |> Array.map like
            Campuses = query.Campuses |> Array.map like
            Area=query.Area }
        // printfn "Query Param: %A" param
        let whereClause = 
            """(@Query='' OR (p.name ILIKE @Query OR p.netid ILIKE @Query))
            AND (@Classes=0 OR (p.responsibilities & @Classes <> 0))
            -- The built-in 'cardinality' function returns the number of elements in an array.
            -- If there are no filter elements then don't try to apply that filter.
            AND (CARDINALITY(@Interests)=0 OR (p.expertise ILIKE ANY (@Interests)))
            AND (CARDINALITY(@Campuses)=0 OR (p.campus ILIKE ANY (@Campuses)))
            AND (CARDINALITY(@Roles)=0 OR (um.role = ANY (@Roles)))
            AND (CARDINALITY(@Permissions)=0 OR (um.permissions = ANY (@Permissions)))
            AND (@Area=0
                -- Area=1: UITS unit members only
                OR (@Area=1 AND um.unit_id IN (
                    WITH RECURSIVE parentage AS (
                        SELECT id, id as root_id FROM units WHERE parent_id IS NULL
                    UNION
                        SELECT u.id, p.root_id as root_id FROM units u INNER JOIN parentage p ON u.parent_id = p.id
                    )
                    SELECT id FROM parentage
                    WHERE root_id = 1))
                -- Area=2: Edge unit members only
                OR (@Area=2 AND um.unit_id IN (
                    WITH RECURSIVE parentage AS (
                        SELECT id, id as root_id FROM units WHERE parent_id IS NULL
                    UNION
                        SELECT u.id, p.root_id as root_id FROM units u INNER JOIN parentage p ON u.parent_id = p.id
                    )
                    SELECT id FROM parentage
                    WHERE root_id <> 1))
                -- Area=3: UITS+Edge unit members only
                OR (@Area=3 AND um.unit_id IN (
                    WITH RECURSIVE parentage AS (
                        SELECT id, id as root_id FROM units WHERE parent_id IS NULL
                    UNION
                        SELECT u.id, p.root_id as root_id FROM units u INNER JOIN parentage p ON u.parent_id = p.id
                    )
                    SELECT id FROM parentage))                
                )
            ORDER BY p.netid"""
            
        fetchAll<Person> (mapPeople(WhereParam(whereClause, param))) connStr
    
    let queryPersonById = fetchOne<Person> mapPerson

    let queryPeopleWithHr connStr netId =
        let sql = """
        WITH cte AS
        (
        	(   -- find matching people by netid
        		SELECT
        			COALESCE(p.netid, hr.netid) as netid, 
        			COALESCE(p.name, hr.name) as name, 
        			COALESCE(p.name_first, hr.name_first) as name_first, 
        			COALESCE(p.name_last, hr.name_last) as name_last, 
        			COALESCE(p.position, hr.position) as position,
        			COALESCE(p.campus, hr.campus) as campus,
        			COALESCE(p.campus_phone, hr.campus_phone) as campus_phone,
        			COALESCE(p.campus_email, hr.campus_email) as campus_email,
        			COALESCE(p.expertise, '') as expertise,
        			COALESCE(p.responsibilities, 0) as responsibilities,
        			COALESCE(p.is_service_admin, false) as is_service_admin,
        			COALESCE(p.location, '') as location,
        			COALESCE(p.photo_url, '') as photo_url,
        			COALESCE(p.notes, '') as notes,
        			1 as rank
        		FROM people p
        		FULL OUTER JOIN hr_people hr using (netid)
        		WHERE COALESCE(p.netid, hr.netid) ILIKE @NetId
        		LIMIT 10
        	)
        	UNION
        	(   -- find matching people by name
        		SELECT
        			COALESCE(p.netid, hr.netid) as netid, 
                    COALESCE(p.name, hr.name) as name, 
        			COALESCE(p.name_first, hr.name_first) as name_first, 
        			COALESCE(p.name_last, hr.name_last) as name_last, 
        			COALESCE(p.position, hr.position) as position,
        			COALESCE(p.campus, hr.campus) as campus,
        			COALESCE(p.campus_phone, hr.campus_phone) as campus_phone,
        			COALESCE(p.campus_email, hr.campus_email) as campus_email,
        			COALESCE(p.expertise, '') as expertise,
        			COALESCE(p.responsibilities, 0) as responsibilities,
        			COALESCE(p.is_service_admin, false) as is_service_admin,
        			COALESCE(p.location, '') as location,
        			COALESCE(p.photo_url, '') as photo_url,
        			COALESCE(p.notes, '') as notes,
        			2 as rank
        		FROM people p
        		FULL OUTER JOIN hr_people hr using (netid)
        		WHERE COALESCE(p.name, hr.name) ILIKE @NetId
        		LIMIT 10
        	)
        	ORDER BY rank -- favor netid matches
        	LIMIT 10
        )
        SELECT DISTINCT -- deduplicate results
        	netid, name, name_first, name_last, position, campus, campus_phone, campus_email, 
        	expertise, responsibilities, is_service_admin, location, photo_url, notes
        FROM cte"""
        let param = {NetId=like netId}
        fetchAll<Person> (fun cn -> cn.QueryAsync<Person>(sql, param)) connStr

    let queryHrPerson connStr netId = pipeline {
        let sql = """
        SELECT 
            0 as id,
            hr.netid, 
            hr.name, 
            hr.name_first,
            hr.name_last,
            COALESCE(hr.position, '') as position,
            hr.campus, 
            COALESCE(hr.campus_phone, '') as campus_phone,
            COALESCE(hr.campus_email, '') as campus_email,
            false as is_service_admin,
            0 as responsibiliies,
            '' as location,
            '' as photo_url,
            '' as expertise,
            '' as notes,
            d.id as department_id 
        FROM hr_people hr 
        LEFT JOIN departments d on d.name = hr.hr_department
        WHERE netid ILIKE @NetId"""
        let param = {NetId=netId}
        let! people = fetchAll<Person> (fun cn -> cn.QueryAsync<Person>(sql, param)) connStr        
        do! requireOne "HR person" people
        return people |> Seq.head
    }

    let queryPersonByNetId connStr netid = pipeline {
        let! people = fetchAll<Person> (mapPeople (WhereParam("p.netid=@NetId", {NetId=netid}))) connStr
        return! takeExactlyOne people
    }

    let tryQueryPersonByNetId connStr netId = async {
        let! people = fetchAll<Person> (mapPeople(WhereParam("netid = @NetId", {NetId=netId}))) connStr
        let result = 
            match people with
            | Ok result ->
                match result |> Seq.tryHead with
                | Some(p) -> Ok (netId, Some(p.Id))
                | None -> Ok (netId, None)
            | Error(msgs) -> Error(msgs)
        return result
    }

    let insertPerson = insert<Person> mapPerson

    let queryPersonMemberships connStr id = pipeline {
        let! members = fetchAll (mapUnitMembers(WhereId("p.id", id))) connStr
        return! members |> stripNotes |> collectMemberTools
    }

    let updatePerson connStr (person:PersonRequest) = pipeline {
        let sql = """
            UPDATE people
            SET location=@Location,
                responsibilities=@Responsibilities,
                expertise=@Expertise,
                photo_url=@PhotoUrl
            WHERE id=@Id;"""
        do! execute connStr sql person
        return! fetchOne<Person> mapPerson connStr person.Id
    }

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

    let queryTools = fetchAll<Tool> (map queryToolsSql Unfiltered)

    let queryToolPermissions = fetchAll<ToolPermission> (map queryToolPermissionsSql Unfiltered)

    let queryTool = fetchOne<Tool> mapTool

    // *********************
    // Member Tools
    // *********************

    let queryMemberToolsSql = """
        SELECT * from unit_member_tools umt"""

    let mapMemberTools filter (cn:Cn) = 
        parseQueryAndParam queryMemberToolsSql filter
        |> cn.QueryAsync<MemberTool>

    let mapMemberTool id = mapMemberTools (WhereId("umt.id", id))

    let queryMemberTools = fetchAll<MemberTool> (mapMemberTools Unfiltered)

    let queryMemberTool = fetchOne<MemberTool> mapMemberTool

    let insertMemberTool = insert<MemberTool> mapMemberTool

    let updateMemberTool = update<MemberTool> mapMemberTool

    let deleteMemberTool = deleteById<MemberTool>

    let isServiceAdminSql = """
    SELECT EXISTS (
        SELECT id 
        FROM people
        WHERE netid = @NetId
            AND is_service_admin = TRUE
        LIMIT 1
    )"""

    type UaaPublicKey = {alg:string; value:string} 
    let uaaPublicKey (url:string) = pipeline {
        let msg = new HttpRequestMessage(HttpMethod.Get, url)
        let! response = sendAsync<UaaPublicKey> msg
        return response.value
    }

    let isServiceAdmin connStr netid =
        let query (cn:Cn) = cn.QuerySingleAsync<bool>(isServiceAdminSql, { NetId=netid })
        fetch query connStr

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
	-- select 'true' if this person has the specified permissions
	-- in this unit or any parent unit and 'false' otherwise.
    SELECT EXISTS (
		SELECT pa.id, um.role, pe.netid
		FROM parentage pa
		JOIN unit_members um on pa.id = um.unit_id
		JOIN people pe on pe.id = um.person_id 
		WHERE
			pe.netid = @NetId 
			AND um.permissions = ANY(@UnitPermissions)
    ) """



    type AuthParams = {UnitId: Id; NetId: NetId; UnitPermissions: int[]}

    let hasCascadedUnitPerms permissions connStr netid unitId  =
        let param = 
          { UnitId=unitId
            NetId=netid 
            UnitPermissions= permissions |> Seq.map int |> Seq.toArray }
        let query (cn:Cn) = cn.QuerySingleAsync<bool>(hasCascadedUnitPermsSql, param)
        fetch query connStr

    let isServiceAdminOrHasUnitPermissions permissions connStr netid unitId = pipeline {
        let! is = isServiceAdmin connStr netid    
        if is
        then return true
        else return! hasCascadedUnitPerms permissions connStr netid unitId
    }

    let isUnitManager = 
        isServiceAdminOrHasUnitPermissions [ UnitPermissions.Owner; UnitPermissions.ManageMembers ]

    let isUnitToolManager = 
        isServiceAdminOrHasUnitPermissions [ UnitPermissions.Owner; UnitPermissions.ManageTools ]

    let canModifyPersonSql = """
        SELECT
        ( -- The requestor is updating their own record 
          SELECT netid = @NetId -- requestor netid
          FROM people p 
          WHERE id = @Id -- updated person id
        ) 
        OR
        ( -- The requestor is updating the record of a person
          -- who is in a unit that the requestor owns/manages.
          -- We can figure this by count the common unit ids that the
          --  a) person is a member of, and
          --  b) requestor owns/manages.
          SELECT COUNT(id) > 0 FROM 
          (
        	-- All the units the requested person is in
        	(SELECT um.unit_id as id
        	 FROM unit_members um
        	 JOIN people p on p.id = um.person_id
        	 WHERE p.id = @Id) -- updated person id
        	INTERSECT -- keep only the common elements
        	-- All the units for which the requestor has owner/manager permissions
        	(SELECT um.unit_id as id
        	 FROM unit_members um
        	 JOIN people p on p.id = um.person_id
        	 WHERE 
        		um.permissions IN (1,3) -- unit owner/manager
        		AND p.netid = @NetId) -- requestor netid 
          ) intersection
        )"""

    type PersonParams = {Id:Id; NetId:NetId}
    let canModifyPerson connStr netid personId = pipeline {
        let param = {Id=personId; NetId=netid}
        let canModifyPersonQuery (cn:Cn) = cn.QuerySingleAsync<bool>(canModifyPersonSql, param) 

        let! result = isServiceAdmin connStr netid
        if result
        then return true
        else return! fetch canModifyPersonQuery connStr
    }


    // *********************
    // Legacy
    // *********************

    // LSPs are any member of a unit that has a support relationship with
    // one or more departmens. "LA" = "local administrator" = unit leader.
    // Exclude "related" people from result.
    let queryLspListSql = """
        SELECT p.netid, MAX(um.Role) in (3,4) as is_service_admin 
        FROM people p
        JOIN unit_members um ON um.person_id = p.id
        WHERE um.unit_id IN (SELECT sr.unit_id from support_relationships sr)
            AND um.role <> 1
        GROUP BY p.netid
        ORDER BY p.netid"""

    let mapLspList filter (cn:Cn) = 
        parseQueryAndParam queryLspListSql filter
        |> cn.QueryAsync<Person>

    let toLspInfo (p:Person) = {NetworkID=p.NetId; IsLA=p.IsServiceAdmin}

    let queryLspInfo connStr = pipeline {
        let! lsps = fetchAll (mapLspList Unfiltered) connStr
        let infos = lsps |> Seq.map toLspInfo |> Seq.toArray
        return { LspInfos = infos }
    }

    let queryLspDepartmentsSql = """
        SELECT d.name
        FROM departments d
        JOIN support_relationships sr ON sr.department_id = d.id
        JOIN unit_members um on um.unit_id = sr.unit_id
        JOIN people p ON um.person_id = p.id
        WHERE p.netid ILIKE @Query
        and um.role <> 1"""

    let mapLspDepartments netid (cn:Cn) = 
        cn.QueryAsync<string>(queryLspDepartmentsSql, {Query=netid})


    let queryLspDepartments connStr netid = pipeline {
        let! depts = fetchAll<string> (mapLspDepartments netid) connStr
        return { NetworkID=netid; DeptCodeList={ Values=Seq.toArray depts } }
    }

    let queryDepartmentLspsSql = """
        SELECT p.name, p.netid, p.campus_phone, p.campus_email, MAX(um.role) in (3,4) as is_service_admin, u.email as notes
        FROM people p
        JOIN unit_members um on um.person_id = p.id
        JOIN support_relationships sr on sr.unit_id = um.unit_id
        JOIN departments d on sr.department_id = d.id
        JOIN units u on um.unit_id = u.id
        WHERE d.name ILIKE @Query 
            AND um.role <> 1
        GROUP BY p.name, p.netid, p.campus_phone, p.campus_email, u.email"""

    let mapDepartmentLsps department (cn:Cn) = 
        cn.QueryAsync<Person>(queryDepartmentLspsSql, {Query=department})

    let toLspContact (p:Person) =
      { NetworkID=p.NetId
        FullName=p.Name 
        IsLSPAdmin=p.IsServiceAdmin // we override IsServiceAdmin in the query
        Email=p.CampusEmail 
        PreferredEmail= if isEmpty p.Notes then p.CampusEmail else p.Notes // we override Notes in the query 
        GroupInternalEmail= ""
        Phone=p.CampusPhone }

    let queryDepartmentLsps connStr department = pipeline {
        let! lsps = fetchAll (mapDepartmentLsps department) connStr
        let contacts = lsps |> Seq.map toLspContact |> Seq.toArray
        return { LspContacts = contacts }
    }

    let People(connStr) = {
        TryGetId = tryQueryPersonByNetId connStr
        GetAll = queryPeople connStr
        GetAllWithHr = queryPeopleWithHr connStr
        GetHr = queryHrPerson connStr
        GetById = queryPersonById connStr
        GetByNetId = queryPersonByNetId connStr
        Create = insertPerson connStr
        GetMemberships = queryPersonMemberships connStr
        Update = updatePerson connStr
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
        GetSupportedBuildings = queryUnitSupportedBuildings connStr
        GetDescendantOfParent = queryUnitGetDescendantOfParent connStr
    }

    let Departments (connStr) = {
        GetAll = queryDepartments connStr
        Get = queryDepartment connStr
        GetMemberUnits = queryDeptMemberUnits connStr
        GetSupportingUnits = queryDeptSupportingUnits connStr
    }

    let Buildings (connStr) = {
        GetAll = queryBuildings connStr
        Get = queryBuilding connStr
        GetSupportingUnits = queryBuildingSupportingUnits connStr
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
    }

    let ToolsRepository (connStr) : ToolsRepository = {
        GetAll = fun () -> queryTools connStr
        GetAllPermissions = fun () -> queryToolPermissions connStr
        Get = queryTool connStr
    }

    let SupportRelationshipsRepository(connStr) : SupportRelationshipRepository = {
        GetAll = fun () -> querySupportRelationships connStr 
        Get = querySupportRelationship connStr
        Create = insertSupportRelationship connStr
        Update = updateSupportRelationship connStr
        Delete = deleteSupportRelationship connStr
    }

    let BuildingRelationshipsRepository(connStr) : BuildingRelationshipRepository = {
        GetAll = fun () -> queryBuildingRelationships connStr 
        Get = queryBuildingRelationship connStr
        Create = insertBuildingRelationship connStr
        Update = updateBuildingRelationship connStr
        Delete = deleteBuildingRelationship connStr
    }

    let AuthorizationRepository(connStr) = {
        UaaPublicKey = uaaPublicKey
        IsServiceAdmin = isServiceAdmin connStr
        IsUnitManager = isUnitManager connStr
        IsUnitToolManager = isUnitToolManager connStr
        CanModifyPerson = canModifyPerson connStr
    }

    let LegacyRepository(connStr) = {
        GetLspList = fun () -> queryLspInfo connStr
        GetLspDepartments = queryLspDepartments connStr
        GetDepartmentLsps = queryDepartmentLsps connStr
    }

    let Repository(connStr) = {
        People = People(connStr)
        Departments = Departments(connStr)
        Buildings = Buildings(connStr)
        Units = Units(connStr)
        Memberships = Memberships(connStr)
        MemberTools = MemberToolsRepository(connStr)
        Tools = ToolsRepository(connStr)
        SupportRelationships = SupportRelationshipsRepository(connStr)
        BuildingRelationships = BuildingRelationshipsRepository(connStr)
        Authorization = AuthorizationRepository(connStr)
        Legacy = LegacyRepository(connStr)
    }
