// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace ImportOrgData

open Chessie.ErrorHandling
open Dapper
open Npgsql
open System
open Types 

module Database =

    type NameParams = 
      { Name: string }

    type UnitParams = 
      { Name:string
        Url:string
        Description:string }

    type UnitRelationParams = 
      { ChildId: int
        ParentId: int }

    type UserRelationParams = 
      { UnitId: int
        NetId: string
        Title: string
        Role: int
        Percentage: int }

    let (|EmptySeq|_|) a = if Seq.isEmpty a then Some () else None

    let getValueForRole role = 
        match role with
        | "leadership" -> 4
        | "sublead" -> 3
        | "member" -> 2
        | _ -> 1

    let sqlConnection connectionString =
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores <- true
        new NpgsqlConnection(connectionString)

    let private dropExistingUnits (dbConnectionString:string) (uits:Unit) =
        let deleteUnitSql = """
            DELETE FROM unit_members
            WHERE unit_id IN (SELECT id FROM units WHERE name = @Name);
            DELETE FROM unit_relations
            WHERE child_id IN (SELECT id FROM units WHERE name = @Name)
            OR parent_id IN (SELECT id FROM units WHERE name = @Name);
            DELETE FROM units WHERE id IN (SELECT id FROM units WHERE name = @Name);"""


        use cn = sqlConnection dbConnectionString
        let rec deleteUnit (unit:Unit) =
            // delete children first
            match unit.Children with
            | EmptySeq -> ()
            | _ -> unit.Children |> Seq.iter deleteUnit
            // then delete this unit
            printfn "  Removing %s..." unit.Name
            cn.Execute(deleteUnitSql, {Name=unit.Name}) |> ignore

        printfn "\nRemoving existing UITS units and memberships..."
        deleteUnit uits

        uits

    let private addUnitToDb connStr (unit:Unit) = 
        let sql = """
            INSERT INTO units (name, url, description)
            VALUES (@Name, @Url, @Description) RETURNING id;"""
        let parameters = {Name = unit.Name; Url=unit.Url; Description=""}
        use cn = sqlConnection connStr
        cn.QueryFirstOrDefault<int>(sql, parameters)

    let private addUnitRelation connStr parentId childId = 
        let sql = """
            INSERT INTO unit_relations (child_id, parent_id)
            VALUES (@ChildId, @ParentId);"""
        let parameters = {ChildId = childId; ParentId = parentId}
        use cn = sqlConnection connStr
        cn.Execute(sql, parameters) |> ignore

    let private addMemberRelation connStr unitId netid title role percentage = 
        let sql = """
            INSERT INTO unit_members (unit_id, person_id, title, role, percentage, tools)
            VALUES (@UnitId, (SELECT id FROM people WHERE netid = @NetId), @Title, @Role, @Percentage, 0);
        """
        let parameters = { UnitId = unitId; NetId = netid; Title = title; Role = role; Percentage=percentage }
        use cn = sqlConnection connStr
        try 
            cn.Execute(sql, parameters) |> ignore
            printfn ""
        with
        | exn -> 
            printfn " [ ERR: netid not found ]"

    let private addUnits (connectionString:string) (uits:Unit) =
        
        let rec addUnit (parentId:int option) (unit:Unit)  =
            printfn "\nAdding unit '%s'..." unit.Name
            // add unit
            let unitId = addUnitToDb connectionString unit
            // add relationship to parent
            match parentId with
            | Some(id) -> 
                printfn "   Adding relation to parent..."
                addUnitRelation connectionString id unitId
            | None -> ()
            // add members
            unit.Members
            |> Seq.iter (fun m -> 
                printf "   Adding member %s..." m.Name
                addMemberRelation connectionString unitId m.Name m.Title (getValueForRole m.Role) m.Percentage)
            // recurse to children
            match unit.Children with
            | EmptySeq -> ()
            | _ -> unit.Children |> Seq.iter (addUnit (Some(unitId)))

        addUnit None uits

        uits

    let updateDatabase (connectionString:string) (uits:Unit) =
        uits
        |> dropExistingUnits connectionString
        |> addUnits connectionString