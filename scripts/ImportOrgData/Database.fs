namespace ImportOrgData

open Chessie.ErrorHandling
open Dapper
open Npgsql
open System
open Types 

module Database =

    type NameParam = { Name:string }

    type UnitRelation = {
        ChildId: int
        ParentId: int
    }

    type UserRelation = {
        UnitId: int
        NetId: string
        Title: string
        Role: int
    }

    let (|EmptySeq|_|) a = if Seq.isEmpty a then Some () else None

    let sqlConnection connectionString =
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores <- true
        new NpgsqlConnection(connectionString)


    let private dropExistingUnits (dbConnectionString:string) (uits:Unit) =
        let deleteUnit = """
            DELETE FROM unit_members um
            WHERE um.unit_id = (SELECT id FROM units WHERE name = @Name);
            DELETE FROM unit_relations ur
            WHERE ur.child_id = (SELECT id FROM units WHERE name = @Name)
            OR ur.parent_id = (SELECT id FROM units WHERE name = @Name);
            DELETE FROM units WHERE name = @Name;"""

        let rec unitNames (unit:Unit) =
            match unit.Children with
            | EmptySeq -> [ unit.Name ] |> List.toSeq
            | _ -> unit.Children |> Seq.collect unitNames

        use cn = sqlConnection dbConnectionString
        uits
        |> unitNames
        |> Seq.map (fun u -> {Name=u})
        |> fun names -> cn.Execute(deleteUnit, names)
        |> ignore

        uits

    let private addUnitToDb connStr name= 
        let sql = """
            INSERT INTO units (name, url, description)
            VALUES (@Name, @Url, @Description) RETURNING id;"""
        use cn = sqlConnection connStr
        cn.QueryFirstOrDefault<int>(sql, {Name = name})

    let private addUnitRelation connStr unitId childId = 
        let sql = """
            INSERT INTO unit_relations (child_id, parent_id)
            VALUES (@ChildId, @ParentId);"""
        use cn = sqlConnection connStr
        cn.Execute(sql, {ChildId = childId; ParentId = unitId}) |> ignore

    let private addMemberRelation connStr unitId netid title role = 
        let sql = """
            INSERT INTO unit_members (unit_id, person_id, title, role, percentage, tools)
            VALUES (@UnitId, (SELECT id FROM people WHERE netid = @NetId), @Title, @Role, 100, 0);
        """
        use cn = sqlConnection connStr
        let paramaters = { UnitId = unitId; NetId = netid; Title = title; Role = role }
        cn.Execute(sql, paramaters) |> ignore

    let updateDatabase (connectionString:string) (unit:Unit) =
        ()
        
    
    

