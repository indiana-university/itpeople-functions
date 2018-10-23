namespace MyFunctions.Api

open Chessie.ErrorHandling
open MyFunctions.Common.Types
open MyFunctions.Common.Jwt
open System.Net.Http
open MyFunctions.Common.Database
open MyFunctions.Common.Fakes
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Microsoft.Extensions.Configuration
open MyFunctions.Common.Http

module Common =

    let appConfig (context:ExecutionContext) = 
        let config = 
            ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional=true, reloadOnChange= true)
                .AddEnvironmentVariables()
                .Build();
        {
            OAuth2ClientId = config.["OAuthClientId"]
            OAuth2ClientSecret = config.["OAuthClientSecret"]
            OAuth2TokenUrl = config.["OAuthTokenUrl"]
            OAuth2RedirectUrl = config.["OAuthRedirectUrl"]
            JwtSecret = config.["JwtSecret"]
            DbConnectionString = config.["DbConnectionString"]
        }

    let getDependencies(context: ExecutionContext) : AppConfig*IDataRepository = 
        let config = context |> appConfig
        let data = DatabaseRepository(config.DbConnectionString) :> IDataRepository
        // let data = FakesRepository() :> IDataRepository
        (config,data)

    let getResponse<'T> 
        (log: TraceWriter) 
        (context: ExecutionContext) 
        (fn:(AppConfig*IDataRepository)->AsyncResult<'T,Error>) = 
        async {
            let (config,data) = getDependencies(context)
            let! result = (config, data) |> fn |> Async.ofAsyncResult
            return constructResponse log result
        } |> Async.StartAsTask

    let getResponse'<'T> 
        (log: TraceWriter) 
        (fn:unit->AsyncResult<'T,Error>) = 
        async {
            let! result = () |> fn |> Async.ofAsyncResult
            return constructResponse log result
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

