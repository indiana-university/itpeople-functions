namespace Integration

module ContractTests =
    open Microsoft.AspNetCore.Hosting
    open Microsoft.Azure.WebJobs.Script
    open Microsoft.Extensions.Logging
    open Microsoft.Extensions.DependencyInjection
    open PactNet
    open PactNet.Infrastructure.Outputters
    open Xunit.Abstractions
    open Xunit
    open Microsoft.Extensions.Options
    open System
    open System.IO
    open System.Net
    open Integration.Hosting
    
    type XUnitLogger(output: ITestOutputHelper) =
        let output = output
        interface ILogger with
            member this.Log(logLevel:LogLevel, eventId:EventId, state:'TState, ex:exn, formatter:Func<'TState,exn,string>) =
                (formatter.Invoke(state, ex)) |> sprintf "  [%A] %s" logLevel |> output.WriteLine
                if ex <> null
                then ex.ToString() |> sprintf "  Exception info: %s" |> output.WriteLine
            member this.IsEnabled (logLevel: LogLevel) =
                true
            member this.BeginScope (state:'TState) =
                {
                     new IDisposable with
                        member x.Dispose () = () |> ignore
                }

    type XUnitOutput(output: ITestOutputHelper)=
        let output = output
        interface IOutput with  
            member this.WriteLine(message: string)=
                message |> output.WriteLine

    type Pact(output: ITestOutputHelper)=
        let output = output
        let logger = XUnitLogger(output) :> ILogger
        
        let httpClient = new System.Net.Http.HttpClient()
        let waitUntilStarted port = async {
            let url = sprintf "http://localhost:%d" port
            let maxAttempts = 50
            let mutable ready = false
            let mutable attempts = 0
            while not ready && attempts < maxAttempts do
                let! resp = httpClient.GetAsync(url) |> Async.AwaitTask
                ready <- resp.IsSuccessStatusCode
                if not ready
                then 
                    do! Async.Sleep(100)
                    attempts <- attempts + 1
            if attempts = maxAttempts 
            then port |> sprintf "Server on port %d never became ready" |> exn |> raise                   
        }        

        let startServer port scriptPath = async {

            let configureServices (services:IServiceCollection) =
                let hostOptions = 
                    ScriptApplicationHostOptions(
                       IsSelfHost=true,
                       ScriptPath= scriptPath,
                       LogPath=Path.Combine(Path.GetTempPath(), "Functions"))
                let optionsMonitor = TestOptionsMonitor(hostOptions);
                services.Remove(ServiceDescriptor.Singleton<IOptionsMonitor<ScriptApplicationHostOptions>>(optionsMonitor)) |> ignore
                services.Add(ServiceDescriptor.Singleton<IOptionsMonitor<ScriptApplicationHostOptions>>(optionsMonitor))

            let configureLogging (builder:ILoggingBuilder) =
                builder.AddProvider(new TestLoggerProvider(logger)) |> ignore

            let url = sprintf "http://*:%d" port
            let server = 
                WebHostBuilder()
                    .ConfigureServices(configureServices)
                    .ConfigureLogging(configureLogging)
                    .UseKestrel()
                    .UseUrls(url)
                    .UseStartup<Microsoft.Azure.WebJobs.Script.WebHost.Startup>()
                    .Build()
            
            do! server.StartAsync() |> Async.AwaitTask
            do! waitUntilStarted port
            return Some(server)
        }

        let verify functionPort statePort = 
            let functionUrl = sprintf "http://localhost:%d" functionPort
            let outputters = ResizeArray<IOutput> [XUnitOutput(output) :> IOutput]
            let verifier = PactVerifierConfig(Outputters=outputters, Verbose=true) |> PactVerifier
            verifier
                .ServiceProvider("API", functionUrl)
                .HonoursPactWith("Client")
                .PactUri("../../../pact.json")
                .Verify()

        let shutDown (server:IWebHost option) =
            match server with
            | None -> ()
            | Some(s) -> s.StopAsync() |> Async.AwaitTask |> Async.RunSynchronously

        let ensureWorkersFolderExists() =
            let directory = 
                typedefof<Microsoft.Azure.WebJobs.Script.Rpc.WorkerConfig>.Assembly.CodeBase 
                |> Uri 
                |> (fun uri -> uri.LocalPath) 
                |> Path.GetDirectoryName
            let path = Path.Combine(directory, "workers")
            if not (Directory.Exists(path))
            then
                path |> sprintf "Creating function workers folder at %s..." |> output.WriteLine
                Directory.CreateDirectory(path) |> ignore
            else
                path |> sprintf "Function workers folder exists at %s..." |> output.WriteLine

        [<Fact>]
        member __.``Test Contracts`` () = async {
            let functionServerPort = 7071
            let stateServerPort = 7072
            let mutable functionServer = None
            let mutable stateServer = None

            try            
                ensureWorkersFolderExists()
                "---> Starting Functions Host..." |> output.WriteLine
                let! functionsServer = startServer functionServerPort "../../../../functions/bin/Debug/netcoreapp2.1"
                "---> Started Functions Host.\n" |> output.WriteLine
                // "---> Starting State Host..." |> output.WriteLine
                // let! stateServer = startServer stateServerPort "../../../../functions/bin/Debug/netcoreapp2.1"
                // "---> Started State Host.\n" |> output.WriteLine
                "---> Verifying Contracts..." |> output.WriteLine
                verify functionServerPort stateServerPort
            finally
                "---> Stopping Functions Host..." |> output.WriteLine
                shutDown functionServer
                "---> Stopped Functions Host.\n" |> output.WriteLine
                // "---> Stopping State Host..." |> output.WriteLine
                // shutDown stateServer
                // "---> Stopped State Host." |> output.WriteLine
        }