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
open Microsoft.Extensions.Logging

module Common =

    let getConfiguration(context: ExecutionContext) : AppConfig =
        let configRoot = 
            ConfigurationBuilder()
                .AddJsonFile("local.settings.json", optional=true)
                .AddKeyPerFile("/run/secrets", optional=true)
                .AddEnvironmentVariables()
                .Build();

        {
            OAuth2ClientId = configRoot.["OAuthClientId"]
            OAuth2ClientSecret = configRoot.["OAuthClientSecret"]
            OAuth2TokenUrl = configRoot.["OAuthTokenUrl"]
            OAuth2RedirectUrl = configRoot.["OAuthRedirectUrl"]
            JwtSecret = configRoot.["JwtSecret"]
            DbConnectionString = configRoot.["DbConnectionString"]
            UseFakes = bool.Parse configRoot.["UseFakeData"]
            CorsHosts = configRoot.["CorsHosts"]
        }

    /// Get app configuration and data dependencies resolvers.
    let private getDependencies(context: ExecutionContext) : AppConfig*IDataRepository = 

        let config = getConfiguration context
        let data = 
            if config.UseFakes
            then FakesRepository() :> IDataRepository
            else DatabaseRepository(config.DbConnectionString) :> IDataRepository
        (config,data)

    /// Given an API function, resolve required dependencies and get a response.  
    let getResponse<'T> 
        (req: HttpRequestMessage)
        (log: ILogger) 
        (context: ExecutionContext) 
        (fn:(AppConfig*IDataRepository)->AsyncResult<'T,Error>) = 
        async {
            try
                let (config,data) = getDependencies(context)
                let! result = (config, data) |> fn |> Async.ofAsyncResult
                return constructResponse req log config.CorsHosts result
            with
            | exn -> 
                let msg = exn.ToString() |> sprintf "Unhandled exception in request: %s" 
                return constructResponse req log "" (fail(Status.InternalServerError, msg))
        } |> Async.StartAsTask

    /// Given an API function, get a response.  
    let getResponse'<'T> 
        (req: HttpRequestMessage)
        (log: ILogger) 
        (context: ExecutionContext) 
        (fn:unit->AsyncResult<'T,Error>) = 
        async { 
            try
                let (config,data) = getDependencies(context)
                let! result = () |> fn |> Async.ofAsyncResult
                return constructResponse req log config.CorsHosts result
            with
            | exn -> 
                let msg = exn.ToString() |> sprintf "Unhandled exception in request: %s" 
                return constructResponse req log "" (fail(Status.InternalServerError, msg))
        } |> Async.StartAsTask

    /// Given an API function, get a response.  
    let optionsResponse
        (req: HttpRequestMessage)
        (log: ILogger) 
        (context: ExecutionContext)  = 
            let (config,data) = getDependencies(context)
            let origin = origin req
            let response = new HttpResponseMessage(Status.OK)
            addCORSHeader response origin config.CorsHosts
            response

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
        return! fn ()
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
        return! fn id
    }

