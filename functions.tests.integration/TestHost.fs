namespace Integration

module TestHost =

    open System
    open System.IO
    open System.Reactive.Disposables
    open Microsoft.AspNetCore.Hosting
    open Microsoft.Azure.WebJobs.Script
    open Microsoft.Extensions.Logging
    open Microsoft.Extensions.DependencyInjection
    open Microsoft.Extensions.Options
    open Xunit.Abstractions

    /// A shim that provides the test host access to hosting options.
    type TestOptionsMonitor<'T>(options:'T)=
        let options = options
        interface IOptionsMonitor<'T> with
            member this.CurrentValue = options
            member this.Get name = options
            member this.OnChange listener = Disposable.Empty

    /// A shim that provides the test host access to the XUnit logger.
    type TestLoggerProvider (output: ILogger)=
        let output = output
        interface ILoggerProvider with
            member this.CreateLogger (categoryName:string) = output
            member this.Dispose () = ()

    /// An ILogger adapter for the XUnit logger.
    type XUnitLogger(output: ITestOutputHelper) =
        let output = output
        interface ILogger with
            member this.Log(logLevel:LogLevel, eventId:EventId, state:'TState, ex:exn, formatter:Func<'TState,exn,string>) =
                (formatter.Invoke(state, ex)) |> sprintf "  [%A] %s" logLevel |> output.WriteLine
                if ex <> null
                then 
                    ex.ToString() 
                    |> sprintf "  Exception info: %s" 
                    |> output.WriteLine
            member this.IsEnabled (logLevel: LogLevel) = true
            member this.BeginScope (state:'TState) = Disposable.Empty

    let httpClient = new System.Net.Http.HttpClient()

    /// Periodically poke the test host URL until it returns a 200 response,
    /// indicating that the host is ready to receive requests.
    let waitUntilStarted port = async {
        let url = sprintf "http://localhost:%d/api/ping" port
        let maxAttempts = 50
        let mutable ready = false
        let mutable attempts = 0
        while not ready && attempts < maxAttempts do
            let! resp = url |> httpClient.GetAsync |> Async.AwaitTask
            ready <- resp.IsSuccessStatusCode
            if not ready
            then 
                do! Async.Sleep(100)
                attempts <- attempts + 1
        if attempts = maxAttempts 
        then port |> sprintf "Server on port %d never became ready" |> exn |> raise                   
    }        

    /// Ensure that a 'workers' folder exists in the the WebJobs.Script binary folder.
    /// This isn't directly used by our TestHost, but the server won't start without it.
    /// Ours is not to question why.
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

    /// Create and start a script host on the specified port
    let startTestServer port scriptPath (output:ITestOutputHelper) = async {
        try
            // The test host port binding
            let url = sprintf "http://*:%d" port

            // Configure the test host script and logging paths
            let configureServices (services:IServiceCollection) =
                ScriptApplicationHostOptions(
                   IsSelfHost=true,
                   ScriptPath= scriptPath,
                   LogPath=Path.Combine(Path.GetTempPath(), "Functions"))
                |> TestOptionsMonitor
                |> ServiceDescriptor.Singleton<IOptionsMonitor<ScriptApplicationHostOptions>>
                |> services.Add

            // Configure the test host logging
            let configureLogging (builder:ILoggingBuilder) =
                new TestLoggerProvider(XUnitLogger(output) :> ILogger)
                |> builder.AddProvider
                |> ignore

            // Build the test host server 
            let server = 
                WebHostBuilder()
                    .ConfigureServices(configureServices)
                    .ConfigureLogging(configureLogging)
                    .UseKestrel()
                    .UseUrls(url)
                    .UseStartup<Microsoft.Azure.WebJobs.Script.WebHost.Startup>()
                    .Build()

            // Start the test host server and wait for it to become ready
            ensureWorkersFolderExists output
            do! server.StartAsync() |> Async.AwaitTask
            do! waitUntilStarted port
            return Some(server)
        with
        | exn -> 
            // Log and re-raise the exception.
            exn.ToString() |> sprintf "Failed to start test server on port %d: %s" port |> output.WriteLine
            raise exn
            return None
    }

    /// Stop a previously started host 
    let stopTestServer (server:IWebHost option) =
        match server with
        | None -> ()
        | Some(s) -> s.StopAsync() |> Async.AwaitTask |> Async.RunSynchronously