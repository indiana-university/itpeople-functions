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

    /// DEPENDENCY RESOLUTION

    /// Dependencies are resolved once at startup.
    let config = getConfiguration()
    let data = getData config
    let log = createLogger config


    /// FUNCTION WORKFLOW HELPERS 

    let addProperty (req:HttpRequestMessage) key value = 
        req.Properties.Add(key, value)

    /// Logging: Add a timestamp to the request properties.
    let timestamp req = 
        addProperty req WorkflowTimestamp DateTime.UtcNow
        req

    /// Logging: Add the authenticated user to the request properties
    let recordAuthenticatedUser req user =
        addProperty req WorkflowUser user.UserName
        ok user
    
    /// Log and rethrow an unhandled exception.
    let handle req exn = 
        logFatal log req exn
        raise exn

    /// Execute a workflow for an anonymous user and return a response.
    let execAnonymousWorkflow workflow req =
        try
            req
            |> timestamp
            |> workflow
            |> createResponse req config log
        with exn -> handle req exn

    /// Execute a workflow for an authenticated user and return a response.
    let execAuthenticatedWorkflow workflow req =
        try
            req
            |> timestamp
            |> authenticateRequest config
            >>= recordAuthenticatedUser req
            >>= workflow
            |> createResponse req config log
        with exn -> handle req exn


    /// FUNCTION WORKFLOWS 

    /// (Anonymous) A function that simply returns, "Pong!" 
    [<FunctionName("Options")>]
    let options
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "{*url}")>] req) =
        optionsResponse req config

    [<FunctionName("PingGet")>]
    let ping
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")>] req) =
        "pong" |> jsonResponse req "*" Status.OK

    /// (Anonymous) Exchanges a UAA OAuth code for an application-scoped JWT
    [<FunctionName("AuthGet")>]
    let auth
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth")>] req) =

        // workflow partials
        let createUaaTokenRequest = createUaaTokenRequest config
        let requestTokenFromUaa = postAsync<UaaResponse> config.OAuth2TokenUrl
        let resolveAppUserId claims = data.TryGetPersonId claims.UserName
        let encodeAppJwt = encodeAppJwt config.JwtSecret (now().AddHours(8.))

        // workflow definition
        let workflow req =  
            req
            |> getQueryParam "oauth_code"
            >>= createUaaTokenRequest
            >>= await requestTokenFromUaa
            >>= decodeUaaJwt
            >>= recordAuthenticatedUser req
            >>= await resolveAppUserId
            >>= encodeAppJwt

        req |> execAnonymousWorkflow workflow

    /// (Authenticated) Get a user profile for a given user 'id'
    [<FunctionName("UserGetId")>]
    let profileGet
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{id}")>] req, id) =
        let workflow _ = id |> await data.GetProfile
        req |> execAuthenticatedWorkflow workflow

    /// (Authenticated) Get a user profile associated with the JWT in the request Authorization header.
    [<FunctionName("UserGetMe")>]
    let profileGetMe
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")>] req) = 
        let workflow user = user.UserId |> await data.GetProfile
        req |> execAuthenticatedWorkflow workflow

    /// (Authenticated) Get all users, departments, and units that match a 'term' query.
    [<FunctionName("SearchGet")>]
    let searchSimpleGet
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "search")>] req) =
        let workflow _ = 
            getQueryParam "term" req
            >>= await data.GetSimpleSearchByTerm
        req |> execAuthenticatedWorkflow workflow

    /// (Authenticated) Get all units.
    [<FunctionName("UnitGetAll")>]
    let unitGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units")>] req) =
        let workflow _ = () |> await data.GetUnits
        req |> execAuthenticatedWorkflow workflow

    /// (Authenticated) Get a unit profile for a given unit 'id'.
    [<FunctionName("UnitGetId")>]
    let unitGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{id}")>] req, id) =
        let workflow _ = id |> await data.GetUnit
        req |> execAuthenticatedWorkflow workflow
            
    /// (Authenticated) Get all departments.
    [<FunctionName("DepartmentGetAll")>]
    let departmentGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments")>] req) =
        let workflow _ = () |> await data.GetDepartments
        req |> execAuthenticatedWorkflow workflow

    /// (Authenticated) Get a department profile for a given department 'id'.
    [<FunctionName("DepartmentGetId")>]
    let departmentGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{id}")>] req, id) =
        let workflow _ = id |> await data.GetDepartment
        req |> execAuthenticatedWorkflow workflow