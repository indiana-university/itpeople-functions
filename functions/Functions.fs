// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause    

namespace Functions

open Types
open Http
open Api
open Jwt
open Util
open Logging
open Validation
open Fakes

open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs
open System
open System.Net.Http
open Microsoft.Azure.WebJobs.Extensions.Http

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

    let authorizeUnitModification (user:JwtClaims) id model  =
        if isAdmin user
        then ok model
        else fail (Status.Forbidden, "You are not authorized to modify this resource.")

    let inline authorizeCorrespondingUnitModification user model = 
        authorizeUnitModification user (unitId model) model

    /// Temporary: if this user is an admin, give them read/write access, else read-only.
    let determineUserPermissions user a =
        if isAdmin user
        then ok (a, [GET; POST; PUT; DELETE])
        else ok (a, [GET])

    // VALIDATION 


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
        let (content, status) = 
            try (new StringContent(openApiSpec.Value), Status.OK)
            with exn -> (new StringContent(exn.ToString()), Status.InternalServerError)
        contentResponse req "*" status None content

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
    [<SwaggerResponse(200, "A JWT access token scoped for the IT People API.", typeof<JwtResponse>)>]
    [<SwaggerResponse(400, "The provided code was missing, invalid, or expired.", typeof<ErrorModel>)>]
    let auth
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth")>] req) =

        // workflow partials
        let createUaaTokenRequest = createUaaTokenRequest config
        let requestTokenFromUaa = postAsync<JwtResponse> config.OAuth2TokenUrl
        let resolveAppUserId claims = data.People.TryGetId claims.UserName
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


    [<FunctionName("PeopleGetAll")>]
    [<SwaggerOperation(Summary="List all people", Description="Search for people by name and/or username (netid).", Tags=[|"People"|])>]
    [<SwaggerResponse(200, "A collection of person records.", typeof<seq<Person>>)>]
    [<OptionalQueryParameter("q", typeof<string>)>]
    let peopleGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people")>] req) =
        let workflow user = 
            req
            |> tryQueryParam "q"
            |> await data.People.GetAll
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("PeopleGetById")>]
    [<SwaggerOperation(Summary="Find a person by ID", Tags=[|"People"|])>]
    [<SwaggerResponse(200, "A person record.", typeof<Person>)>]
    [<SwaggerResponse(404, "No person was found with the ID provided.", typeof<ErrorModel>)>]
    let peopleGetById
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{personId}")>] req, personId) =
        let workflow user = 
            await data.People.Get personId
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("PeopleGetAllMemberships")>]
    [<SwaggerOperation(Summary="List a person's unit memberships", Description="List all units for which this person does IT work.", Tags=[|"People"|])>]
    [<SwaggerResponse(200, "A collection of units of which this person is a member.", typeof<seq<UnitMember>>)>]
    [<SwaggerResponse(404, "No person was found with the ID provided.", typeof<ErrorModel>)>]
    let peopleGetAllMemberships
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{personId}/memberships")>] req, personId) =
        let workflow user = 
            await data.People.Get personId
            >>= (fun p -> await data.People.GetMemberships p.Id)
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK


    // *****************
    // ** Units
    // *****************

    let setUnitId id (a:Unit) = ok { a with Id=id }

    let unitValidator = unitValidator(data)

    [<FunctionName("UnitGetAll")>]
    [<SwaggerOperation(Summary="List all IT units.", Description="Search for IT units by name and/or description. If no search term is provided, lists all top-level IT units." , Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A collection of unit records.", typeof<seq<Unit>>)>]
    [<OptionalQueryParameter("q", typeof<string>)>]
    let unitGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units")>] req) =
        let workflow user = 
            req
            |> tryQueryParam "q"
            |> await data.Units.GetAll
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("UnitGetId")>]
    [<SwaggerOperation(Summary="Find a unit by ID.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A unit record.", typeof<Unit>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    let unitGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}")>] req, unitId) =
        let workflow user = 
            await data.Units.Get unitId
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK
            
    [<FunctionName("UnitPost")>]
    [<SwaggerOperation(Summary="Create a unit.", Tags=[|"Units"|])>]
    [<SwaggerRequestExample(typeof<UnitRequest>, typeof<UnitRequestExample>)>]
    [<SwaggerResponse(201, "A record of the newly created unit.", typeof<Unit>)>]
    [<SwaggerResponse(400, "The request body is malformed, or the unit name is missing.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The request body specifies a name that is already in use by another unit.", typeof<ErrorModel>)>]
    let unitPost
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "units")>] req) =
        let workflow user =
            deserializeBody<Unit> req
            >>= setUnitId 0      
            >>= authorizeUnitModification user 0           
            >>= await unitValidator.ValidForCreate
            >>= await data.Units.Create
            >>= determineUserPermissions user
        req |> authenticate workflow Status.Created

    [<FunctionName("UnitPut")>]
    [<SwaggerOperation(Summary="Update a unit.", Tags=[|"Units"|])>]
    [<SwaggerRequestExample(typeof<UnitRequest>, typeof<UnitRequestExample>)>]
    [<SwaggerResponse(200, "A record of the updated unit", typeof<Unit>)>]
    [<SwaggerResponse(400, "The request body is malformed, or the unit name is missing.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You do not have permission to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The request body specifies a name that is already in use by another unit.", typeof<ErrorModel>)>]
    let unitPut
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "units/{unitId}")>] req, unitId) =
        let workflow user = 
            deserializeBody<Unit> req      
            >>= setUnitId unitId      
            >>= authorizeUnitModification user unitId
            >>= await unitValidator.ValidForUpdate
            >>= await data.Units.Update
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("UnitDelete")>]
    [<SwaggerOperation(Summary="Delete a unit.", Tags=[|"Units"|])>]
    [<SwaggerResponse(204)>]
    [<SwaggerResponse(403, "You do not have permission to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    let unitDelete
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "units/{unitId}")>] req, unitId) =
        let workflow user =
            await unitValidator.ValidEntity unitId
            >>= authorizeUnitModification user unitId
            >>= fun _ -> await data.Units.Delete unitId
            >>= determineUserPermissions user
        req |> authenticate workflow Status.NoContent

    [<FunctionName("UnitGetAllMembers")>]
    [<SwaggerOperation(Summary="List all unit members", Description="List all people who do IT work for this unit along with any vacant positions.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A collection of membership records.", typeof<seq<UnitMember>>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    let unitGetAllMembers
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/members")>] req, unitId) =
        let workflow user =
            await unitValidator.ValidEntity unitId
            >>= await data.Units.GetMembers
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("UnitGetAllSupportedDepartments")>]
    [<SwaggerOperation(Summary="List all supported departments", Description="List all departments that receive IT support from this unit.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A collection of department records.", typeof<seq<UnitMember>>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    let unitGetAllSupportedDepartments
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/supportedDepartments")>] req, unitId) =
        let workflow user = 
            await unitValidator.ValidEntity unitId
            >>= await data.Units.GetSupportedDepartments
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("UnitGetAllChildren")>]
    [<SwaggerOperation(Summary="List all unit children", Description="List all units that fall below this unit in an organizational hierarchy.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A collection of unit records.", typeof<seq<Unit>>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    let unitGetAllChildren
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/children")>] req, unitId) =
        let workflow user = 
            await unitValidator.ValidEntity unitId
            >>= await data.Units.GetChildren
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK


    // *******************
    // ** Unit Memberships
    // *******************

    let membershipValidator = membershipValidator(data)
    let setMembershipId id (a:UnitMember) = ok { a with Id=id }

    [<FunctionName("MemberGetAll")>]
    [<SwaggerOperation(Summary="List all unit memberships", Tags=[|"Unit Memberships"|])>]
    [<SwaggerResponse(200, "A collection of unit membership records", typeof<seq<UnitMember>>)>]
    let memberGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "memberships")>] req) =
        let workflow user = 
            await data.Memberships.GetAll ()
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("MemberGetById")>]
    [<SwaggerOperation(Summary="Find a unit membership by ID", Tags=[|"Unit Memberships"|])>]
    [<SwaggerResponse(200, "A unit membership record", typeof<UnitMember>)>]
    [<SwaggerResponse(404, "No membership was found with the ID provided.", typeof<ErrorModel>)>]
    let memberGetById
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "memberships/{membershipId}")>] req, membershipId) =
        let workflow user = 
            await data.Memberships.Get membershipId
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("MemberCreate")>]
    [<SwaggerOperation(Summary="Create a unit membership.", Tags=[|"Unit Memberships"|])>]
    [<SwaggerRequestExample(typeof<UnitMemberRequest>, typeof<MembershipRequestExample>)>]
    [<SwaggerResponse(201, "The newly created unit membership record", typeof<UnitMember>)>]
    [<SwaggerResponse(400, "The request body was malformed, the unitId field was missing, or the specified unit does not exist.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The provided person is already a member of the provided unit.", typeof<ErrorModel>)>]
    let memberCreate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "memberships")>] req) =
        let workflow user = 
            deserializeBody<UnitMember> req
            >>= setMembershipId 0
            >>= authorizeCorrespondingUnitModification user
            >>= await membershipValidator.ValidForCreate
            >>= await data.Memberships.Create
            >>= determineUserPermissions user
        req |> authenticate workflow Status.Created

    [<FunctionName("MemberUpdate")>]
    [<SwaggerOperation(Summary="Update a unit membership.", Tags=[|"Unit Memberships"|])>]
    [<SwaggerRequestExample(typeof<UnitMemberRequest>, typeof<MembershipRequestExample>)>]
    [<SwaggerResponse(200, "The update unit membership record.", typeof<UnitMember>)>]
    [<SwaggerResponse(400, "The request body was malformed, the unitId field was missing, or the specified unit does not exist.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No membership was found with the ID provided.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The provided person is already a member of the provided unit.", typeof<ErrorModel>)>]
    let memberUpdate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "memberships/{membershipId}")>] req, membershipId) =
        let workflow user = 
            deserializeBody<UnitMember> req
            >>= setMembershipId membershipId
            >>= authorizeCorrespondingUnitModification user
            >>= await membershipValidator.ValidForUpdate
            >>= await data.Memberships.Update
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK
  
    [<FunctionName("MemberDelete")>]
    [<SwaggerOperation(Summary="Delete a unit membership.", Tags=[|"Unit Memberships"|])>]
    [<SwaggerResponse(204)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No membership was found with the ID provided.", typeof<ErrorModel>)>]
    let memberDelete
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "memberships/{membershipId}")>] req, membershipId) =
        let workflow user = 
            await membershipValidator.ValidEntity membershipId
            >>= authorizeCorrespondingUnitModification user
            >>= fun _ -> await data.Memberships.Delete membershipId
            >>= determineUserPermissions user
        req |> authenticate workflow Status.NoContent


    // *****************
    // ** Departments
    // *****************

    [<FunctionName("DepartmentGetAll")>]
    [<SwaggerOperation(Summary="List all departments.", Description="Search for departments by name and/or description.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, "A collection of department records", typeof<seq<Department>>)>]
    [<OptionalQueryParameter("q", typeof<string>)>]
    let departmentGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments")>] req) =
        let workflow user = 
            req
            |> tryQueryParam "q"
            |> await data.Departments.GetAll
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("DepartmentGetId")>]
    [<SwaggerOperation(Summary="Find a department by ID.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, "A department record", typeof<Department>)>]
    [<SwaggerResponse(404, "No department was found with the ID provided.", typeof<ErrorModel>)>]
    let departmentGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}")>] req, departmentId) =
        let workflow user = 
            await data.Departments.Get departmentId
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("DepartmentGetAllMemberUnits")>]
    [<SwaggerOperation(Summary="List a department's member units.", Description="A member unit contains people that have an HR relationship with the department.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, "A collection of unit records", typeof<seq<Unit>>)>]
    [<SwaggerResponse(404, "No department was found with the ID provided.", typeof<ErrorModel>)>]
    let departmentGetMemberUnits
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}/memberUnits")>] req, departmentId) =
        let workflow user = 
            await data.Departments.GetMemberUnits departmentId
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("DepartmentGetAllSupportingUnits")>]
    [<SwaggerOperation(Summary="List a department's supporting units.", Description="A member unit contains people that have an HR relationship with the department.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, "A collection of unit records", typeof<seq<Unit>>)>]
    [<SwaggerResponse(404, "No department was found with the ID provided.", typeof<ErrorModel>)>]
    let departmentGetSupportingUnits
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}/supportingUnits")>] req, departmentId) =
        let workflow user = 
            await data.Departments.GetSupportingUnits departmentId
            >>= determineUserPermissions user    
        req |> authenticate workflow Status.OK


    // ************************
    // ** Support Relationships
    // ************************

    let setRelationshipId id (a:SupportRelationship) = ok { a with Id=id }
    let relationshipValidator = supportRelationshipValidator data

    [<FunctionName("SupportRelationshipsGetAll")>]
    [<SwaggerOperation(Summary="List all unit-department support relationships.", Tags=[|"Support Relationships"|])>]
    [<SwaggerResponse(200, "A collection of support relationship records", typeof<SupportRelationship seq>)>]
    let supportRelationshipsGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "supportRelationships")>] req) =
        let workflow user = 
            await data.SupportRelationships.GetAll ()
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("SupportRelationshipsGetId")>]
    [<SwaggerOperation(Summary="Find a unit-department support relationships by ID", Tags=[|"Support Relationships"|])>]
    [<SwaggerResponse(200, "A support relationship record", typeof<SupportRelationship>)>]
    [<SwaggerResponse(404, "No support relationship was found with the ID provided.", typeof<ErrorModel>)>]
    let supportRelationshipsGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "supportRelationships/{relationshipId}")>] req, relationshipId) =
        let workflow user = 
            await data.SupportRelationships.Get relationshipId
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("SupportRelationshipsCreate")>]
    [<SwaggerOperation(Summary="Create a unit-department support relationship", Tags=[|"Support Relationships"|])>]
    [<SwaggerRequestExample(typeof<SupportRelationshipRequest>, typeof<SupportRelationshipRequestExample>)>]
    [<SwaggerResponse(201, "The newly created support relationship record", typeof<SupportRelationship>)>]
    [<SwaggerResponse(400, "The request body was malformed, the unitId and/or departmentId field was missing, or the specified unit and/or department does not exist.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The provided unit already has a support relationship with the provided department.", typeof<ErrorModel>)>]
    let supportRelationshipsCreate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "supportRelationships")>] req) =
        let workflow user = 
            deserializeBody<SupportRelationship> req
            >>= setRelationshipId 0
            >>= await relationshipValidator.ValidForCreate
            >>= authorizeCorrespondingUnitModification user
            >>= await data.SupportRelationships.Create          
            >>= determineUserPermissions user
        req |> authenticate workflow Status.Created

    [<FunctionName("SupportRelationshipsUpdate")>]
    [<SwaggerOperation(Summary="Update a unit-department support relationship", Tags=[|"Support Relationships"|])>]
    [<SwaggerRequestExample(typeof<SupportRelationshipRequest>, typeof<SupportRelationshipRequestExample>)>]
    [<SwaggerResponse(200, "The updated support relationship record", typeof<SupportRelationship>)>]
    [<SwaggerResponse(400, "The request body was malformed, the unitId and/or departmentId field was missing, or the specified unit and/or department does not exist.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No support relationship was found with the ID provided.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The provided unit already has a support relationship with the provided department.", typeof<ErrorModel>)>]
    let supportRelationshipsUpdate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "supportRelationships/{relationshipId}")>] req, relationshipId) =
        let workflow user = 
            deserializeBody<SupportRelationship> req
            >>= setRelationshipId relationshipId
            >>= await relationshipValidator.ValidForUpdate
            >>= authorizeCorrespondingUnitModification user
            >>= await data.SupportRelationships.Update
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("SupportRelationshipsDelete")>]
    [<SwaggerOperation(Summary="Delete a unit-department support relationship", Tags=[|"Support Relationships"|])>]
    [<SwaggerResponse(204)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No support relationship was found with the ID provided.", typeof<ErrorModel>)>]
    let supportRelationshipsDelete
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "supportRelationships/{relationshipId}")>] req, relationshipId) =
        let workflow user = 
            await relationshipValidator.ValidEntity relationshipId
            >>= authorizeCorrespondingUnitModification user
            >>= fun _ -> await data.SupportRelationships.Delete relationshipId
            >>= determineUserPermissions user
        req |> authenticate workflow Status.NoContent
