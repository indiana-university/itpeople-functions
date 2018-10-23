namespace MyFunctions

open MyFunctions.Common.Types
open MyFunctions.Common.Http
open MyFunctions.Common.Database
open MyFunctions.Common.Fakes
open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs
open Microsoft.AspNetCore.Http
open Microsoft.Azure.WebJobs.Host
open Microsoft.Extensions.Configuration
open System.Net.Http

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
        let data = DatabaseRepository(config.DbConnectionString) :> IDataRepository
        // let data = FakesRepository() :> IDataRepository
        (config,data)

    [<FunctionName("PingGet")>]
    let ping
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "ping")>]
        req: HttpRequestMessage, log: TraceWriter) =
        async {
            let! result = Api.Ping.get req |> Async.ofAsyncResult
            return constructResponse log result
        } |> Async.StartAsTask

    [<FunctionName("AuthGet")>]
    let auth
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "auth")>]
        req: HttpRequestMessage, log: TraceWriter, context: ExecutionContext) =
        async {
            let (config,data) = getDependencies(context)
            let queryUserByNetId = data.GetUserByNetId
            let! result = Api.Auth.Get.workflow req config data.GetUserByNetId |> Async.ofAsyncResult
            return constructResponse log result
        } |> Async.StartAsTask

    [<FunctionName("UserGetId")>]
    let profileGet
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "users/{id}")>]
        req: HttpRequestMessage, log: TraceWriter, context: ExecutionContext, id: Id) =
        async {
            let (config,data) = getDependencies(context)
            let fetch () = data.GetProfile id
            let! result = Api.Common.getAll req config fetch |> Async.ofAsyncResult
            return constructResponse log result
        } |> Async.StartAsTask

    [<FunctionName("UserGetMe")>]
    let profileGetMe
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "me")>]
        req: HttpRequestMessage, log: TraceWriter, context: ExecutionContext) = 
        async {
            let (config,data) = getDependencies(context)
            let! result = Api.User.getMe req config data.GetProfile |> Async.ofAsyncResult
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
        req: HttpRequestMessage, log: TraceWriter, context: ExecutionContext) =
        async {
            let (config,data) = getDependencies(context)
            let! result = Api.Search.getSimple req config data.GetSimpleSearchByTerm |> Async.ofAsyncResult
            return constructResponse log result
        } |> Async.StartAsTask


    [<FunctionName("UnitGetAll")>]
    let unitGetAll
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "units")>]
        req: HttpRequestMessage, log: TraceWriter, context: ExecutionContext) =
        async {
            let (config, data) = getDependencies(context)
            let! result = Api.Common.getAll req config data.GetUnits |> Async.ofAsyncResult
            return constructResponse log result
        } |> Async.StartAsTask
            
    [<FunctionName("UnitGetId")>]
    let unitGetId
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "units/{id}")>]
        req: HttpRequestMessage, log: TraceWriter, context: ExecutionContext, id: Id) =
        async {
            let (config,data) = getDependencies(context)
            let! result = Api.Common.getById req config id data.GetUnit |> Async.ofAsyncResult
            return constructResponse log result
        } |> Async.StartAsTask
            
    [<FunctionName("DepartmentGetAll")>]
    let departmentGetAll
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "departments")>]
        req: HttpRequestMessage, log: TraceWriter, context: ExecutionContext) =
        async {
            let (config,data) = getDependencies(context)
            let! result = Api.Common.getAll req config data.GetDepartments |> Async.ofAsyncResult
            return constructResponse log result
        } |> Async.StartAsTask

    [<FunctionName("DepartmentGetId")>]
    let departmentGetId
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "departments/{id}")>]
        req: HttpRequestMessage, log: TraceWriter, context: ExecutionContext, id: Id) =
        async {
            let (config,data) = getDependencies(context)
            let! result = Api.Common.getById req config id data.GetDepartment |> Async.ofAsyncResult
            return constructResponse log result
        } |> Async.StartAsTask
