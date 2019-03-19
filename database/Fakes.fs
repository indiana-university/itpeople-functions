// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Database

module Fakes =

    open Npgsql
    open Dapper
    
    open Functions.Types    
    open Functions.Fakes
    open Migration

    let testConnectionString = "User ID=root;Host=localhost;Port=5432;Database=circle_test;Pooling=true;"

    let resetDatabaseWithTestFakes () = 
        
        Functions.Database.init() 

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
        // unit membership
        db.Insert<UnitMember>(swansonMembership) |> ignore
        db.Insert<UnitMember>(knopeMembership) |> ignore
        db.Insert<UnitMember>(parksAndRecVacancy) |> ignore
        db.Insert<UnitMember>(wyattMembership) |> ignore
        // support relationship
        db.Insert<SupportRelationship>(supportRelationship) |> ignore
        
        ()
