namespace Functions.Api

open Chessie.ErrorHandling
open Functions.Common.Types
open Functions.Common.Jwt
open System.Net.Http
open Functions.Common.Database
open Functions.Common.Fakes
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Microsoft.Extensions.Configuration
open Functions.Common.Http
open System
open Serilog.Core
open Microsoft.Extensions.Logging

module Common =
    /// Get app configuration and data dependencies resolvers.
    let private getDependencies(context: ExecutionContext) : AppConfig*IDataRepository = 
        let configRoot = 
            ConfigurationBuilder()
                .AddJsonFile("local.settings.json", optional=true, reloadOnChange=true)
                .AddKeyPerFile("/run/secrets", optional=true)
                .AddEnvironmentVariables()
                .Build();

        let appConfig = {
            OAuth2ClientId = configRoot.["OAuthClientId"]
            OAuth2ClientSecret = configRoot.["OAuthClientSecret"]
            OAuth2TokenUrl = configRoot.["OAuthTokenUrl"]
            OAuth2RedirectUrl = configRoot.["OAuthRedirectUrl"]
            JwtSecret = configRoot.["JwtSecret"]
            DbConnectionString = configRoot.["DbConnectionString"]
            UseFakes = bool.Parse configRoot.["UseFakeData"]
        }

        let data = 
            if appConfig.UseFakes
            then FakesRepository() :> IDataRepository
            else DatabaseRepository(appConfig.DbConnectionString) :> IDataRepository

        (appConfig,data)

    /// Given an API function, resolve required dependencies and get a response.  
    let getResponse<'T> 
        (req: HttpRequestMessage)
        (log: Logger) 
        (context: ExecutionContext) 
        (fn:(AppConfig*IDataRepository)->AsyncResult<'T,Error>) = 
        async {
            try
                let (config,data) = getDependencies(context)
                let! result = (config, data) |> fn |> Async.ofAsyncResult
                return constructResponse req log result
            with
            | exn -> 
                let msg = exn.ToString() |> sprintf "Unhandled exception in request: %s" 
                return constructResponse req log (fail(Status.InternalServerError, msg))
        } |> Async.StartAsTask

    /// Given an API function, get a response.  
    let getResponse'<'T> 
        (req: HttpRequestMessage)
        (log: Logger) 
        (fn:unit->AsyncResult<'T,Error>) = 
        async { 
            try
                let! result = () |> fn |> Async.ofAsyncResult
                return constructResponse req log result
            with
            | exn -> 
                let msg = exn.ToString() |> sprintf "Unhandled exception in request: %s" 
                return constructResponse req log (fail(Status.InternalServerError, msg))
        } |> Async.StartAsTask

    /// <summary>
    /// Get all items.
    /// </summary>
    /// <param name="req">The HTTP request that triggered this function</param>
    /// <param name="config">The application configuration</param>
    /// <param name="fn">A function to fetch all items</param>
    /// <returns>
    /// A collection of items, or error information.
    /// </returns>
    let getAll<'T> (req:HttpRequestMessage) (config:AppConfig) (fn:unit->AsyncResult<'T,Error>) = asyncTrial {
        let! _ = requireMembership config req
        let! result = fn ()
        return result
    }

    /// <summary>
    /// Get a single item by ID.
    /// </summary>
    /// <param name="req">The HTTP request that triggered this function</param>
    /// <param name="config">The application configuration</param>
    /// <param name="fn">A function to fetch a given item by its Id</param>
    /// <returns>
    /// A single item, or error information.
    /// </returns>
    let getById<'T> (req:HttpRequestMessage) (config:AppConfig) (id:Id) (fn:Id->AsyncResult<'T,Error>) = asyncTrial {
        let! _ = requireMembership config req
        let! result = fn id
        return result
    }

