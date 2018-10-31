namespace Integration

module ContractTests =
    open PactNet
    open PactNet.Infrastructure.Outputters
    open Xunit.Abstractions
    open Xunit
    open TestFixture
    open TestHost
    
    type XUnitOutput(output: ITestOutputHelper)=
        let output = output
        interface IOutput with  
            member this.WriteLine(message: string)=
                message |> output.WriteLine

    let verifyPact functionPort statePort output = 
        let functionUrl = sprintf "http://localhost:%d" functionPort
        let outputters = ResizeArray<IOutput> [XUnitOutput(output) :> IOutput]
        let verifier = PactVerifierConfig(Outputters=outputters, Verbose=true) |> PactVerifier
        verifier
            .ServiceProvider("API", functionUrl)
            .HonoursPactWith("Client")
            .PactUri("../../../pact.json")
            .Verify()

    type Pact(output: ITestOutputHelper)=
        inherit HttpIntegrationTestBase()
        let output = output

        [<Fact>]
        member __.``Test Contracts`` () = async {
            let functionScriptPath = "../../../../functions/bin/Debug/netcoreapp2.1"
            let stateScriptPath = "../../../../functions/bin/Debug/netcoreapp2.1"
            let functionServerPort = 7071
            let stateServerPort = 7072
            let mutable functionServer = None
            let mutable stateServer = None

            try            
                "---> Starting Functions Host..." |> output.WriteLine
                let! functionsServer = startTestServer functionServerPort functionScriptPath output
                "---> Started Functions Host.\n" |> output.WriteLine
                // "---> Starting State Host..." |> output.WriteLine
                // let! stateServer = startTestServer stateServerPort stateScriptPath output
                // "---> Started State Host.\n" |> output.WriteLine
                "---> Verifying Pact..." |> output.WriteLine
                verifyPact functionServerPort stateServerPort output
            finally
                "---> Stopping Functions Host..." |> output.WriteLine
                stopTestServer functionServer
                "---> Stopped Functions Host.\n" |> output.WriteLine
                // "---> Stopping State Host..." |> output.WriteLine
                // stopTestServer stateServer
                // "---> Stopped State Host." |> output.WriteLine
        }