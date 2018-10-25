namespace Integration 

module SqlServerContainer=

    open System
    open Npgsql

    let yep () = "[YEP]" |> Console.WriteLine
    let nope () = "[NOPE]" |> Console.WriteLine
    let result b = if b then "[OK]" else "[ERROR]"
    let connStr = "User ID=root;Host=localhost;Port=5432;Database=circle_test;Pooling=true;"
    let startSqlServer = """run --name integration_test_db -p 5432:5432 circleci/postgres:9.6.5-alpine-ram"""
    let logsSqlServer = "logs integration_test_db"   
    let stopSqlServer = "stop integration_test_db"   
    let rmSqlServer = "rm integration_test_db"   
    let tryConnect () = async {
        try
            use conn = new NpgsqlConnection(connStr)
            do! conn.OpenAsync() |> Async.AwaitTask
            return true
        with 
        | exn -> 
            // sprintf "Failed to connect to server: %s" exn.Message |> Console.WriteLine
            return false
    }

    let runDockerCommand cmd wait = 
        Console.WriteLine(sprintf "Exec: %s" cmd)
        let p = System.Diagnostics.Process.Start("docker",cmd)
        if wait then p.WaitForExit() |> ignore

    let ensureReady () = async {
        let maxTries = 10
        let delayMs = 2000
        let mutable count = 0
        let mutable isReady = false
        while isReady = false && count < maxTries do
            do! Async.Sleep(delayMs)
            let! isReady' = tryConnect ()
            isReady <- isReady'
            "---> Is PostgresQL server ready? " |> Console.Write
            if isReady = false
            then
                nope()
                count <- count + 1
            else
                yep()
        
        return count = maxTries
    }

    let ensureStarted () = async {
        "---> Checking if PostgresQL Server is already running... " |> Console.Write
        let! alreadyStarted = tryConnect()
        if alreadyStarted
        then 
            yep()
            return true
        else 
            nope()
            "---> Starting PostgresQL Server... "  |> Console.WriteLine
            runDockerCommand startSqlServer false
            let! started = ensureReady()
            return started
    }

    let stop () =
        "---> Stopping PostgresQL container... "  |> Console.WriteLine
        runDockerCommand stopSqlServer true
        "---> Removing PostgresQL container... "  |> Console.WriteLine
        runDockerCommand rmSqlServer true

    let migrate () = 
        use db = new NpgsqlConnection(connStr) 
        let migrator = db |> Migrations.Program.migrator
        migrator.Load()
        "---> Resetting database and applying all migrations... "  |> Console.WriteLine
        migrator.MigrateTo(int64 0)
        migrator.MigrateToLatest()
    let populate () = async {
        use cn = new NpgsqlConnection(connStr)
        cn.Open()
        let cmd = new NpgsqlCommand("INSERT INTO Units (Name, Description, Url) VALUES ('Foo', 'Bar', 'Baz')", cn)
        let rows = cmd.ExecuteNonQuery()
    
        //let! inserted = cn.InsertAsync<Unit>(biology) |> Async.AwaitTask
        return 1
        // return if inserted.HasValue then inserted.Value else 0
    }
