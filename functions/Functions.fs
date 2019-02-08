// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause    

namespace Functions

open Types
open Http
open Api
open Jwt
open Util
open Logging
open Fakes

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
open Swashbuckle.AspNetCore.Filters
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
    let anonymous workflow successStatus req =
        try
            req
            |> timestamp
            |> workflow
            |> createResponse req config log successStatus
        with exn -> handle req exn

    /// Execute a workflow for an authenticated user and return a response.
    let authenticate workflow successStatus req =
        try
            req
            |> timestamp
            |> authenticateRequest config
            >>= recordAuthenticatedUser req
            >>= workflow
            |> createResponse req config log successStatus
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
    [<SwaggerIgnore>]
    let ping
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")>] req) =
        "pong" |> jsonResponse req "*" Status.OK



    // *****************
    // ** Authentication
    // *****************

    [<FunctionName("AuthGet")>]
    [<SwaggerOperation(Summary="Get OAuth JWT", Description="Exchanges a UAA OAuth code for an application-scoped JWT. The JWT is required to make authenticated requests to this API.", Tags=[|"Authentication"|])>]
    [<SwaggerResponse(200, Type=typeof<JwtResponse>)>]
    let auth
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth")>] req) =

        // workflow partials
        let createUaaTokenRequest = createUaaTokenRequest config
        let requestTokenFromUaa = postAsync<JwtResponse> config.OAuth2TokenUrl
        let resolveAppUserId claims = data.TryGetPersonId claims.UserName
        let encodeAppJwt = encodeAppJwt config.JwtSecret (now().AddHours(8.))

        // workflow definition
        let workflow req =  
            req
            |> queryParam "oauth_code"
            >>= createUaaTokenRequest
            >>= await requestTokenFromUaa
            >>= decodeUaaJwt
            >>= recordAuthenticatedUser req
            >>= await resolveAppUserId
            >>= encodeAppJwt

        req |> anonymous workflow Status.OK



    // *****************
    // ** People
    // *****************

    [<FunctionName("PeopleGetAll")>]
    [<SwaggerOperation(Summary="List all people", Tags=[|"People"|])>]
    [<SwaggerResponse(200, Type=typeof<seq<Person>>)>]
    [<OptionalQueryParameter("q", typeof<string>)>]
    let peopleGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people")>] req) =
        let workflow _ = 
            req
            |> tryQueryParam "q"
            |> await data.GetPeople
        req |> authenticate workflow Status.OK

    [<FunctionName("PeopleGetById")>]
    [<SwaggerOperation(Summary="Find a person by ID", Tags=[|"People"|])>]
    [<SwaggerResponse(200, Type=typeof<Person>)>]
    let peopleGetById
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{personId}")>] req, personId) =
        let workflow _ = await data.GetPerson personId
        req |> authenticate workflow Status.OK

    [<FunctionName("PeopleGetAllMemberships")>]
    [<SwaggerOperation(Summary="Find a person's unit memberships", Tags=[|"People"|])>]
    [<SwaggerResponse(200, Type=typeof<seq<UnitMember>>)>]
    let peopleGetAllMemberships
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{personId}/memberships")>] req, personId) =
        let workflow _ = await data.GetPersonMemberships personId
        req |> authenticate workflow Status.OK

    [<FunctionName("PeopleGetMembershipById")>]
    [<SwaggerOperation(Summary="Find a person's unit membership by ID", Tags=[|"People"|])>]
    [<SwaggerResponse(200, Type=typeof<Unit>)>]
    let peopleGetMembershipById
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{personId}/memberships/{membershipId}")>] req, personId:PersonId, membershipId) =
        let workflow _ = await data.GetMembership membershipId
        req |> authenticate workflow Status.OK



    // *****************
    // ** Units
    // *****************

    [<FunctionName("UnitGetAll")>]
    [<SwaggerOperation(Summary="List all top-level IT units.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, Type=typeof<seq<Unit>>)>]
    [<OptionalQueryParameter("q", typeof<string>)>]
    let unitGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units")>] req) =
        let workflow _ = 
            req
            |> tryQueryParam "q"
            |> await data.GetUnits
        req |> authenticate workflow Status.OK

    [<FunctionName("UnitGetId")>]
    [<SwaggerOperation(Summary="Find a unit by ID.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, Type=typeof<Unit>)>]
    let unitGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}")>] req, unitId) =
        let workflow _ = await data.GetUnit unitId
        req |> authenticate workflow Status.OK
            
    [<FunctionName("UnitGetAllMembers")>]
    [<SwaggerOperation(Summary="Get all unit members", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, Type=typeof<seq<UnitMember>>)>]
    let unitGetAllMembers
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/members")>] req, unitId) =
        let workflow _ = await data.GetUnitMembers unitId
        req |> authenticate workflow Status.OK

    [<FunctionName("UnitGetMember")>]
    [<SwaggerOperation(Summary="Get a unit member", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, Type=typeof<UnitMember>)>]
    let unitGetMember
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/members/{membershipId}")>] req, unitId:UnitId, membershipId) =
        let workflow _ = await data.GetMembership membershipId
        req |> authenticate workflow Status.OK


    [<FunctionName("UnitPost")>]
    [<SwaggerOperation(Summary="Create a unit.", Tags=[|"Units"|])>]
    [<SwaggerResponse(201, Type=typeof<Unit>)>]
    let unitPost
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "units")>] req) =
        let workflow user = 
            deserializeBody<Unit> req
            >>= await data.CreateUnit
        req |> authenticate workflow Status.Created

    [<FunctionName("UnitPut")>]
    [<SwaggerOperation(Summary="Update a unit.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, Type=typeof<Unit>)>]
    let unitPut
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "units/{unitId}")>] req, unitId) =
        let workflow user = 
            deserializeBody<Unit> req
            >>= await (data.UpdateUnit unitId)
        req |> authenticate workflow Status.OK

    [<FunctionName("UnitDelete")>]
    [<SwaggerOperation(Summary="Delete a unit.", Tags=[|"Units"|])>]
    [<SwaggerResponse(204)>]
    let unitDelete
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "units/{unitId}")>] req:HttpRequestMessage, unitId) =
        let workflow _ = await data.DeleteUnit unitId
        ()
        //req |> authenticate workflow Status.NoContent



    // *****************
    // ** Departments
    // *****************

    [<FunctionName("DepartmentGetAll")>]
    [<SwaggerOperation(Summary="List all departments.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, Type=typeof<seq<Department>>)>]
    [<OptionalQueryParameter("q", typeof<string>)>]
    let departmentGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments")>] req) =
        let workflow _ = 
            req
            |> tryQueryParam "q"
            |> await data.GetDepartments
        req |> authenticate workflow Status.OK

    [<FunctionName("DepartmentGetId")>]
    [<SwaggerOperation(Summary="Find a department by ID.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, Type=typeof<Department>)>]
    let departmentGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}")>] req, departmentId) =
        let workflow _ = await data.GetDepartment departmentId
        req |> authenticate workflow Status.OK

    [<FunctionName("DepartmentGetAllMemberUnits")>]
    [<SwaggerOperation(Summary="List a department's member units.", Description="A member unit contains people that have an HR relationship with the department.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, Type=typeof<seq<Unit>>)>]
    let departmentGetMemberUnits
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}/memberUnits")>] req, departmentId) =
        let workflow _ = await data.GetDepartmentMemberUnits departmentId
        req |> authenticate workflow Status.OK

    [<FunctionName("DepartmentGetAllSupportingUnits")>]
    [<SwaggerOperation(Summary="List a department's member units.", Description="A member unit contains people that have an HR relationship with the department.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, Type=typeof<seq<Unit>>)>]
    let departmentGetSupportingUnits
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}/supportingUnits")>] req, departmentId) =
        let workflow _ = await data.GetDepartmentSupportingUnits departmentId
        req |> authenticate workflow Status.OK

