namespace MyFunctions

open MyFunctions.Types
open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs
open Microsoft.AspNetCore.Http
open Microsoft.Azure.WebJobs.Host
open Microsoft.Extensions.Configuration
open Common
open Types

///<summary>
/// This module defines the bindings and triggers for all functions in the project
///</summary
module Functions =

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
        let data = Database.DatabaseRepository(config.DbConnectionString) :> IDataRepository
        // let data = Fakes.FakesRepository() :> IDataRepository
        (config,data)

    [<FunctionName("PingGet")>]
    let ping
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "ping")>]
        req: HttpRequest,
        log: TraceWriter) =
            Ping.Get.run req log |> Async.StartAsTask

    [<FunctionName("AuthGet")>]
    let auth
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "auth")>]
        req: HttpRequest,
        log: TraceWriter,
        context: ExecutionContext) =
            let (config,data) = getDependencies(context)
            Auth.Get.run req log data config |> Async.StartAsTask

    [<FunctionName("UserGetId")>]
    let profileGet
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "users/{id}")>]
        req: HttpRequest,
        log: TraceWriter,
        context: ExecutionContext,
        id: Id) =
        async {
            let (config,data) = getDependencies(context)
            let! result = User.getById req config id data.GetProfile |> Async.ofAsyncResult
            return constructResponse log result
        } |> Async.StartAsTask

    [<FunctionName("UserGetMe")>]
    let profileGetMe
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "me")>]
        req: HttpRequest,
        log: TraceWriter,
        context: ExecutionContext) = 
        async {
            let (config,data) = getDependencies(context)
            let! result = User.getMe req config data.GetProfile |> Async.ofAsyncResult
            return constructResponse log result
        } |> Async.StartAsTask

    // [<FunctionName("UserPut")>]
    // let profilePut
    //     ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "put", Route = "users/{id}")>]
    //     req: HttpRequest,
    //     log: TraceWriter,
    //     context: ExecutionContext,
    //     id: Id) =
    //         context |> appConfig |> User.Put.run req log id |> Async.StartAsTask

    [<FunctionName("SearchGet")>]
    let searchSimpleGet
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "search")>]
        req: HttpRequest,
        log: TraceWriter,
        context: ExecutionContext) =
            let (config,data) = getDependencies(context)
            Search.GetSimple.run req log data config |> Async.StartAsTask


    [<FunctionName("UnitGetAll")>]
    let unitGetAll
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "units")>]
        req: HttpRequest,
        log: TraceWriter,
        context: ExecutionContext) =
        async {
            let (config, data) = getDependencies(context)
            let! result = Unit.getAll req config data.GetUnits |> Async.ofAsyncResult
            return constructResponse log result
        } |> Async.StartAsTask
            
    [<FunctionName("UnitGetId")>]
    let unitGetId
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "units/{id}")>]
        req: HttpRequest,
        log: TraceWriter,
        context: ExecutionContext,
        id: Id) =
        async {
            let (config,data) = getDependencies(context)
            let! result = Unit.getById req config id data.GetUnit |> Async.ofAsyncResult
            return constructResponse log result
        } |> Async.StartAsTask
            
    [<FunctionName("DepartmentGetAll")>]
    let departmentGetAll
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "departments")>]
        req: HttpRequest,
        log: TraceWriter,
        context: ExecutionContext) =
            let (config,data) = getDependencies(context)
            Department.GetAll.run req log data config |> Async.StartAsTask

    [<FunctionName("DepartmentGetId")>]
    let departmentGetId
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "departments/{id}")>]
        req: HttpRequest,
        log: TraceWriter,
        context: ExecutionContext,
        id: Id) =
            let (config,data) = getDependencies(context)
            Department.GetId.run req log data id config |> Async.StartAsTask
