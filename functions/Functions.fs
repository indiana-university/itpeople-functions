// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open Types
open Http
open Api
open Jwt
open Util
open Logging

open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs
open System.Net.Http
open System
open Microsoft.Azure.WebJobs.Extensions.Http

///<summary>
/// This module defines the bindings and triggers for all functions in the project
///</summary
module Functions =    

    let start = System.Diagnostics.Stopwatch.StartNew

    let config = getConfiguration()
    let data = getData config
    let log = createLogger config

    /// (Anonymous) A function that simply returns, "Pong!" 
    [<FunctionName("Options")>]
    let options
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "{*url}")>]
        req: HttpRequestMessage) =
        optionsResponse req config

    [<FunctionName("PingGet")>]
    let ping
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")>]
        req: HttpRequestMessage) =
        "pong" |> jsonResponse req "*" Status.OK

    /// (Anonymous) Exchanges a UAA OAuth code for an application-scoped JWT
    [<FunctionName("AuthGet")>]
    let auth
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth")>]
        req: HttpRequestMessage) =
        let timer = start()

        // workflow partials
        let createUaaTokenRequest = createUaaTokenRequest config
        let requestTokenFromUaa = postAsync<UaaResponse> config.OAuth2TokenUrl
        let resolveAppUser claims = data.GetUserByNetId claims.UserName
        let encodeAppJwt = encodeAppJwt config.JwtSecret (now().AddHours(8.))

        getQueryParam "oauth_code" req
        >>= createUaaTokenRequest
        >>= await requestTokenFromUaa
        >>= decodeUaaJwt
        >>= await resolveAppUser
        >>= encodeAppJwt
        |> createResponse req config log timer

    /// (Authenticated) Get a user profile for a given user 'id'
    [<FunctionName("UserGetId")>]
    let profileGet
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{id}")>]
        req: HttpRequestMessage, id: Id) =
        let timer = start()
        authenticateRequest req config
        >>= await (fun _ -> data.GetProfile id)
        |> createResponse req config log (start())

    /// (Authenticated) Get a user profile associated with the JWT in the request Authorization header.
    [<FunctionName("UserGetMe")>]
    let profileGetMe
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")>]
        req: HttpRequestMessage) = 
        let timer = start()
        authenticateRequest req config
        >>= await (fun user -> data.GetProfile user.UserId)
        |> createResponse req config log timer

    /// (Authenticated) Get all users, departments, and units that match a 'term' query.
    [<FunctionName("SearchGet")>]
    let searchSimpleGet
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "search")>]
        req: HttpRequestMessage) =
        let timer = start()
        authenticateRequest req config
        >>= (fun _ -> getQueryParam "term" req)
        >>= await data.GetSimpleSearchByTerm
        |> createResponse req config log timer

    /// (Authenticated) Get all units.
    [<FunctionName("UnitGetAll")>]
    let unitGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units")>]
        req: HttpRequestMessage) =
        let timer = start()
        authenticateRequest req config
        >>= await (fun _ -> data.GetUnits())
        |> createResponse req config log timer

    /// (Authenticated) Get a unit profile for a given unit 'id'.
    [<FunctionName("UnitGetId")>]
    let unitGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{id}")>]
        req: HttpRequestMessage, id: Id) =
        let timer = start()
        authenticateRequest req config
        >>= await (fun _ -> data.GetUnit id)
        |> createResponse req config log timer
            
    /// (Authenticated) Get all departments.
    [<FunctionName("DepartmentGetAll")>]
    let departmentGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments")>]
        req: HttpRequestMessage) =
        let timer = start()
        authenticateRequest req config
        >>= await (fun _ -> data.GetDepartments())
        |> createResponse req config log timer

    /// (Authenticated) Get a department profile for a given department 'id'.
    [<FunctionName("DepartmentGetId")>]
    let departmentGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{id}")>]
        req: HttpRequestMessage, id: Id) =
        let timer = start()
        authenticateRequest req config
        >>= await (fun _ -> data.GetDepartment id)
        |> createResponse req config log timer
