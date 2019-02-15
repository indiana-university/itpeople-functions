// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Integration 

module TestFixture =

    open Xunit
    open Xunit.Abstractions
    open Xunit.Sdk
    open Chessie.ErrorHandling
    open Dapper
    open PostgresContainer
    open Functions.Database
    open Functions.Fakes
    open Functions.Types
    open Database.Fakes

    // Generally:
    // 1. Go fetch the postgres docker image.
    // 2. Create and start a postgres container.
    // 3. Migrate the DB to the latest version
    // 4. Within a test, populate and exercise DB code
    // 5. Stop and remove the container.
    type IntegrationFixture (output: IMessageSink)=
        // A flag to determine whether the Postgres server container was 
        // started prior to running the tests. This will true for tests run 
        // in Circle CI, and (usually) false for tests running locally.
        do
            let log (msg:string) = msg |> System.Console.WriteLine
            ensureDatabaseServerStarted log |> Async.RunSynchronously

    // This collection provides a common interface for all 
    // integration tests so that the postgres server only gets 
    // started/stopped once.
    [<CollectionDefinition("Integration collection")>]
    type IntegrationCollection() =
        interface ICollectionFixture<IntegrationFixture>

    // A base class for all integration tests that clears the
    // database and migrates it to the latest version.
    [<Collection("Integration collection")>]
    type DatabaseIntegrationTestBase() =
        do resetDatabaseWithTestFakes ()

