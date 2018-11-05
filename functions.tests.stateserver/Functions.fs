namespace MyFunctions

open MyFunctions.Common.Types
open Microsoft.Azure.WebJobs
open Microsoft.AspNetCore.Http
open System.Net.Http
open Microsoft.Extensions.Logging
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

    /// The request body of the Pact client
    type ProviderState = {
        consumer: string
        state: string
    }

    /// Clear the database and migrated it to the latest version
    let resetDatabase connStr = 
        try
            clearAndMigrate connStr
            ok ()
        with
        | exn -> fail (HttpStatusCode.InternalServerError, (sprintf "migration failed with %s" (exn.ToString())))
    
    /// Initialize the database with a unit
    let initalizeDatabaseWithUnit1 connStr = asyncTrial {
        let dbConnection = new NpgsqlConnection(connStr)
        let! result = dbConnection.InsertAsync<Unit>(cito) |> Async.AwaitTask 
        return result.GetValueOrDefault()
    }

    /// Find the appropriate initialization function for the state
    /// described by the Pact client.
    let matcher (state:string) = 
        match state with 
        | "unit 1 is present" -> ok initalizeDatabaseWithUnit1
        | _ ->  
            let error = sprintf "I don't know what to do for state, '%s'" state
            fail (HttpStatusCode.BadRequest, error)
    
    /// Ensure that the database is reset and initialized as requested by
    /// the Pact client     
    let ensureState (req: HttpRequestMessage) connStr = asyncTrial {
        do! resetDatabase connStr
        let! body = deserializeBody<ProviderState> req
        match body.state with
        | null -> 
            return 0
        | _ -> 
            let! initializer = matcher (body.state)
            let! result = initializer connStr
            return result
    }

    [<FunctionName("InitializeState")>]
    let initializeState
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "post", Route = "state")>]
        req: HttpRequestMessage, log: ILogger) =
        let connStr = System.Environment.GetEnvironmentVariable("DbConnectionString")
        let fn () = ensureState req connStr
        Api.Common.getResponse' log fn

    
    /// (Anonymous) A function that simply returns, "Pong!" 
    [<FunctionName("PingGet")>]
    let ping
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "ping")>]
        req: HttpRequestMessage, log: ILogger, context: ExecutionContext) =
        let fn () = Api.Ping.get req
        // let fn () = Api.Ping.get req
        Api.Common.getResponse' log fn