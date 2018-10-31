namespace Integration

module TestHost =

    open Microsoft.AspNetCore.Hosting
    open Microsoft.Azure.WebJobs.Script
    open Microsoft.Extensions.Logging
    open Microsoft.Extensions.DependencyInjection
    open Xunit.Abstractions
    open Microsoft.Extensions.Options
    open System
    open System.IO
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

    let ensureWorkersFolderExists (output:ITestOutputHelper) =
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

    let startTestServer port scriptPath (output:ITestOutputHelper) = async {
        try

            let url = sprintf "http://*:%d" port
            let logger = XUnitLogger(output) :> ILogger

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

            let server = 
                WebHostBuilder()
                    .ConfigureServices(configureServices)
                    .ConfigureLogging(configureLogging)
                    .UseKestrel()
                    .UseUrls(url)
                    .UseStartup<Microsoft.Azure.WebJobs.Script.WebHost.Startup>()
                    .Build()

            ensureWorkersFolderExists output
            do! server.StartAsync() |> Async.AwaitTask
            do! waitUntilStarted port
            return Some(server)
        with
        | exn -> return None
    }

    let stopTestServer (server:IWebHost option) =
        match server with
        | None -> ()
        | Some(s) -> s.StopAsync() |> Async.AwaitTask |> Async.RunSynchronously