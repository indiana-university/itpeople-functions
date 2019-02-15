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
    open Database.Fakes
    
    type XUnitOutput(output: ITestOutputHelper)=
        let output = output
        interface IOutput with  
            member this.WriteLine(message: string)=
                message |> output.WriteLine
    
    let functionServerScriptPath = "../../../../functions/bin/Debug/netcoreapp2.1"
    let functionServerPort = 9091

    let stateServerScriptPath = "../../../../functions.tests.stateserver/bin/Debug/netcoreapp2.1"
    let stateServerPort = 9092

    let verifyPact output = 
        let stateServerUrl = sprintf "http://localhost:%d" stateServerPort
        let functionUrl = sprintf "http://localhost:%d" functionServerPort
        let outputters = ResizeArray<IOutput> [XUnitOutput(output) :> IOutput]
        let verifier = PactVerifierConfig(Outputters=outputters, Verbose=true) |> PactVerifier
        verifier
            .ProviderState(stateServerUrl)
            .ServiceProvider("API", functionUrl)
            .HonoursPactWith("Client")
            .PactUri("https://raw.githubusercontent.com/indiana-university/itpeople-app/develop/contracts/itpeople-app-itpeople-functions.json")
            .Verify()

    type Pact(output: ITestOutputHelper)=
        inherit DatabaseIntegrationTestBase()

        let output = output

        [<Fact>]
        member __.``Test Contracts`` () = async {
            let mutable functionServer = None
            let mutable stateServer = None

            // System.Reflection.Assembly.GetEntryAssembly().Location
            // |> System.IO.Path.GetDirectoryName
            // |> printfn "Executing in: %s"

            try            
                // These config settings are needed for the tests
                Environment.SetEnvironmentVariable("UseFakeData", "false")
                Environment.SetEnvironmentVariable("JwtSecret","jwt signing secret")
                Environment.SetEnvironmentVariable("DbConnectionString", testConnectionString)
                // These config settings aren't needed for the tests, but the config expects them
                Environment.SetEnvironmentVariable("OAuthClientId","na")
                Environment.SetEnvironmentVariable("OAuthClientSecret","na")
                Environment.SetEnvironmentVariable("OAuthTokenUrl","na")
                Environment.SetEnvironmentVariable("OAuthRedirectUrl","na")

                "---> Starting functions host..." |> Console.WriteLine
                let! functionsServer = startTestServer functionServerPort functionServerScriptPath output
                "---> Starting state server host..." |> Console.WriteLine
                let! stateServer = startTestServer stateServerPort stateServerScriptPath output
                "---> Verifying contract..." |> Console.WriteLine
                verifyPact output
            finally
                stopTestServer functionServer
                stopTestServer stateServer
        }