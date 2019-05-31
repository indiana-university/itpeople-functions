// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Database

module Fakes =

    open Npgsql
    open Dapper
    
    open Core.Types    
    open Core.Fakes
    open Migration

    let testConnectionString = "User ID=root;Host=localhost;Port=5432;Database=circle_test;Pooling=true;"

    let resetDatabaseWithTestFakes () = 
        
        Command.init() 

        use db = new NpgsqlConnection(testConnectionString)
        
        /// Clear the database and migrate it to the latest schema
        let migrator = db |> migrator
        migrator.Load()
        // Migrate back to 0
        migrator.MigrateTo(int64 0)
        // Vacuum: garbage-collect and optionally analyze a database
        // https://www.postgresql.org/docs/9.2/sql-vacuum.html
        db.Execute("VACUUM") |> ignore
        // Migrate to the latest schema
        migrator.MigrateToLatest()
        
        // departments
        db.Insert<Department>(parksDept) |> ignore 
        // units
        db.Insert<Unit>(cityOfPawnee) |> ignore // db.Insert<Unit>(cityOfPawnee) |> ignore
        db.Insert<Unit>(parksAndRec) |> ignore
        db.Insert<Unit>(fourthFloor) |> ignore
        // people
        db.Insert<Person>(swanson) |> ignore
        db.Insert<Person>(knope) |> ignore
        db.Insert<Person>(wyatt) |> ignore
        db.Insert<Person>(admin) |> ignore
        db.Insert<HrPerson>(donnaHr) |> ignore
        // unit membership
        db.Insert<UnitMember>(swansonMembership) |> ignore
        db.Insert<UnitMember>(knopeMembership) |> ignore
        db.Insert<UnitMember>(parksAndRecVacancy) |> ignore
        db.Insert<UnitMember>(wyattMembership) |> ignore
        // support relationship
        db.Insert<SupportRelationship>(supportRelationship) |> ignore
        // tools 
        db.Insert<Tool>(tool) |> ignore
        // member tools 
        db.Insert<MemberTool>({Id=1; MembershipId=wyattMembership.Id; ToolId=tool.Id}) |> ignore
        
        ()
