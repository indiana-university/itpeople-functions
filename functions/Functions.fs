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
open System
open System.Net.Http
open System.Reflection
open Microsoft.Azure.WebJobs.Extensions.Http
open Microsoft.Extensions.DependencyInjection

open Microsoft.AspNetCore.Mvc
open Swashbuckle.AspNetCore.Swagger
open Swashbuckle.AspNetCore.Annotations
open Swashbuckle.AspNetCore.AzureFunctions.Annotations

/// This module defines the bindings and triggers for all functions in the project
module Functions =    

    // DEPENDENCY RESOLUTION

    /// Dependencies are resolved once at startup.
    let openApiSpec = generateOpenAPISpec()
    let config = getConfiguration()
    let data = getData config
    let log = createLogger config


    // FUNCTION WORKFLOW HELPERS 

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


    // FUNCTION WORKFLOWS 

    [<FunctionName("Options")>]
    [<SwaggerIgnore>]
    let options
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "{*url}")>] req) =
        optionsResponse req config

    [<FunctionName("OpenAPI")>]
    [<SwaggerIgnore>]
    let openapi
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "openapi.json")>] req) =
        new StringContent(openApiSpec) |> contentResponse req "*" Status.OK

    [<FunctionName("PingGet")>]
    [<SwaggerOperation(Description="A function that simply returns, 'pong'", Tags=[||])>]
    [<SwaggerResponse(200, "A pong response.", typeof<string>)>]
    let ping
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")>] req) =
        "pong" |> jsonResponse req "*" Status.OK

    [<FunctionName("AuthGet")>]
    [<SwaggerOperation(Description="Exchanges a UAA OAuth code for an application-scoped JWT", Tags=[||])>]
    [<SwaggerResponse(200, "Exchanges an OAuth code for a JWT.", typeof<UaaResponse>)>]
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

    [<FunctionName("UserGetId")>]
    [<SwaggerOperation(Description="Get a user profile for a given user 'id'", Tags=[||])>]
    [<SwaggerResponse(200, "A person profile.", typeof<PersonDto>)>]
    let profileGet
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{id}")>] req, id) =
        let workflow _ = id |> await data.GetProfile
        req |> execAuthenticatedWorkflow workflow

    [<FunctionName("UserGetMe")>]
    [<SwaggerOperation(Description="Get a user profile associated with the JWT in the request Authorization header.", Tags=[||])>]
    [<SwaggerResponse(200, "The profile of the authenticated user.", typeof<PersonDto>)>]
    let profileGetMe
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "me")>] req) = 
        let workflow user = user.UserId |> await data.GetProfile
        req |> execAuthenticatedWorkflow workflow

    [<FunctionName("SearchGet")>]
    [<SwaggerOperation(Description="Get all users, departments, and units that match a 'term' query.", Tags=[||])>]
    [<SwaggerResponse(200, "A search result of people, units, and departments.", typeof<SimpleSearch>)>]
    let searchSimpleGet
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "search")>] req) =
        let workflow _ = 
            getQueryParam "term" req
            >>= await data.GetSimpleSearchByTerm
        req |> execAuthenticatedWorkflow workflow

    [<FunctionName("UnitGetAll")>]
    [<SwaggerOperation(Description="Get all IT units.", Tags=[||])>]
    [<SwaggerResponse(200, "All top-level IT units.", typeof<seq<Unit>>)>]
    let unitGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units")>] req) =
        let workflow _ = () |> await data.GetUnits
        req |> execAuthenticatedWorkflow workflow

    [<FunctionName("UnitGetId")>]
    [<SwaggerOperation(Description="Get a unit profile for a given unit 'id'.", Tags=[||])>]
    [<SwaggerResponse(200, "A single IT unit.", typeof<UnitDto>)>]
    let unitGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{id}")>] req, id) =
        let workflow _ = id |> await data.GetUnit
        req |> execAuthenticatedWorkflow workflow
            
    [<FunctionName("DepartmentGetAll")>]
    [<SwaggerOperation(Description="Get all departments.", Tags=[||])>]
    [<SwaggerResponse(200, "All departments.", typeof<seq<Department>>)>]
    let departmentGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments")>] req) =
        let workflow _ = () |> await data.GetDepartments
        req |> execAuthenticatedWorkflow workflow

    [<FunctionName("DepartmentGetId")>]
    [<SwaggerOperation(Description="Get a department profile for a given department 'id'.", Tags=[||])>]
    [<SwaggerResponse(200, "A single department.", typeof<DepartmentDto>)>]
    let departmentGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{id}")>] req, id) =
        let workflow _ = id |> await data.GetDepartment
        req |> execAuthenticatedWorkflow workflow