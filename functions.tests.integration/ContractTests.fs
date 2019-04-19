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
    open Functions.Fakes
    open Database.Fakes
    
    type XUnitOutput(output: ITestOutputHelper)=
        let output = output
        interface IOutput with  
            member this.WriteLine(message: string)=
                message |> output.WriteLine
    


    type PactTests(output: ITestOutputHelper)=
        inherit HttpTestBase(output)

        let stateServerScriptPath = "../../../../functions.tests.stateserver/bin/Debug/netcoreapp2.1"
        let stateServerPort = 9092

        let verifyPact output = 
            let stateServerUrl = sprintf "http://localhost:%d/state" stateServerPort
            let outputters = ResizeArray<IOutput> [XUnitOutput(output) :> IOutput]
            let verifier = PactVerifierConfig(Outputters=outputters, Verbose=false, PublishVerificationResults=false) |> PactVerifier
            verifier
                .ProviderState(stateServerUrl)
                .ServiceProvider("API", functionServerUrl)
                .HonoursPactWith("Client")
                .PactUri("https://raw.githubusercontent.com/indiana-university/itpeople-app/feature/tool-permissions/contracts/itpeople-app-itpeople-functions.json")
                .Verify()

        [<Fact>]
        member __.``Verify Contracts`` () = async {
            let mutable stateServer = None
            try
                // "---> Starting state server host..." |> Console.WriteLine
                stateServer <- startTestServer stateServerPort stateServerScriptPath output |> Async.RunSynchronously
                verifyPact output
            finally 
                // "---> Stopping state server host..." |> Console.WriteLine
                stopTestServer stateServer

        }

