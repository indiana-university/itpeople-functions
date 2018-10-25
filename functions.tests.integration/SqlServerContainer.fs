namespace Integration 
open Npgsql
open Npgsql

module SqlServerContainer=

    open System
    open System.Data.SqlClient
    open Dapper
    open MyFunctions.Common.Fakes
    open MyFunctions.Common.Types

    let connStr = "User ID=root;Host=localhost;Port=5432;Database=circle_test;Pooling=true;"
    let startSqlServer = """run --name integration_test_db -p 5432:5432 circleci/postgres:9.6.5-alpine-ram"""
    let logsSqlServer = "logs integration_test_db"   
    let stopSqlServer = "stop integration_test_db"   
    let rmSqlServer = "rm integration_test_db"   

    let result b = if b then "[OK]" else "[ERROR]"

    let tryConnect () = async {
        try
            use conn = new NpgsqlConnection(connStr)
            do! conn.OpenAsync() |> Async.AwaitTask
            return true
        with 
        | exn -> 
            sprintf "Failed to connect to server: %s" exn.Message |> Console.WriteLine
            return false
    }

    let runDockerCommand cmd wait = 
        Console.WriteLine(sprintf "Exec: %s" cmd)
        let p = System.Diagnostics.Process.Start("docker",cmd)
        if wait then p.WaitForExit() |> ignore

    let ensureReady () = async {
        let maxTries = 15
        let delayMs = 1000
        let mutable count = 0
        let mutable isReady = false
        while isReady = false && count < maxTries do
            "Checking if server is ready..." |> Console.Write
            let! isReady' = tryConnect ()
            isReady <- isReady'
            if isReady = false
            then
                "[NOPE]" |> Console.WriteLine
                count <- count + 1
                do! Async.Sleep(delayMs)
            else
                "[YEP]" |> Console.WriteLine
        
        return count = maxTries
    }

    let start () = async {
        runDockerCommand startSqlServer false
        let! started = ensureReady()
        // runDockerCommand logsSqlServer true
        return started
    }

    let stop () =
        runDockerCommand stopSqlServer true
        runDockerCommand rmSqlServer true

    let migrate () = 
        Migrations.Program.migrate connStr ["up"]

    let populate () = async {
        use cn = new NpgsqlConnection(connStr)
        cn.Open()
        let cmd = new NpgsqlCommand("INSERT INTO Units (Name, Description, Url) VALUES ('Foo', 'Bar', 'Baz')", cn)
        let rows = cmd.ExecuteNonQuery()
    
        //let! inserted = cn.InsertAsync<Unit>(biology) |> Async.AwaitTask
        return 1
        // return if inserted.HasValue then inserted.Value else 0
    }
