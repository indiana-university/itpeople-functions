// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace ImportOrgData

open Dapper
open Npgsql
open System
open Types 

module Database =

    type NameParams = 
      { Name: string }

    type DeptParams = 
      { Name: string
        Description: string }

    type UnitParams = 
      { Name:string
        Url:string
        Description:string }

    type UnitWithParentParams = 
      { Name:string
        Url:string
        Description:string
        ParentId:int }

    type UnitRelationParams = 
      { ChildId: int
        ParentId: int }

    type UnitDepartmentParams = 
      { UnitName: string
        DepartmentName: string }

    type UserRelationParams = 
      { UnitId: int
        NetId: string
        Title: string
        Role: int
        Percentage: int }

    type PersonParams = {
        NetId: string;
        Name: string;
        Position: string;
        Campus: string;
        Phone: string;
        Email: string;
        HrDepartment: string
    }

    let (|EmptySeq|_|) a = if Seq.isEmpty a then Some () else None

    let getValueForRole role = 
        match role with
        | "leadership" -> 4
        | "Admin" -> 4
        | "sublead" -> 3
        | "Coadmin" -> 3
        | "member" -> 2
        | "IT Pro" -> 2
        | _ -> 1

    let sqlConnection connectionString =
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores <- true
        new NpgsqlConnection(connectionString)

    let private addUnitToDb connStr (unit:Unit) (parentId:int option) = 
        let sql = """
            INSERT INTO units (name, url, description)
            VALUES (@Name, @Url, @Description) RETURNING id;"""
        let sqlParentId = """
            INSERT INTO units (name, url, description, parent_id)
            VALUES (@Name, @Url, @Description, @ParentId) RETURNING id;"""
        use cn = sqlConnection connStr
        match parentId with
        | Some(id) -> 
            let parameters = {Name = unit.Name; Url=unit.Url; Description=""; ParentId=id}
            cn.QueryFirstOrDefault<int>(sqlParentId, parameters)
        | None ->
            let parameters = {Name = unit.Name; Url=unit.Url; Description=""}
            cn.QueryFirstOrDefault<int>(sql, parameters)

    let indent level = String.replicate level "  "

    let private addMemberRelation connStr level unitId netid title role percentage = 
        let sql = """
            INSERT INTO unit_members (unit_id, person_id, title, role, percentage, tools)
            VALUES (@UnitId, (SELECT id FROM people WHERE netid = @NetId), @Title, @Role, @Percentage, 0);
        """
        let parameters = { UnitId = unitId; NetId = netid; Title = title; Role = role; Percentage=percentage }
        use cn = sqlConnection connStr
        try 
            cn.Execute(sql, parameters) |> ignore
        with
        | exn -> printfn "%s  ERR: Failed to add member '%s': %s" (indent level) netid exn.Message

    let private addUnits (connectionString:string) (uits:Unit) =
        
        let rec addUnit (level:int) (parentId:int option) (parentName:string option) (unit:Unit)  =
            printfn "%sAdding unit '%s'" (indent level) unit.Name
            // add unit
            let unitId = addUnitToDb connectionString unit parentId
            // add members
            unit.Members
            |> Seq.iter (fun m -> 
                addMemberRelation connectionString level unitId m.Name m.Title (getValueForRole m.Role) m.Percentage)
            // recurse to children
            match unit.Children with
            | EmptySeq -> ()
            | _ -> unit.Children |> Seq.iter (addUnit (level+1) (Some(unitId)) (Some(unit.Name)))

        addUnit 0 None None uits

    let private addUnitDepartmentRelationships connectionString (unitDept:UnitDept.Row) = 
        let sql = """
            INSERT INTO support_relationships (unit_id, department_id)
            VALUES (
                (SELECT id FROM units WHERE name = @UnitName), 
                (SELECT id FROM departments WHERE name = @DepartmentName));"""
        printfn "Adding support relationship for %s and '%s'" unitDept.Dept unitDept.``Supporting Unit``
        let parameters = 
          { UnitName = unitDept.``Supporting Unit``
            DepartmentName = unitDept.Dept }
        try
            use cn = sqlConnection connectionString
            cn.Execute(sql, parameters) |> ignore
        with exn -> printfn "  Failed to add relationship: %s" exn.Message


    let ci = (new System.Globalization.CultureInfo("en-US",false)).TextInfo

    let private addOrUpdatePerson connectionString (person:Person.Row) = 
        let sql = """
            INSERT INTO people (hash, netid, name, position, location, campus, campus_phone, campus_email, expertise, notes, photo_url, responsibilities, tools, department_id)
            VALUES ('', @NetId, @Name, @Position, '', @Campus, @Phone, @Email, '', '', '', 0, 0, (SELECT id FROM departments WHERE name = @HrDepartment))
            ON CONFLICT (netid) DO UPDATE 
              SET name=@Name, position=@Position, campus=@Campus, campus_phone=@Phone, campus_email=@Email, department_id=(SELECT id FROM departments WHERE name = @HrDepartment)"""
        let netid = ci.ToLower(person.``User ID (Network ID)``)
        let parameters = 
          { NetId=netid
            Name=person.``Preferred Full Name``.Replace(",", ", ")
            Position=person.``Position Title Description``
            Campus=person.``Location Code``
            Phone=person.``Campus Phone``
            Email=person.``Campus Email Address``
            HrDepartment=person.``Department ID`` }
        printfn "Adding person %s..." netid
        use cn = sqlConnection connectionString
        cn.Execute(sql, parameters) |> ignore

    let private addOrUpdateDepartment connectionString (dept:Dept.Row) = 
        let sql = """
            INSERT INTO departments (name, description)
            VALUES (@Name, @Description)
            ON CONFLICT (name) DO UPDATE 
              SET description=@Description"""
        let parameters = 
          { Name=dept.Name
            Description=ci.ToTitleCase(ci.ToLower(dept.Description)) }
        printfn "Adding department %s..." dept.Name
        use cn = sqlConnection connectionString
        cn.Execute(sql, parameters) |> ignore

    let clearDatabase connectionString =
        let sql = """
            DELETE from support_relationships;
            DELETE from unit_members;
            DELETE from people;
            DELETE from units;
            DELETE from departments;"""
        printfn "Clearing records..."
        use cn = sqlConnection connectionString
        cn.Execute(sql) |> ignore

    let addOrUpdateUnits connectionString units =
        units |> Seq.iter (addUnits connectionString)
 
    let addOrUpdateUnitDepts connectionString unitDepts =
        unitDepts |> Seq.iter (addUnitDepartmentRelationships connectionString)

    let addOrUpdateDepartments connectionString (people:Dept.Row seq) =
        people 
        |> Seq.sortBy (fun p -> (p.Name))
        |> Seq.iter (addOrUpdateDepartment connectionString)

    let addOrUpdatePeople connectionString (people:Person.Row seq) =
        people 
        |> Seq.sortBy (fun p -> (p.``User ID (Network ID)``))
        |> Seq.iter (addOrUpdatePerson connectionString)