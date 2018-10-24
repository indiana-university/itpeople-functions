namespace Integration 

module SqlServerContainer=

    open System
    open Docker.DotNet
    open Docker.DotNet.Models
    open System.Data.SqlClient
    open System.Collections.Generic
    open Dapper
    open MyFunctions.Common
    open MyFunctions.Common.Types
    open System.Runtime.InteropServices

    let connStr = "Server=127.0.0.1,1433;User Id=sa;Password=Abcd1234!;Timeout=5"
    let startSqlServer = """run --name intgration_test_mssql -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=Abcd1234!" -p 1433:1433 -d microsoft/mssql-server-linux:2017-latest"""
    let stopSqlServer = "stop intgration_test_mssql"   
    let rmSqlServer = "rm intgration_test_mssql"   

    let result b = if b then "[OK]" else "[ERROR]"

    let private ready () = async {
        try
            use conn = new SqlConnection(connStr)
            do! conn.OpenAsync() |> Async.AwaitTask
            return true
        with 
        | exn -> 
            // sprintf "Failed to connect to server: %s" exn.Message |> Console.WriteLine
            return false
    }
    let private ensureReady () = async {
        let maxTries = 15
        let delayMs = 1000
        let mutable count = 0
        let mutable isReady = false
        while isReady = false && count < maxTries do
            "Checking if server is ready..." |> Console.Write
            let! isReady' = ready()
            isReady <- isReady'
            if isReady = false
            then
                "[NOPE]" |> Console.WriteLine
                count <- count + 1
                do! Async.Sleep(delayMs)
            else
                "[YEP]" |> Console.WriteLine
        
        if count = maxTries
        then raise(Exception("SQL Server never became ready. :("))
    }

    let runDockerCommand cmd = 
        Console.WriteLine(sprintf "Exec: %s" cmd)
        let p = System.Diagnostics.Process.Start("docker",cmd)
        p.WaitForExit() |> ignore

    let start container = async {
        runDockerCommand startSqlServer
        do! ensureReady()
        return true
    }

    let stop () =
        runDockerCommand stopSqlServer
        runDockerCommand rmSqlServer

    let migrate () = 
        Program.migrate connStr ["up"]

    let populate () = async {
        use cn = new SqlConnection(connStr)
        let! inserted = cn.InsertAsync<Unit>(Fakes.cito) |> Async.AwaitTask
        return if inserted.HasValue then inserted.Value else 0
    }
