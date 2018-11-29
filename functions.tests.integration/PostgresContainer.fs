namespace Integration 

module PostgresContainer =

    open System
    open Npgsql
    open Functions.Common.Database

    let yep () = "[YEP]" |> Console.WriteLine
    let nope () = "[NOPE]" |> Console.WriteLine
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
    let runDockerCommand cmd wait = 
        Console.WriteLine(sprintf "Exec: %s" cmd)
        let p = System.Diagnostics.Process.Start("docker",cmd)
        if wait then p.WaitForExit() |> ignore

    /// Wait until the postgres container is receiving connections.
    let ensureReady () = async {
        let maxTries = 10
        let delayMs = 2000
        let mutable count = 0
        let mutable isReady = false
        while isReady = false && count < maxTries do
            do! Async.Sleep(delayMs)
            let! error = tryConnect ()
            "---> Is PostgresQL server ready? " |> Console.Write
            match error with 
            | None ->
                yep()
                isReady <- true
            | Some(e) ->
                nope()
                count <- count + 1
                if (count = maxTries) then 
                    e |> sprintf "Database never became ready: %s" |> Exception |> raise
    }

    /// Ensure the postgres container is started
    let ensureStarted () = async {
        "---> Checking if PostgresQL Server is already running... " |> Console.Write
        let! error = tryConnect()
        match error with
        | None ->
            yep()
        | Some(_) ->
            nope()
            "---> Starting PostgresQL Server... "  |> Console.WriteLine
            runDockerCommand startSqlServer false
            do! ensureReady()
    }

    /// Stop and remove the running postgres container
    let stop () =
        "---> Stopping PostgresQL container... "  |> Console.WriteLine
        runDockerCommand stopSqlServer true
        "---> Removing PostgresQL container... "  |> Console.WriteLine
        runDockerCommand rmSqlServer true