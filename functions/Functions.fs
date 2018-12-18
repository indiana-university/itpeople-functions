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

    let config = getConfiguration()
    let data = getData config
    let log = createLogger config

    /// Update the request properties with the start time of this request.
    let start (req:HttpRequestMessage) = 
        req.Properties.Add(WorkflowStarted, DateTime.UtcNow)
        req

    /// Attempt to authenticate the request and update the request properties 
    /// with the net ID of the authenticated user.
    let authenticate (req:HttpRequestMessage) = 
        req
        |> start
        |> authenticateRequest config
        >>= tap (fun user -> req.Properties.Add(WorkflowUser, user.UserName))
        
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

        // workflow partials
        let createUaaTokenRequest = createUaaTokenRequest config
        let requestTokenFromUaa = postAsync<UaaResponse> config.OAuth2TokenUrl
        let resolveAppUserId claims = data.TryGetPersonId claims.UserName
        let encodeAppJwt = encodeAppJwt config.JwtSecret (now().AddHours(8.))
        // let logSignin user = sprintf "%s signed in" (fst user) |> logInfo log req

        req
        |> start
        |> getQueryParam "oauth_code"
        >>= createUaaTokenRequest
        >>= await requestTokenFromUaa
        >>= decodeUaaJwt
        >>= await resolveAppUserId
        >>= encodeAppJwt
        |> createResponse req config log

        
    /// (Authenticated) Get a user profile for a given user 'id'
    [<FunctionName("UserGetId")>]
    let profileGet
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{id}")>]
        req: HttpRequestMessage, id: Id) =
        req 
        |> authenticate
        >>= await (fun _ -> data.GetProfile id)
        |> createResponse req config log

    /// (Authenticated) Get a user profile associated with the JWT in the request Authorization header.
    [<FunctionName("UserGetMe")>]
    let profileGetMe
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")>]
        req: HttpRequestMessage) = 
        req 
        |> authenticate
        >>= await (fun user -> data.GetProfile user.UserId)
        |> createResponse req config log

    /// (Authenticated) Get all users, departments, and units that match a 'term' query.
    [<FunctionName("SearchGet")>]
    let searchSimpleGet
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "search")>]
        req: HttpRequestMessage) =
        req
        |> authenticate
        >>= (fun _ -> getQueryParam "term" req)
        >>= await data.GetSimpleSearchByTerm
        |> createResponse req config log

    /// (Authenticated) Get all units.
    [<FunctionName("UnitGetAll")>]
    let unitGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units")>]
        req: HttpRequestMessage) =
        req 
        |> authenticate
        >>= await (fun _ -> data.GetUnits())
        |> createResponse req config log

    /// (Authenticated) Get a unit profile for a given unit 'id'.
    [<FunctionName("UnitGetId")>]
    let unitGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{id}")>]
        req: HttpRequestMessage, id: Id) =
        req 
        |> authenticate
        >>= await (fun _ -> data.GetUnit id)
        |> createResponse req config log
            
    /// (Authenticated) Get all departments.
    [<FunctionName("DepartmentGetAll")>]
    let departmentGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments")>]
        req: HttpRequestMessage) =
        req 
        |> authenticate
        >>= await (fun _ -> data.GetDepartments())
        |> createResponse req config log

    /// (Authenticated) Get a department profile for a given department 'id'.
    [<FunctionName("DepartmentGetId")>]
    let departmentGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{id}")>]
        req: HttpRequestMessage, id: Id) =
        req 
        |> authenticate
        >>= await (fun _ -> data.GetDepartment id)
        |> createResponse req config log
