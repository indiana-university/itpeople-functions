namespace Integration 

module PostgresContainer =

    open System
    open System.Diagnostics
    open Npgsql
    open Functions.Common.Database
    open Xunit.Abstractions

    let result b = if b then "[OK]" else "[ERROR]"
    let connectionString = "User ID=root;Host=localhost;Port=5432;Database=circle_test;Pooling=true;"
    let startSqlServer = """run --name integration_test_db -p 5432:5432 circleci/postgres:9.6.5-alpine-ram"""
    let logsSqlServer = "logs integration_test_db"   
    let stopSqlServer = "stop integration_test_db"   
    let rmSqlServer = "rm integration_test_db"   

    /// Get a new postgres connection.
    let dbConnection () = sqlConnection connectionString
    
    /// Attempt to connect to the postgres database.
    let tryConnect () = async {
        try
            use conn = dbConnection ()
            do! conn.OpenAsync() |> Async.AwaitTask
            return None
        with 
        | exn -> 
            return sprintf "Failed to connect to server: %s" exn.Message |> Some
    }

    /// Execute an arbitrary docker command.
    let runDockerCommand log cmd wait = 

        let logConsoleOutput level data =
            if String.IsNullOrWhiteSpace(data)
            then ()
            else sprintf "%s: %s" level data |> log

        sprintf "EXEC: %s" cmd |> log
        let si = new ProcessStartInfo()
        si.FileName <- "docker"
        si.Arguments <- cmd
        si.UseShellExecute <- false
        si.RedirectStandardOutput <- true
        si.RedirectStandardError <- true
        let p = new System.Diagnostics.Process()
        p.StartInfo <- si
        p.EnableRaisingEvents <- true
        p.OutputDataReceived.AddHandler(DataReceivedEventHandler (fun _ a -> a.Data |> logConsoleOutput "INFO"))
        p.ErrorDataReceived.AddHandler(DataReceivedEventHandler (fun _ a -> a.Data |> logConsoleOutput "ERROR"))
        p.Start() |> ignore
        p.BeginOutputReadLine()
        p.BeginErrorReadLine()
        if wait then p.WaitForExit() |> ignore

    /// Wait until the postgres container is receiving connections.
    let ensureReady log = async {
        let maxTries = 10
        let delayMs = 2000
        let mutable count = 0
        let mutable isReady = false
        "---> Waiting for PostgresQL Server to beome available... " |> log
        while isReady = false && count < maxTries do
            do! Async.Sleep(delayMs)
            let! error = tryConnect ()
            match error with 
            | None ->
                isReady <- true
                "---> PostgresQL Server is available."  |> log
            | Some(e) ->
                count <- count + 1
                if (count = maxTries) then 
                    e |> sprintf "Database never became ready: %s" |> Exception |> raise
    }

    /// Ensure the postgres container is started
    let ensureDatabaseServerStarted log = async {
        "---> Checking if PostgresQL Server is already running... " |> log
        let! error = tryConnect()
        match error with
        | None ->
            "---> PostgresQL Server is running. "  |> log
        | Some(_) ->
            "---> Starting PostgresQL Server... "  |> log
            runDockerCommand log startSqlServer false
            do! ensureReady log
    }