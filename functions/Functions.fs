namespace Functions

open Types
open Http
open Api
open Jwt
open Util

open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs
open System.Net.Http
open System

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
        req: HttpRequestMessage) =
        "pong" |> jsonResponse req "*" Status.OK

    /// (Anonymous) Exchanges a UAA OAuth code for an application-scoped JWT
    [<FunctionName("AuthGet")>]
    let auth
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "auth")>]
        req: HttpRequestMessage, context: ExecutionContext) =
        let deps = resolveDependencies context
        
        // workflow partials
        let createUaaTokenRequest = createUaaTokenRequest deps.Config
        let requestTokenFromUaa = postAsync<UaaResponse> deps.Config.OAuth2TokenUrl
        let resolveAppUser claims = deps.Data.GetUserByNetId claims.UserName
        let encodeAppJwt = encodeAppJwt deps.Config.JwtSecret (now().AddHours(8.))

        getQueryParam "oauth_code" req
        >>= createUaaTokenRequest
        >>= await requestTokenFromUaa
        >>= decodeUaaJwt
        >>= await resolveAppUser
        >>= encodeAppJwt
        |> constructResponse req deps

    /// (Authenticated) Get a user profile for a given user 'id'
    [<FunctionName("UserGetId")>]
    let profileGet
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "people/{id}")>]
        req: HttpRequestMessage, context: ExecutionContext, id: Id) =
        let deps = resolveDependencies context
        authenticateRequest req deps
        >>= await (fun _ -> deps.Data.GetProfile id)
        |> constructResponse req deps

    /// (Authenticated) Get a user profile associated with the JWT in the request Authorization header.
    [<FunctionName("UserGetMe")>]
    let profileGetMe
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "me")>]
        req: HttpRequestMessage, context: ExecutionContext) = 
        let deps = resolveDependencies context
        let getUserProfile user = deps.Data.GetProfile user.UserId
        authenticateRequest req deps
        >>= await getUserProfile
        |> constructResponse req deps

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
        req: HttpRequestMessage, context: ExecutionContext) =
        let deps = resolveDependencies context
        authenticateRequest req deps
        >>= (fun _ -> getQueryParam "term" req)
        >>= await deps.Data.GetSimpleSearchByTerm
        |> constructResponse req deps

    /// (Authenticated) Get all units.
    [<FunctionName("UnitGetAll")>]
    let unitGetAll
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "units")>]
        req: HttpRequestMessage, context: ExecutionContext) =
        let deps = resolveDependencies context
        authenticateRequest req deps
        >>= await (fun _ -> deps.Data.GetUnits())
        |> constructResponse req deps

    /// (Authenticated) Get a unit profile for a given unit 'id'.
    [<FunctionName("UnitGetId")>]
    let unitGetId
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "units/{id}")>]
        req: HttpRequestMessage, context: ExecutionContext, id: Id) =
        let deps = resolveDependencies context
        authenticateRequest req deps
        >>= await (fun _ -> deps.Data.GetUnit id)
        |> constructResponse req deps
            
    /// (Authenticated) Get all departments.
    [<FunctionName("DepartmentGetAll")>]
    let departmentGetAll
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "departments")>]
        req: HttpRequestMessage, context: ExecutionContext) =
        let deps = resolveDependencies context
        authenticateRequest req deps
        >>= await (fun _ -> deps.Data.GetDepartments())
        |> constructResponse req deps

    /// (Authenticated) Get a department profile for a given department 'id'.
    [<FunctionName("DepartmentGetId")>]
    let departmentGetId
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "departments/{id}")>]
        req: HttpRequestMessage, context: ExecutionContext, id: Id) =
        let deps = resolveDependencies context
        authenticateRequest req deps
        >>= await (fun _ -> deps.Data.GetDepartment id)
        |> constructResponse req deps
