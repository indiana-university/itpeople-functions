namespace MyFunctions

open MyFunctions.Common.Types
open Microsoft.Azure.WebJobs
open Microsoft.AspNetCore.Http
open Microsoft.Azure.WebJobs.Host
open System.Net.Http
open Microsoft.Extensions.Logging
open Microsoft.Azure.WebJobs.Host
open Microsoft.Azure.WebJobs.Host
open Chessie.ErrorHandling
open Common.Http
open System.Net
open Npgsql
open Dapper
open MyFunctions.Common.Fakes
open Migrations.Program

///<summary>
/// This module defines the bindings and triggers for all functions in the project
///</summary
module Functions =

    let connStr = "User ID=root;Host=localhost;Port=5432;Database=circle_test;Pooling=true;"
    
    type ProviderState = {
        Consumer: string
        State: string
    }

    let resetDatabase () = 
        try
            clearAndMigrate connStr
            ok ()
        with
        | exn -> fail (HttpStatusCode.InternalServerError, (sprintf "migration failed with %s" (exn.ToString())))
    
    let populateDatabaseWithUnit1 () = asyncTrial {
        let dbConnection = new NpgsqlConnection(connStr)
        let! result = dbConnection.InsertAsync<Unit>(cito) |> Async.AwaitTask 
        return result.GetValueOrDefault()
    }

    let matcher (arg:string) = 
        match arg with 
        | "unit 1 is present" -> ok populateDatabaseWithUnit1
        | _ ->  fail (HttpStatusCode.BadRequest, (sprintf "no state defined for %s" arg))
    
         
    let setState (req: HttpRequestMessage) = asyncTrial {
        let! state = deserializeBody<ProviderState> req
        let! fn = matcher state.State

        do! resetDatabase()
        let! result = fn()
        return result
    }

    /// (Anonymous) A function that simply returns, "Pong!" 
    [<FunctionName("InitializeState")>]
    let ping
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "post", Route = "state")>]
        req: HttpRequestMessage, log: ILogger) =
        let fn () = setState req
        Api.Common.getResponse' log fn

    
    