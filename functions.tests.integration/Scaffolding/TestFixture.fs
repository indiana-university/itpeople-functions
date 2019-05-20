// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Integration 

module TestFixture =

    open Xunit
    open Xunit.Abstractions
    open PostgresContainer
    open Database.Fakes
    open System
    open System.IO
    open TestHost
    

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

    let functionServerScriptPath = Path.Combine(Directory.GetCurrentDirectory(), "../../../../functions/bin/Debug/netcoreapp2.1")
    let functionServerPort = 9091
    let functionServerUrl = sprintf "http://localhost:%d" functionServerPort

    type HttpTestBase (output: ITestOutputHelper) = 
        inherit DatabaseIntegrationTestBase()
        let mutable functionsServer = None

        do
            // These config settings are needed for the tests
            Environment.SetEnvironmentVariable("UseFakeData", "false")
            Environment.SetEnvironmentVariable("OAuthPublicKey",Core.Fakes.fakePublicKey)
            Environment.SetEnvironmentVariable("DbConnectionString", testConnectionString)
            // These config settings aren't needed for the tests, but the config expects them
            Environment.SetEnvironmentVariable("OAuthClientId","na")
            Environment.SetEnvironmentVariable("OAuthClientSecret","na")
            Environment.SetEnvironmentVariable("OAuthTokenUrl","na")
            Environment.SetEnvironmentVariable("OAuthRedirectUrl","na")
            Environment.SetEnvironmentVariable("SharedSecret","na")

            // "---> Starting functions host..." |> Console.WriteLine
            functionsServer <- startTestServer functionServerPort functionServerScriptPath output |> Async.RunSynchronously

        interface IDisposable with
            member this.Dispose() = 
                // "---> Stopping functions host..." |> Console.WriteLine
                stopTestServer functionsServer
         

