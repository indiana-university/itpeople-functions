// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Integration

module ContractTests =
    open PactNet
    open PactNet.Infrastructure.Outputters
    open Xunit.Abstractions
    open Xunit
    open TestFixture
    open TestHost
    open System
    open PostgresContainer
    open Dapper
    open Npgsql
    open Functions.Types
    open Functions.Fakes
    open Functions.Database
    open Migrations.Program
    
    type XUnitOutput(output: ITestOutputHelper)=
        let output = output
        interface IOutput with  
            member this.WriteLine(message: string)=
                message |> output.WriteLine
    
    let verifyPact functionPort output = 
        let functionUrl = sprintf "http://localhost:%d" functionPort
        let outputters = ResizeArray<IOutput> [XUnitOutput(output) :> IOutput]
        let verifier = PactVerifierConfig(Outputters=outputters, Verbose=true) |> PactVerifier
        verifier
            .ServiceProvider("API", functionUrl)
            .HonoursPactWith("Client")
            .PactUri("https://raw.githubusercontent.com/indiana-university/itpeople-app/develop/contracts/itpeople-app-itpeople-functions.json")
            .Verify()

    type Pact(output: ITestOutputHelper)=
        inherit DatabaseIntegrationTestBase()
        let output = output

        [<Fact>]
        member __.``Test Contracts`` () = async {
            let functionScriptPath = "../../../../functions/bin/Debug/netcoreapp2.1"
            let functionServerPort = 9091
            let mutable functionServer = None

            try            
                // These config settings are needed for the tests
                Environment.SetEnvironmentVariable("JwtSecret","jwt signing secret")
                Environment.SetEnvironmentVariable("DbConnectionString",connectionString)
                // These config settings aren't needed for the tests, but the config expects them
                Environment.SetEnvironmentVariable("OAuthClientId","na")
                Environment.SetEnvironmentVariable("OAuthClientSecret","na")
                Environment.SetEnvironmentVariable("OAuthTokenUrl","na")
                Environment.SetEnvironmentVariable("OAuthRedirectUrl","na")

                "---> Starting functions hst..." |> output.WriteLine
                let! functionsServer = startTestServer functionServerPort functionScriptPath output
                "---> Verifying contract..." |> output.WriteLine
                verifyPact functionServerPort output
            finally
                stopTestServer functionServer
        }