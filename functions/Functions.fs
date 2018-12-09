namespace Functions

open Types
open Http
open Api
open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs
open System.Net.Http
///<summary>
/// This module defines the bindings and triggers for all functions in the project
///</summary
module Functions =

    /// (Anonymous) A function that simply returns, "Pong!" 
    [<FunctionName("Options")>]
    let options
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "options", Route = "{*url}")>]
        req: HttpRequestMessage, context: ExecutionContext) =
        optionsResponse req context

    [<FunctionName("PingGet")>]
    let ping
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "ping")>]
        req: HttpRequestMessage, context: ExecutionContext) =
        let getResponse = asyncTrial { return { MessageResult.Message="pong!" } }
        getAnonymousResponse req context (fun _ _ -> getResponse)

    /// (Anonymous) Exchanges a UAA OAuth code for an application-scoped JWT
    [<FunctionName("AuthGet")>]
    let auth
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "auth")>]
        req: HttpRequestMessage, context: ExecutionContext) =
        getAnonymousResponse req context (fun cfg data -> getAuthToken req cfg data.GetUserByNetId)

    /// (Authenticated) Get a user profile for a given user 'id'
    [<FunctionName("UserGetId")>]
    let profileGet
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "people/{id}")>]
        req: HttpRequestMessage, context: ExecutionContext, id: Id) =
        getAuthorizedResponse req context (fun data _ -> data.GetProfile id)

    /// (Authenticated) Get a user profile associated with the JWT in the request Authorization header.
    [<FunctionName("UserGetMe")>]
    let profileGetMe
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "me")>]
        req: HttpRequestMessage, context: ExecutionContext) = 
        getAuthorizedResponse req context (fun data user -> data.GetProfile user.UserId)

    // [<FunctionName("UserPut")>]
    // let profilePut
    //     ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "put", Route = "users/{id}")>]
    //     req: HttpRequest,
    //     logger: Iloggerger,
    //     context: ExecutionContext,
    //     id: Id) =
    //         context |> appConfig |> User.Put.run req logger id |> Async.StartAsTask

    /// (Authenticated) Get all users, departments, and units that match a 'term' query.
    [<FunctionName("SearchGet")>]
    let searchSimpleGet
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "search")>]
        req: HttpRequestMessage, ctx: ExecutionContext) =
        let getResponse getSearchResults = asyncTrial {
            let! term = getQueryParam "term" req
            return! getSearchResults term
        }
        getAuthorizedResponse req ctx (fun data _ -> getResponse data.GetSimpleSearchByTerm)


    /// (Authenticated) Get all units.
    [<FunctionName("UnitGetAll")>]
    let unitGetAll
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "units")>]
        req: HttpRequestMessage, context: ExecutionContext) =
        getAuthorizedResponse req context (fun data _ -> data.GetUnits())
            
    /// (Authenticated) Get a unit profile for a given unit 'id'.
    [<FunctionName("UnitGetId")>]
    let unitGetId
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "units/{id}")>]
        req: HttpRequestMessage, context: ExecutionContext, id: Id) =
        getAuthorizedResponse req context (fun data _ -> data.GetUnit id)
            
    /// (Authenticated) Get all departments.
    [<FunctionName("DepartmentGetAll")>]
    let departmentGetAll
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "departments")>]
        req: HttpRequestMessage, context: ExecutionContext) =
        getAuthorizedResponse req context (fun data _ -> data.GetDepartments())

    /// (Authenticated) Get a department profile for a given department 'id'.
    [<FunctionName("DepartmentGetId")>]
    let departmentGetId
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "departments/{id}")>]
        req: HttpRequestMessage, context: ExecutionContext, id: Id) =
        getAuthorizedResponse req context (fun data _ -> data.GetDepartment id)
