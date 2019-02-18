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

    let authorizeUnitModification (user:JwtClaims) model unitId =
        if isAdmin user
        then ok model
        else fail (Status.Forbidden, "You are not authorized to modify this resource.")

    /// Temporary: if this user is an admin, give them read/write access, else read-only.
    let determineUserPermissions user a =
        if isAdmin user
        then ok (a, [GET; POST; PUT; DELETE])
        else ok (a, [GET])

    // VALIDATION 

    let validateExistsAndPassThrough id model lookup = async {
        let! lookupResult = lookup id
        return 
            match lookupResult with
            | Ok(_) -> ok model
            | Bad(msgs) -> Bad msgs
    }
    let validateExistsAndReturn id lookup = async {
        let! lookupResult = lookup id
        return 
            match lookupResult with
            | Ok(value,_) -> ok value
            | Bad(msgs) -> Bad msgs
    }
    let validatePersonExists id = validateExistsAndPassThrough id id data.People.Get
    let findMembership id = validateExistsAndReturn id data.Memberships.Get
    let findSupportRelationship id = validateExistsAndReturn id data.SupportRelationships.Get


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
    [<SwaggerResponse(200, Type=typeof<JwtResponse>)>]
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
    [<SwaggerResponse(200, Type=typeof<seq<Person>>)>]
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
    [<SwaggerResponse(200, Type=typeof<Person>)>]
    let peopleGetById
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{personId}")>] req, personId) =
        let workflow user = 
            await data.People.Get personId
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("PeopleGetAllMemberships")>]
    [<SwaggerOperation(Summary="List a person's unit memberships", Description="List all units for which this person does IT work.", Tags=[|"People"|])>]
    [<SwaggerResponse(200, Type=typeof<seq<UnitMember>>)>]
    let peopleGetAllMemberships
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{personId}/memberships")>] req, personId) =
        let workflow user = 
            await validatePersonExists personId
            >>= await data.People.GetMemberships
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK


    // *****************
    // ** Units
    // *****************

    let findUnit id = validateExistsAndReturn id data.Units.Get
    let validateUnitExists (model:Unit) = 
        validateExistsAndPassThrough model.Id model data.Units.Get
    let validateUnitExists' id model = 
        validateExistsAndPassThrough id model data.Units.Get
    let validateUnitParentExists (u:Unit) = 
        match u.ParentId with 
        | Some(id) -> validateUnitExists' id u
        | None -> ok u |> async.Return

    let testForCircularDependency (u:Unit) (child:Unit option) =    
        match (child) with
        | Some(c) -> 
            let error = sprintf "Whoops! %s is a parent of %s in the unit hierarcy. Adding it as a child would result in a circular relationship. 🙃" u.Name c.Name
            fail(Status.Conflict, error)
        | None -> ok u
    
    let validateUnitParentIsNotCircular (u:Unit) = async {
        match u.ParentId with
        | None -> return (ok u)
        | Some(parentId) ->    
            return 
                parentId
                |> await (data.Units.GetDescendantOfParent u) 
                >>= testForCircularDependency u
    }
       
    let authorizeUnitModification' user (model:Unit) = 
        if (model.Id = 0 && model.ParentId.IsNone)
        then 
            if isAdmin user
            then ok model
            else fail(Status.Forbidden, "Please contact IT Community Partnerships (talk2uits@iu.edu) to create a top-level IT unit.")
        else authorizeUnitModification user model model.Id

    [<FunctionName("UnitGetAll")>]
    [<SwaggerOperation(Summary="List all IT units.", Description="Search for IT units by name and/or description. If no search term is provided, lists all top-level IT units." , Tags=[|"Units"|])>]
    [<SwaggerResponse(200, Type=typeof<seq<Unit>>)>]
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
    [<SwaggerResponse(200, Type=typeof<Unit>)>]
    let unitGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}")>] req, unitId) =
        let workflow user = 
            await data.Units.Get unitId
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK
            
    [<FunctionName("UnitPost")>]
    [<SwaggerOperation(Summary="Create a unit.", Tags=[|"Units"|])>]
    [<SwaggerRequestExample(typeof<UnitRequest>, typeof<UnitRequestExample>)>]
    [<SwaggerResponse(201, Type=typeof<Unit>)>]
    let unitPost
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "units")>] req) =
        let workflow user =
            deserializeBody<Unit> req
            >>= authorizeUnitModification' user
            >>= await validateUnitParentExists
            >>= await validateUnitParentIsNotCircular
            >>= await data.Units.Create
            >>= determineUserPermissions user
        req |> authenticate workflow Status.Created

    [<FunctionName("UnitPut")>]
    [<SwaggerOperation(Summary="Update a unit.", Tags=[|"Units"|])>]
    [<SwaggerRequestExample(typeof<UnitRequest>, typeof<UnitRequestExample>)>]
    [<SwaggerResponse(200, Type=typeof<Unit>)>]
    let unitPut
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "units/{unitId}")>] req, unitId) =
        let workflow user = 
            deserializeBody<Unit> req
            >>= fun model -> ok { model with Id=unitId } 
            >>= await validateUnitExists
            >>= authorizeUnitModification' user
            >>= await validateUnitParentExists
            >>= await validateUnitParentIsNotCircular
            >>= await data.Units.Update
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("UnitDelete")>]
    [<SwaggerOperation(Summary="Delete a unit.", Tags=[|"Units"|])>]
    [<SwaggerResponse(204)>]
    let unitDelete
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "units/{unitId}")>] req, unitId) =
        let workflow user =
            await findUnit unitId
            >>= authorizeUnitModification' user
            >>= await data.Units.Delete
            >>= determineUserPermissions user
        req |> authenticate workflow Status.NoContent

    [<FunctionName("UnitGetAllMembers")>]
    [<SwaggerOperation(Summary="List all unit members", Description="List all people who do IT work for this unit along with any vacant positions.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, Type=typeof<seq<UnitMember>>)>]
    let unitGetAllMembers
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/members")>] req, unitId) =
        let workflow user =
            await findUnit unitId
            >>= await data.Units.GetMembers
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("UnitGetAllSupportedDepartments")>]
    [<SwaggerOperation(Summary="List all supported departments", Description="List all departments that receive IT support from this unit.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, Type=typeof<seq<UnitMember>>)>]
    let unitGetAllSupportedDepartments
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/supportedDepartments")>] req, unitId) =
        let workflow user = 
            await findUnit unitId
            >>= await data.Units.GetSupportedDepartments
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("UnitGetAllChildren")>]
    [<SwaggerOperation(Summary="List all unit children", Description="List all units that fall below this unit in an organizational hierarchy.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, Type=typeof<seq<Unit>>)>]
    let unitGetAllChildren
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/children")>] req, unitId) =
        let workflow user = 
            await findUnit unitId
            >>= await data.Units.GetChildren
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK


    // *******************
    // ** Unit Memberships
    // *******************

    let validateMembershipRecordExists (m:UnitMember) = 
        validateExistsAndPassThrough m.Id m data.Memberships.Get
    let validateMembershipUnitExists (m:UnitMember) = 
        validateExistsAndPassThrough m.UnitId m data.Units.Get 
    let validateMembershipPersonExists (m:UnitMember) = 
        match m.PersonId with 
        | Some(id) -> validateExistsAndPassThrough id m data.People.Get
        | None -> ok m |> async.Return


    let authorizeMemberUnitModification user (model:UnitMember) = 
        authorizeUnitModification user model model.UnitId 

    [<FunctionName("MemberGetAll")>]
    [<SwaggerOperation(Summary="Find a unit membership", Tags=[|"Unit Memberships"|])>]
    [<SwaggerResponse(200, Type=typeof<seq<UnitMember>>)>]
    let memberGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "memberships")>] req) =
        let workflow user = 
            await data.Memberships.GetAll ()
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("MemberGetById")>]
    [<SwaggerOperation(Summary="Find a unit membership by ID", Tags=[|"Unit Memberships"|])>]
    [<SwaggerResponse(200, Type=typeof<UnitMember>)>]
    let memberGetById
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "memberships/{membershipId}")>] req, membershipId) =
        let workflow user = 
            await data.Memberships.Get membershipId
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("MemberCreate")>]
    [<SwaggerOperation(Summary="Create a unit membership.", Tags=[|"Unit Memberships"|])>]
    [<SwaggerRequestExample(typeof<UnitMemberRequest>, typeof<MembershipRequestExample>)>]
    [<SwaggerResponse(201, Type=typeof<UnitMember>)>]
    let memberCreate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "memberships")>] req) =
        let workflow user = 
            deserializeBody<UnitMember> req
            >>= await validateMembershipUnitExists
            >>= await validateMembershipPersonExists
            >>= authorizeMemberUnitModification user
            >>= await data.Memberships.Create
            >>= determineUserPermissions user
        req |> authenticate workflow Status.Created

    [<FunctionName("MemberUpdate")>]
    [<SwaggerOperation(Summary="Update a unit membership.", Tags=[|"Unit Memberships"|])>]
    [<SwaggerRequestExample(typeof<UnitMemberRequest>, typeof<MembershipRequestExample>)>]
    [<SwaggerResponse(200, Type=typeof<UnitMember>)>]
    let memberUpdate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "memberships/{membershipId}")>] req, membershipId) =
        let workflow user = 
            deserializeBody<UnitMember> req
            >>= fun model -> ok { model with Id=membershipId }
            >>= await validateMembershipRecordExists
            >>= await validateMembershipUnitExists
            >>= await validateMembershipPersonExists
            >>= authorizeMemberUnitModification user
            >>= await data.Memberships.Update
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK
  
    [<FunctionName("MemberDelete")>]
    [<SwaggerOperation(Summary="Delete a unit membership.", Tags=[|"Unit Memberships"|])>]
    [<SwaggerResponse(204)>]
    let memberDelete
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "memberships/{membershipId}")>] req, membershipId) =
        let workflow user = 
            await findMembership membershipId
            >>= authorizeMemberUnitModification user
            >>= await data.Memberships.Delete
            >>= determineUserPermissions user
        req |> authenticate workflow Status.NoContent


    // *****************
    // ** Departments
    // *****************

    [<FunctionName("DepartmentGetAll")>]
    [<SwaggerOperation(Summary="List all departments.", Description="Search for departments by name and/or description.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, Type=typeof<seq<Department>>)>]
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
    [<SwaggerResponse(200, Type=typeof<Department>)>]
    let departmentGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}")>] req, departmentId) =
        let workflow user = 
            await data.Departments.Get departmentId
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("DepartmentGetAllMemberUnits")>]
    [<SwaggerOperation(Summary="List a department's member units.", Description="A member unit contains people that have an HR relationship with the department.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, Type=typeof<seq<Unit>>)>]
    let departmentGetMemberUnits
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}/memberUnits")>] req, departmentId) =
        let workflow user = 
            await data.Departments.GetMemberUnits departmentId
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("DepartmentGetAllSupportingUnits")>]
    [<SwaggerOperation(Summary="List a department's supporting units.", Description="A member unit contains people that have an HR relationship with the department.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, Type=typeof<seq<Unit>>)>]
    let departmentGetSupportingUnits
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}/supportingUnits")>] req, departmentId) =
        let workflow user = 
            await data.Departments.GetSupportingUnits departmentId
            >>= determineUserPermissions user    
        req |> authenticate workflow Status.OK


    // ************************
    // ** Support Relationships
    // ************************
    let authorizeSupportUnitModification user (model:SupportRelationship) = 
        authorizeUnitModification user model model.UnitId 

    [<FunctionName("SupportRelationshipsGetAll")>]
    [<SwaggerOperation(Summary="List all unit-department support relationships.", Tags=[|"Support Relationships"|])>]
    [<SwaggerResponse(200, Type=typeof<SupportRelationship>)>]
    // [<SwaggerResponseExample(200, typeof<SupportRelationshipsResponseExample>)>]
    let supportRelationshipsGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "supportRelationships")>] req) =
        let workflow user = 
            await data.SupportRelationships.GetAll ()
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("SupportRelationshipsGetId")>]
    [<SwaggerOperation(Summary="Find a unit-department support relationships by ID", Tags=[|"Support Relationships"|])>]
    [<SwaggerResponse(200, Type=typeof<SupportRelationship seq>)>]
    let supportRelationshipsGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "supportRelationships/{relationshipId}")>] req, relationshipId) =
        let workflow user = 
            await data.SupportRelationships.Get relationshipId
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("SupportRelationshipsCreate")>]
    [<SwaggerOperation(Summary="Create a unit-department support relationship", Tags=[|"Support Relationships"|])>]
    [<SwaggerRequestExample(typeof<SupportRelationshipRequest>, typeof<SupportRelationshipRequestExample>)>]
    [<SwaggerResponse(201, Type=typeof<SupportRelationship>)>]
    let supportRelationshipsCreate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "supportRelationships")>] req) =
        let workflow user = 
            deserializeBody<SupportRelationship> req
            >>= authorizeSupportUnitModification user
            >>= await data.SupportRelationships.Create          
            >>= determineUserPermissions user
        req |> authenticate workflow Status.Created

    [<FunctionName("SupportRelationshipsUpdate")>]
    [<SwaggerOperation(Summary="Update a unit-department support relationship", Tags=[|"Support Relationships"|])>]
    [<SwaggerRequestExample(typeof<SupportRelationshipRequest>, typeof<SupportRelationshipRequestExample>)>]
    [<SwaggerResponse(200, Type=typeof<SupportRelationship>)>]
    let supportRelationshipsUpdate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "supportRelationships/{relationshipId}")>] req, relationshipId) =
        let workflow user = 
            deserializeBody<SupportRelationship> req
            >>= fun model -> ok { model with Id=relationshipId }
            >>= authorizeSupportUnitModification user
            >>= await data.SupportRelationships.Update
            >>= determineUserPermissions user
        req |> authenticate workflow Status.OK

    [<FunctionName("SupportRelationshipsDelete")>]
    [<SwaggerOperation(Summary="Delete a unit-department support relationship", Tags=[|"Support Relationships"|])>]
    [<SwaggerResponse(204)>]
    let supportRelationshipsDelete
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "supportRelationships/{relationshipId}")>] req, relationshipId) =
        let workflow user = 
            await findSupportRelationship relationshipId
            >>= authorizeSupportUnitModification user
            >>= await data.SupportRelationships.Delete
            >>= determineUserPermissions user
        req |> authenticate workflow Status.NoContent
