// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Database

module Fakes =

    open Npgsql
    open System.Reflection
    open Dapper
    
    open Functions.Types    
    open Functions.Fakes
    open Migration

    let testConnectionString = "User ID=root;Host=localhost;Port=5432;Database=circle_test;Pooling=true;"

    /// Clear the database and migrate it to the latest schema
    let clearAndMigrate () = 
        use db = new NpgsqlConnection(testConnectionString)
        let migrator = db |> migrator
        migrator.Load()
        migrator.MigrateTo(int64 0)
        migrator.MigrateToLatest()


    let resetDatabaseWithTestFakes () = 
        
        let mutable parksAndRecId = 0
        let mutable cityId = 0
        let mutable fourthFloorId = 0
        let mutable parksDeptId = 0
        let mutable swansonId = 0
        let mutable knopeId = 0
        let mutable sebastianId = 0

        clearAndMigrate ()
        use db = new NpgsqlConnection(testConnectionString)
        // units
        parksAndRecId <- db.Insert<Unit>(parksAndRec).GetValueOrDefault()
        cityId <- db.Insert<Unit>(cityOfPawnee).GetValueOrDefault()
        fourthFloorId <- db.Insert<Unit>(fourthFloor).GetValueOrDefault()
        // departments
        parksDeptId <- db.Insert<Department>(parksDept).GetValueOrDefault()
        // people
        swansonId <- db.Insert<Person>({swanson with HrDepartmentId=parksDeptId}).GetValueOrDefault()
        knopeId <- db.Insert<Person>({knope with HrDepartmentId=parksDeptId}).GetValueOrDefault()
        sebastianId <- db.Insert<Person>({sebastian with HrDepartmentId=parksDeptId}).GetValueOrDefault()
        // unit membership

        let _ = db.Insert<UnitMember>({Id=0; UnitId=parksAndRecId; PersonId=swansonId; Role=Role.Leader; Permissions=Permissions.Owner; Title="Director"; Tools=Tools.AccountMgt; Percentage=100; Person=Some(swanson); Unit=parksAndRec})
        let _ = db.Insert<UnitMember>({Id=0; UnitId=parksAndRecId; PersonId=knopeId; Role=Role.Sublead; Permissions=Permissions.Viewer; Title="Deputy Director"; Tools=Tools.None; Percentage=100; Person=Some(knope); Unit=parksAndRec})
        let _ = db.Insert<UnitMember>({Id=0; UnitId=parksAndRecId; PersonId=sebastianId; Role=Role.Member; Permissions=Permissions.Viewer; Title="Mascot"; Tools=Tools.None; Percentage=100; Person=Some(sebastian); Unit=parksAndRec})
        // unit relationsips
        let _ = db.Insert<UnitRelation>({ChildUnitId=parksAndRecId; ParentUnitId=cityId})
        let _ = db.Insert<UnitRelation>({ChildUnitId=fourthFloorId; ParentUnitId=parksAndRecId})
        // support relationship
        let _ = db.Insert<SupportRelationship>({DepartmentId=parksDeptId; UnitId=parksAndRecId})
        ()