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
    let openApiSpec = lazy (generateOpenAPISpec())
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

    /// Temporary: a list of IT people admins.
    let isAdmin (user:JwtClaims) =
        let admins = [ "jhoerr"; "kendjone"; "jerussel"; "brrund"; "mattzink"; "johndoe" ]
        admins |> List.contains user.UserName

    /// Temporary: if this user is an admin, allow them to modify the resource.
    let authorize (user:JwtClaims) =
        if isAdmin user
        then ok user
        else fail (Status.Forbidden, "You are not authorized to modify this resource.")

    /// Temporary: if this user is an admin, give them read/write access, else read-only.
    let determineUserPermissions user a =
        if isAdmin user
        then ok (a, [GET; POST; PUT; DELETE])
        else ok (a, [GET])

    /// Execute a workflow that authenticates and authorizes the user for modifying the selected resource.
    let authorizeWrite workflow successStatus req =
        try
            req
            |> timestamp
            |> authenticateRequest config
            >>= recordAuthenticatedUser req
            >>= authorize
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
        new StringContent(openApiSpec.Value) 
        |> contentResponse req "*" Status.OK None

    [<FunctionName("PingGet")>]
    [<SwaggerIgnore>]
    let ping
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")>] req) =
        new StringContent("Pong!") 
        |> contentResponse req "*" Status.OK None



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
            >>= fun user -> ok (user, [GET])

        req |> anonymous workflow Status.OK



    // *****************
    // ** People
    // *****************

    // [<FunctionName("PeopleGetAll")>]
    // [<SwaggerOperation(Summary="List all people", Tags=[|"People"|])>]
    // [<SwaggerResponse(200, Type=typeof<seq<Person>>)>]
    // [<OptionalQueryParameter("q", typeof<string>)>]
    // let peopleGetAll
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people")>] req) =
    //     let workflow user = 
    //         req
    //         |> tryQueryParam "q"
    //         |> await data.GetPeople
    //         >>= determineUserPermissions user
    //     req |> authenticate workflow Status.OK

    // [<FunctionName("PeopleGetById")>]
    // [<SwaggerOperation(Summary="Find a person by ID", Tags=[|"People"|])>]
    // [<SwaggerResponse(200, Type=typeof<Person>)>]
    // let peopleGetById
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{personId}")>] req, personId) =
    //     let workflow user = 
    //         await data.GetPerson personId
    //         >>= determineUserPermissions user
    //     req |> authenticate workflow Status.OK

    // [<FunctionName("PeopleGetAllMemberships")>]
    // [<SwaggerOperation(Summary="Find a person's unit memberships", Tags=[|"People"|])>]
    // [<SwaggerResponse(200, Type=typeof<seq<UnitMember>>)>]
    // let peopleGetAllMemberships
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{personId}/memberships")>] req, personId) =
    //     let workflow user = 
    //         await data.GetPersonMemberships personId
    //         >>= determineUserPermissions user
    //     req |> authenticate workflow Status.OK


    // *****************
    // ** Units
    // *****************

    // [<FunctionName("UnitGetAll")>]
    // [<SwaggerOperation(Summary="List all top-level IT units.", Tags=[|"Units"|])>]
    // [<SwaggerResponse(200, Type=typeof<seq<Unit>>)>]
    // [<OptionalQueryParameter("q", typeof<string>)>]
    // let unitGetAll
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units")>] req) =
    //     let workflow user = 
    //         req
    //         |> tryQueryParam "q"
    //         |> await data.GetUnits
    //         >>= determineUserPermissions user
    //     req |> authenticate workflow Status.OK

    // [<FunctionName("UnitGetId")>]
    // [<SwaggerOperation(Summary="Find a unit by ID.", Tags=[|"Units"|])>]
    // [<SwaggerResponse(200, Type=typeof<Unit>)>]
    // let unitGetId
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}")>] req, unitId) =
    //     let workflow user = 
    //         await data.GetUnit unitId
    //         >>= determineUserPermissions user
    //     req |> authenticate workflow Status.OK
            
    // [<FunctionName("UnitPost")>]
    // [<SwaggerOperation(Summary="Create a unit.", Tags=[|"Units"|])>]
    // let unitPost
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "units")>] req) =
    //     let workflow user =
    //         deserializeBody<Unit> req
    //         >>= await data.CreateUnit
    //         >>= determineUserPermissions user
    //     req |> authorizeWrite workflow Status.Created

    // [<FunctionName("UnitPut")>]
    // [<SwaggerOperation(Summary="Update a unit.", Tags=[|"Units"|])>]
    // [<SwaggerResponse(200, Type=typeof<Unit>)>]
    // let unitPut
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "units/{unitId}")>] req, unitId) =
    //     let workflow user = 
    //         deserializeBody<Unit> req
    //         >>= await (data.UpdateUnit unitId)
    //         >>= determineUserPermissions user
    //     req |> authorizeWrite workflow Status.OK

    // [<FunctionName("UnitDelete")>]
    // [<SwaggerOperation(Summary="Delete a unit.", Tags=[|"Units"|])>]
    // [<SwaggerResponse(204)>]
    // let unitDelete
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "units/{unitId}")>] req, unitId) =
    //     let workflow user = 
    //         await data.DeleteUnit unitId
    //         >>= determineUserPermissions user
    //     req |> authorizeWrite workflow Status.NoContent

    // [<FunctionName("UnitGetAllMembers")>]
    // [<SwaggerOperation(Summary="Get all unit members", Tags=[|"Units"|])>]
    // [<SwaggerResponse(200, Type=typeof<seq<UnitMember>>)>]
    // let unitGetAllMembers
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/members")>] req, unitId) =
    //     let workflow user = 
    //         await data.GetUnitMembers unitId
    //         >>= determineUserPermissions user
    //     req |> authenticate workflow Status.OK

    // [<FunctionName("UnitGetAllSupportedDepartments")>]
    // [<SwaggerOperation(Summary="Get all departments supported by a unit", Tags=[|"Units"|])>]
    // [<SwaggerResponse(200, Type=typeof<seq<UnitMember>>)>]
    // let unitGetAllSupportedDepartments
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/supportedDepartments")>] req, unitId) =
    //     let workflow user = 
    //         await data.GetUnitSupportedDepartments unitId
    //         >>= determineUserPermissions user
    //     req |> authenticate workflow Status.OK

    // [<FunctionName("UnitGetAllChildren")>]
    // [<SwaggerOperation(Summary="Get all children of a unit", Tags=[|"Units"|])>]
    // [<SwaggerResponse(200, Type=typeof<seq<Unit>>)>]
    // let unitGetAllChildren
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/children")>] req, unitId) =
    //     let workflow user = 
    //         await data.GetUnitChildren unitId
    //         >>= determineUserPermissions user
    //     req |> authenticate workflow Status.OK


    // *******************
    // ** Unit Memberships
    // *******************

    // [<FunctionName("GetAllMembership")>]
    // [<SwaggerOperation(Summary="Find a unit membership by ID", Tags=[|"Unit Memberships"|])>]
    // [<SwaggerResponse(200, Type=typeof<seq<UnitMember>>)>]
    // let getMemberships
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "memberships")>] req) =
    //     let workflow user = 
    //         await data.GetMemberships ()
    //         >>= determineUserPermissions user
    //     req |> authenticate workflow Status.OK

    // [<FunctionName("GetMembershipById")>]
    // [<SwaggerOperation(Summary="Find a unit membership by ID", Tags=[|"Unit Memberships"|])>]
    // [<SwaggerResponse(200, Type=typeof<Unit>)>]
    // let getMembershipById
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "memberships/{membershipId}")>] req, membershipId) =
    //     let workflow user = 
    //         await data.GetMembership membershipId
    //         >>= determineUserPermissions user
    //     req |> authenticate workflow Status.OK

    // let hasValidUnitId (membership:UnitMember) = 
    //     if (membership.UnitId > 0)
    //     then ok membership
    //     else fail (Status.BadRequest, "The unit ID must be greater than zero.")

    // let validateUnitExists (membership:UnitMember) = async {
    //     let! lookupResult = data.GetUnit membership.UnitId
    //     return 
    //         match lookupResult with
    //         | Ok(_,_) -> ok membership
    //         | Bad(_) -> fail (Status.NotFound, "No unit exists with that ID.")
    // }

    // let validateUnitMembershipRequest membership = 
    //     membership
    //     |> hasValidUnitId
    //     >>= await validateUnitExists

    // [<FunctionName("PostMembership")>]
    // [<SwaggerOperation(Summary="Create a unit member.", Tags=[|"Unit Memberships"|])>]
    // [<SwaggerResponse(201, Type=typeof<UnitMember>)>]
    // let unitPostMember
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "memberships")>] req) =
    //     let workflow user = 
    //         deserializeBody<UnitMember> req
    //         >>= validateUnitMembershipRequest
    //         >>= await data.CreateMembership
    //         >>= determineUserPermissions user
    //     req |> authorizeWrite workflow Status.Created

    // [<FunctionName("PutMembership")>]
    // [<SwaggerOperation(Summary="Update a unit member.", Tags=[|"Unit Memberships"|])>]
    // [<SwaggerResponse(200, Type=typeof<UnitMember>)>]
    // let unitPutMember
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "memberships/{membershipId}")>] req, membershipId) =
    //     let workflow user = 
    //         deserializeBody<UnitMember> req
    //         >>= validateUnitMembershipRequest
    //         >>= await (data.UpdateMembership membershipId)
    //         >>= determineUserPermissions user
    //     req |> authorizeWrite workflow Status.OK
  
    // [<FunctionName("DeleteMembership")>]
    // [<SwaggerOperation(Summary="Delete a unit member.", Tags=[|"Unit Memberships"|])>]
    // [<SwaggerResponse(200, Type=typeof<UnitMember>)>]
    // let unitDeleteMember
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "memberships/{membershipId}")>] req, membershipId) =
    //     let workflow user = 
    //         await data.DeleteMembership membershipId
    //         >>= determineUserPermissions user
    //     req |> authorizeWrite workflow Status.NoContent


    // *****************
    // ** Departments
    // *****************

    // [<FunctionName("DepartmentGetAll")>]
    // [<SwaggerOperation(Summary="List all departments.", Tags=[|"Departments"|])>]
    // [<SwaggerResponse(200, Type=typeof<seq<Department>>)>]
    // [<OptionalQueryParameter("q", typeof<string>)>]
    // let departmentGetAll
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments")>] req) =
    //     let workflow user = 
    //         req
    //         |> tryQueryParam "q"
    //         |> await data.GetDepartments
    //         >>= determineUserPermissions user
    //     req |> authenticate workflow Status.OK

    // [<FunctionName("DepartmentGetId")>]
    // [<SwaggerOperation(Summary="Find a department by ID.", Tags=[|"Departments"|])>]
    // [<SwaggerResponse(200, Type=typeof<Department>)>]
    // let departmentGetId
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}")>] req, departmentId) =
    //     let workflow user = 
    //         await data.GetDepartment departmentId
    //         >>= determineUserPermissions user
    //     req |> authenticate workflow Status.OK

    // [<FunctionName("DepartmentGetAllMemberUnits")>]
    // [<SwaggerOperation(Summary="List a department's member units.", Description="A member unit contains people that have an HR relationship with the department.", Tags=[|"Departments"|])>]
    // [<SwaggerResponse(200, Type=typeof<seq<Unit>>)>]
    // let departmentGetMemberUnits
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}/memberUnits")>] req, departmentId) =
    //     let workflow user = 
    //         await data.GetDepartmentMemberUnits departmentId
    //         >>= determineUserPermissions user
    //     req |> authenticate workflow Status.OK

    // [<FunctionName("DepartmentGetAllSupportingUnits")>]
    // [<SwaggerOperation(Summary="List a department's supporting units.", Description="A member unit contains people that have an HR relationship with the department.", Tags=[|"Departments"|])>]
    // [<SwaggerResponse(200, Type=typeof<seq<Unit>>)>]
    // let departmentGetSupportingUnits
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}/supportingUnits")>] req, departmentId) =
    //     let workflow user = 
    //         await data.GetDepartmentSupportingUnits departmentId
    //         >>= determineUserPermissions user    
    //     req |> authenticate workflow Status.OK


    // ************************
    // ** Support Relationships
    // ************************

    [<FunctionName("SupportRelationshipsGetAll")>]
    [<SwaggerOperation(Summary="List all unit-department support relationships.", Tags=[|"Support Relationships"|])>]
    [<SwaggerResponse(200, Type=typeof<SupportRelationship>)>]
    // [<SwaggerResponseExample(200, typeof<SupportRelationshipsResponseExample>)>]
    let supportRelationshipsGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "supportRelationships")>] req) =
        let workflow user = 
            await data.GetSupportRelationships ()
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("SupportRelationshipsGetId")>]
    [<SwaggerOperation(Summary="Find a unit-department support relationships by ID", Tags=[|"Support Relationships"|])>]
    [<SwaggerResponse(200, Type=typeof<SupportRelationship seq>)>]
    let supportRelationshipsGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "supportRelationships/{relationshipId}")>] req, relationshipId) =
        let workflow user = 
            await data.GetSupportRelationship relationshipId
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("SupportRelationshipsCreate")>]
    [<SwaggerOperation(Summary="Create a unit-department support relationship", Tags=[|"Support Relationships"|])>]
    [<SwaggerRequestExample(typeof<SupportRelationshipRequest>, typeof<SupportRelationshipRequestExample>)>]
    [<SwaggerResponse(201, Type=typeof<SupportRelationship>)>]
    // [<SwaggerResponseExample(201, typeof<SupportRelationshipResponseExample>)>]
    let supportRelationshipsCreate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "supportRelationships")>] req) =
        let workflow user = 
            deserializeBody<SupportRelationship> req
            >>= await data.CreateSupportRelationship          
            >>= determineUserPermissions user
        req |> authorizeWrite workflow Status.Created

    // [<FunctionName("SupportRelationshipsUpdate")>]
    // [<SwaggerOperation(Summary="Update a unit-department support relationship", Tags=[|"Support Relationships"|])>]
    // [<SwaggerRequestExample(typeof<SupportRelationshipRequest>, typeof<SupportRelationshipRequestExample>)>]
    // [<SwaggerResponseExample(200, typeof<SupportRelationshipResponseExample>)>]
    // let supportRelationshipsUpdate
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "supportRelationships/{relationshipId}")>] req, relationshipId) =
    //     let workflow user = 
    //         deserializeBody<SupportRelationship> req
    //         >>= await (data.UpdateSupportRelationship relationshipId)
    //         >>= determineUserPermissions user
    //     req |> authorizeWrite workflow Status.OK

    // [<FunctionName("SupportRelationshipsDelete")>]
    // [<SwaggerOperation(Summary="Delete a unit-department support relationship", Tags=[|"Support Relationships"|])>]
    // [<SwaggerResponse(204)>]
    // let supportRelationshipsDelete
    //     ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "supportRelationships/{relationshipId}")>] req, relationshipId) =
    //     let workflow user = 
    //         await data.DeleteSupportRelationship relationshipId
    //         >>= determineUserPermissions user
    //     req |> authorizeWrite workflow Status.NoContent
