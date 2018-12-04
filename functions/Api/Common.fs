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
open System.Diagnostics
open Serilog
open Serilog.Core
open Microsoft.Extensions.Logging

module Common =

    let private getConfiguration(context: ExecutionContext) : AppConfig =
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

    let private getData config =
        if config.UseFakes
        then FakesRepository() :> IDataRepository
        else DatabaseRepository(config.DbConnectionString) :> IDataRepository

    let private getLogger config =
        Serilog.LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.ApplicationInsightsTraces(System.Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY"))
            .CreateLogger()

    let private resolveDependenciesAndDo req context fn = 
        async {
            let config = getConfiguration context
            let data = getData config
            let log = getLogger config
            try
                let! result = fn config data |> Async.ofAsyncResult
                return constructResponse req log config.CorsHosts result
            with
            | exn -> 
                exn.ToStringDemystified() |> sprintf "Unhandled exception: %s" |> log.Error
                return (jsonResponse req "*" Status.InternalServerError "A server error occurred.")
        } |> Async.StartAsTask

    /// Given an API function, resolve required dependencies and get a response.  
    let getAnonymousResponse<'T> req context (fn: AppConfig->IDataRepository->AsyncResult<'T,Error>) =
        resolveDependenciesAndDo req context fn
    
    let doWithAuth<'T> (req:HttpRequestMessage) (config:AppConfig) (fn:JwtClaims->AsyncResult<'T,Error>) = asyncTrial {
        let! user = authorizeRequest config req
        return! fn user
    }

    /// Given an API function, resolve required dependencies and get a response.  
    let getAuthorizedResponse<'T> 
        (req: HttpRequestMessage)
        (context: ExecutionContext) 
        (fn: IDataRepository -> JwtClaims -> AsyncResult<'T,Error>) = 
        resolveDependenciesAndDo req context (fun config data -> doWithAuth req config (fn data))

    /// Given an API function, get a response.  
    let optionsResponse
        (req: HttpRequestMessage)
        (context: ExecutionContext)  = 
            let config = getConfiguration context
            let origin = origin req
            let response = new HttpResponseMessage(Status.OK)
            addCORSHeader response origin config.CorsHosts
            response
