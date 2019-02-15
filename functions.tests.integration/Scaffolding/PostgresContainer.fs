// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Integration 

module PostgresContainer =

    open System
    open System.Diagnostics
    open Npgsql
    open Dapper
    open Functions.Database
    open Xunit.Abstractions
    open Database.Fakes

    let result b = if b then "[OK]" else "[ERROR]"
    let startServer = """run --name integration_test_db -p 5432:5432 circleci/postgres:9.6.5-alpine-ram"""
    let logsSqlServer = "logs integration_test_db"   
    let stopSqlServer = "stop integration_test_db"   
    let rmSqlServer = "rm integration_test_db"   

    /// Attempt to connect to the postgres database.
    let tryConnect () = async {
        try
            use conn = new NpgsqlConnection(testConnectionString)
            do! conn.OpenAsync() |> Async.AwaitTask
            return None
        with 
        | exn -> 
            return sprintf "Failed to connect to server: %s" exn.Message |> Some
    }

    /// Attempt to connect to the postgres database.
    let clearSchema () = async {
        let sql = """
            DROP SCHEMA public CASCADE;
            CREATE SCHEMA public;
            GRANT ALL ON SCHEMA public TO postgres;
            GRANT ALL ON SCHEMA public TO public;"""
        try
            use db = new NpgsqlConnection(testConnectionString)
            db.Execute(sql) |> ignore
        with 
        | exn -> 
            return sprintf "Failed to clear schema: %s" exn.Message |> Exception |> raise
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
            | Some(e) ->
                count <- count + 1
                if (count = maxTries) then 
                    e |> sprintf "Database never became ready: %s" |> Exception |> raise
    }

    /// Ensure the postgres container is started
    let ensureDatabaseServerStarted log = async {
        "---> Starting PostgresQL Server... " |> log
        let! error = tryConnect()
        match error with
        | None -> ()
        | Some(_) ->
            "     Stopping and removing any running 'integration_test_db' containers..." |> log
            runDockerCommand log "stop integration_test_db" true
            runDockerCommand log "rm integration_test_db" true
            "     Starting PostgresQL Server in 'integration_test_db' container... "  |> log
            runDockerCommand log startServer false
            do! ensureReady log
            "     PostgresQL Server is available."  |> log
        "---> Clearing public schema..."  |> log
        do! clearSchema ()
    }