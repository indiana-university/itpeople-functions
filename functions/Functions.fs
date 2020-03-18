// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause    

namespace Functions

open Core.Types
open Core.Util
open Core.Json

open Api
open Jwt
open Authorization
open Logging
open Validation
open Examples

open System
open System.Net.Http
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Extensions.Http
open Microsoft.Extensions.Logging

open Swashbuckle.AspNetCore.Annotations
open Swashbuckle.AspNetCore.Filters
open Swashbuckle.AspNetCore.AzureFunctions.Annotations

/// This module defines the bindings and triggers for all functions in the project
module Functions =    

    // DEPENDENCY RESOLUTION

    /// Dependencies are resolved once at startup.
    let openApiSpec = lazy (generateOpenAPISpec())
    let config = getConfiguration()
    let data = 
        if config.UseFakes
        then FakesRepository.Repository
        else
            Database.Command.init()
            DatabaseRepository.Repository(config.DbConnectionString)
    let log = createLogger config.DbConnectionString
    
    let publicKey =
        let envKey = System.Environment.GetEnvironmentVariable("OAuthPublicKey")
        if isNull envKey
        then
            "JWT Public Key not found in Environment; Fetching from UAA..." |> log.Information
            let resp =  
                config.OAuth2TokenUrl
                |> sprintf "%s_key"
                |> data.Authorization.UaaPublicKey 
                |> Async.RunSynchronously
            match resp with
            | Ok(uaaKey) -> 
                uaaKey
                |> sprintf "Using JWT Public Key from UAA: %s" 
                |> log.Information
                uaaKey
            | Error(msg) -> 
                let err = sprintf "Failed to fetch UAA Public Key. Function app cannot start. Reason: %A" msg
                log.Fatal(err)
                err |> System.Exception |> raise
        else 
            envKey 
            |> sprintf "Using JWT Public Key from Environment: %s" 
            |> log.Information
            envKey

           
    // FUNCTION WORKFLOW HELPERS 

    let addProperty (req:HttpRequestMessage) key value = 
        req.Properties.Add(key, value)

    let getProperty (req:HttpRequestMessage) key = 
        req.Properties.[key] |> string

    /// Logging: Add a timestamp to the request properties.
    let timestamp req = 
        addProperty req WorkflowTimestamp DateTime.UtcNow
        
    /// Logging: Add the authenticated user to the request properties
    let recordUserPermissions req model perms =
        addProperty req WorkflowPermissions perms
        ok model
    
    let recordAuthenticatedUser req (netid:NetId) =
        addProperty req WorkflowUser netid

    let authenticate req = pipeline {
        let! netid = authenticateRequest publicKey req
        recordAuthenticatedUser req netid
        return ()
    }

    let authenticatedRequestor req = 
        getProperty req WorkflowUser

    let setEndpointPermissions req permissionResolver = pipeline {
        let netid = getProperty req WorkflowUser
        let! canModify = permissionResolver data.Authorization netid
        let permissions = if canModify then [GET; POST; PUT; DELETE] else [GET]
        addProperty req WorkflowPermissions permissions
        return ()
    }

    let authorizeAction (req:HttpRequestMessage) requiredPermission description = 
        let canDo = 
            req.Properties.[WorkflowPermissions] 
            :?> List<UserPermissions>
            |> Seq.contains requiredPermission
        if canDo
        then ok ()
        else error (Status.Forbidden, sprintf "You are not allowed to %s this resource." description)

    let authorizeUpdate req = authorizeAction req PUT "modify"
    let authorizeCreate req = authorizeAction req POST "create"
    let authorizeDelete req = authorizeAction req DELETE "delete"

    let permission'' req authFn model =
        let user = getProperty req WorkflowUser
        determineAuthenticatedUserPermissions data.Authorization authFn user
        >>= recordUserPermissions req model

    let authorize req authFn model = pipeline {
        let netid = getProperty req WorkflowUser
        let! _ = permission'' req authFn model
        return! authorizeRequest data.Authorization model authFn netid
    }

    let inline authorizeRelationUnitModification req relation =
        authorize req (canModifyUnit (unitId relation)) relation
    
    let inline permissionRelationUnitModification req relation =
        permission'' req (canModifyUnit (unitId relation)) relation     

    type Formatter<'a> = 'a -> StringContent

    /// Execute a workflow for an authenticated user and return a response.
    let inline execute (successStatus:Status) (req:HttpRequestMessage) (responseFormatter: Formatter<'a> option) (workflow: Async<Result<'a,Error>>)  = 
        async {
            try
                timestamp req
                let! result = workflow
                match result with
                | Ok body ->
                    do! logSuccess log req successStatus
                    match responseFormatter with
                    | None -> return emptyResponse req config.CorsHosts successStatus 
                    | Some(fmt) -> return body |> fmt |> contentResponse req config.CorsHosts successStatus
                | Error (status,msg) -> 
                    do! logError log req status msg
                    return msg |> jsonResponse |> contentResponse req config.CorsHosts status
            with exn -> 
                do! logFatal log req exn
                raise exn
                return req.CreateErrorResponse(Status.InternalServerError, exn.Message)
        } |> Async.StartAsTask

    let get req workflow = execute Status.OK req (Some jsonResponse) workflow
    let create req workflow = execute Status.Created req (Some jsonResponse) workflow
    let update req workflow = execute Status.OK req (Some jsonResponse) workflow
    let delete req workflow = execute Status.NoContent req None workflow
    let getXml req workflow = execute Status.OK req (Some xmlResponse) workflow


    let inline ensureEntityExistsForModel (getter:Id->Async<Result<'a,Error>>) model : Async<Result<'b,Error>> = async {
        let! result = getter (identity model)
        match result with 
        | Ok _ -> return Ok model
        | Error msg -> return Error msg
    }     

    let inline ensureExists (entityResolver:Id->Async<Result<'a,Error>>) id = async {
        let! entity = entityResolver id
        match entity with 
        | Ok _ -> return Ok ()
        | Error msg -> return Error msg
    }     

    // VALIDATION 


    // FUNCTION WORKFLOWS 

    // *****************
    // ** Availability
    // *****************

    [<FunctionName("Options")>]
    [<SwaggerIgnore>]
    let options
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "{*url}")>] req) =
        optionsResponse req config

    [<FunctionName("PingGet")>]
    [<SwaggerIgnore>]
    let ping
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")>] req) =
        "Pong!" |> textContent |> contentResponse req "*" Status.OK

    // *****************
    // ** Documentation
    // *****************

    [<FunctionName("OpenAPI")>]
    [<SwaggerIgnore>]
    let openapi
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "openapi.json")>] req) =
        try openApiSpec.Value |> jsonContent |> contentResponse req "*" Status.OK
        with exn -> exn.ToString() |> textContent |> contentResponse req "*" Status.InternalServerError

    [<FunctionName("OpenAPIHtml")>]
    [<SwaggerIgnore>]
    let openapihtml
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "/")>] req) =
        """<!DOCTYPE html>
<html>
<head>
    <title>IT People API</title>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <link href="https://fonts.googleapis.com/css?family=Montserrat:300,400,700|Roboto:300,400,700" rel="stylesheet">
    <style> body { margin: 0; padding: 0; }</style>
</head>
<body>
    <redoc spec-url='/openapi.json'></redoc>
    <script src="https://cdn.jsdelivr.net/npm/redoc@next/bundles/redoc.standalone.js"> </script>
</body>
</html>"""
        |> htmlContent
        |> contentResponse req "*" Status.OK


    // *****************
    // ** Authentication
    // *****************

    [<FunctionName("AuthGet")>]
    [<SwaggerIgnore>]
    let authGet
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth")>] req) =
        let workflow = pipeline {  
            let! query = queryParam "oauth_code" req
            let! tokenRequest = createUaaTokenRequest config query
            let! jwt = postAsync<JwtResponse> config.OAuth2TokenUrl tokenRequest
            let! netid = decodeJwt publicKey jwt.access_token
            recordAuthenticatedUser req netid
            return jwt
        }

        get req workflow

    // *****************
    // ** People
    // *****************

    [<FunctionName("PeopleGetAll")>]
    [<SwaggerOperation(Summary="Search IT people", Description="""Search for IT people. Available filters include:<br/>
    <ul><li><strong>q</strong>: filter by name/netid, ex: 'Ron' or 'rswanso'
    <li><strong>class</strong>: filter by job classification/responsibility, ex: 'UserExperience' or 'UserExperience,WebAdminDevEng'
    <li><strong>interest</strong>: filter by one interests, ex: 'serverless' or 'node,lambda'
    <li><strong>campus</strong>: filter by primary campus: 'Bloomington','Indianapolis','Columbus','East','Fort Wayne','Kokomo','Northwest','South Bend','Southeast'
    <li><strong>role</strong>: filter by unit role, ex: 'Leader' or 'Leader,Member'
    <li><strong>permission</strong>: filter by unit permissions, ex: 'Owner' or 'Owner,ManageMembers'
    <li><strong>area</strong>: filter by unit area, e.g. 'uits' or 'edge'
    </ul></br>
    Search results are unioned within a filter and intersected across filters. For example, 'interest=node,lambda' will 
    return people with an interest in either 'node' OR 'lambda', whereas `role=ItLeadership&interest=node` will only return
    people who are both in 'ItLeadership' AND have an interest in 'node'.""", Tags=[|"People"|])>]
    [<SwaggerResponse(200, "A collection of person records.", typeof<seq<Person>>)>]
    [<OptionalQueryParameter("q", typeof<string>)>]
    [<OptionalQueryParameter("class", typeof<seq<Responsibilities>>)>]
    [<OptionalQueryParameter("interest", typeof<seq<string>>)>]
    [<OptionalQueryParameter("campus", typeof<seq<string>>)>]
    [<OptionalQueryParameter("role", typeof<seq<Role>>)>]
    [<OptionalQueryParameter("permission", typeof<seq<UnitPermissions>>)>]
    [<OptionalQueryParameter("area", typeof<seq<Area>>)>]
    let peopleGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people")>] req) =
        let queryParams =
            { Query=parseQueryAsString req "q"
              Classes=parseQueryAsInt req "class" (fun x->Enum.TryParse<Responsibilities>(x,true))
              Interests=parseQueryAsStringArray req "interest"
              Roles=parseQueryAsIntArray req "role" (fun x->Enum.TryParse<Role>(x,true))
              Campuses=parseQueryAsStringArray req "campus"
              Permissions=parseQueryAsIntArray req "permission" (fun x->Enum.TryParse<UnitPermissions>(x,true))
              Area=parseQueryAsInt req "area" (fun x->Enum.TryParse<Area>(x,true)) }

        let workflow = pipeline { 
            do! authenticate req
            return! data.People.GetAll queryParams
        }

        get req workflow

    let addPersonToDirectory netid = pipeline {
        let! hrPerson = data.People.GetHr netid
        return! data.People.Create hrPerson
    }
    let ensurePersonInDirectory netid = pipeline {
        let! (_,idOption) = data.People.TryGetId netid
        let! person = 
            match idOption with
            | Some(id) -> data.People.GetById id
            | None -> addPersonToDirectory netid
        return person
    }

    [<FunctionName("PeopleLookupAll")>]
    [<SwaggerOperation(Summary="Search all staff", Description="Search for staff, including IT People, by name or username (netid).", Tags=[|"People"|])>]
    [<SwaggerResponse(200, "A collection of person records.", typeof<seq<Person>>)>]
    [<OptionalQueryParameter("q", typeof<string>)>]
    let peopleLookupAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people-lookup")>] req) =
        let workflow = pipeline { 
            do! authenticate req
            let! query = queryParam "q" req
            return! data.People.GetAllWithHr query
        }
        get req workflow

    /// Find a person by their record id (int) or a netid (string).
    /// If using a netid, assume that the person may not yet be in the directory.
    let findPerson (personId:string) = 
        match personId with
        | Int id -> data.People.GetById id
        | _      -> ensurePersonInDirectory personId

    [<FunctionName("PeopleGetById")>]
    [<SwaggerOperation(Summary="Find a person by ID", Tags=[|"People"|])>]
    [<SwaggerResponse(200, "A person record.", typeof<Person>)>]
    [<SwaggerResponse(404, "No person was found with the ID provided.", typeof<ErrorModel>)>]
    let peopleGetById
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{personId}")>] req, personId) =
        let workflow = pipeline {
            do! authenticate req
            let! person = findPerson personId
            do! setEndpointPermissions req (canModifyPerson person.Id)
            return person
        }
        get req workflow

    [<FunctionName("PeopleGetAllMemberships")>]
    [<SwaggerOperation(Summary="List a person's unit memberships", Description="List all units for which this person does IT work.", Tags=[|"People"|])>]
    [<SwaggerResponse(200, "A collection of units of which this person is a member.", typeof<seq<UnitMember>>)>]
    [<SwaggerResponse(404, "No person was found with the ID provided.", typeof<ErrorModel>)>]
    let peopleGetAllMemberships
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{personId}/memberships")>] req, personId) =
        let workflow = pipeline {
            do! authenticate req
            let! person = findPerson personId
            return! data.People.GetMemberships person.Id
        }
        get req workflow

    [<FunctionName("PersonPut")>]
    [<SwaggerOperation(Summary="Update a person's location, expertise, and responsibilities/job classes.", Description="<em>Authorization</em>: The JWT must represent either the person whose record is being modified (i.e., a person can modify their  own record), or someone who has permissions to manage a unit of which this person is a member (i.e., typically that person's manager/supervisor.)  ", Tags=[|"People"|])>]
    [<SwaggerRequestExample(typeof<PersonRequest>, typeof<PersonRequestExample>)>]
    [<SwaggerResponse(200, "A record of the updated person", typeof<Person>)>]
    [<SwaggerResponse(400, "The request body is malformed.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You do not have permission to modify this person.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No person was found with the ID provided.", typeof<ErrorModel>)>]
    let personPut
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "people/{personId}")>] req, personId) =
        let workflow = pipeline {
            do! authenticate req
            do! ensureExists data.People.GetById personId
            do! setEndpointPermissions req (canModifyPerson personId)
            do! authorizeUpdate req
            let! body = deserializeBody<PersonRequest> req
            return! data.People.Update { body with Id=personId }
        }
        update req workflow


    // *****************
    // ** Units
    // *****************

    let setUnitId id (a:Unit) = ok { a with Id=id }

    [<FunctionName("UnitGetAll")>]
    [<SwaggerOperation(Summary="List all IT units.", Description="""Search for IT units by name and/or description. If no search term is provided, lists all top-level IT units. Available filters include:<br/>
    <ul><li><strong>q</strong>: filter by unit name/description, ex: 'Parks'</ul></br>""" , Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A collection of unit records.", typeof<seq<Unit>>)>]
    [<OptionalQueryParameter("q", typeof<string>)>]
    let unitGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units")>] req) =
        let workflow = pipeline {
            do! authenticate req
            do! setEndpointPermissions req canCreateDeleteUnit
            let! query = tryQueryParam req "q"
            return! data.Units.GetAll query
        }
        get req workflow

    [<FunctionName("UnitGetId")>]
    [<SwaggerOperation(Summary="Find a unit by ID.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A unit record.", typeof<Unit>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    let unitGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}")>] req, unitId) =
        let workflow = pipeline {
            do! authenticate req
            do! setEndpointPermissions req (canModifyUnit unitId)
            return! data.Units.Get unitId 
        }
        get req workflow
            
    [<FunctionName("UnitPost")>]
    [<SwaggerOperation(Summary="Create a unit.", Description="<em>Authorization</em>: Unit creation is restricted to service administrators.", Tags=[|"Units"|])>]
    [<SwaggerRequestExample(typeof<UnitRequest>, typeof<UnitRequestExample>)>]
    [<SwaggerResponse(201, "A record of the newly created unit.", typeof<Unit>)>]
    [<SwaggerResponse(400, "The request body is malformed, or the unit name is missing.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "The specified unit parent does not exist.", typeof<ErrorModel>)>]
    let unitPost
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "units")>] req) =
        let workflow = pipeline {
            do! authenticate req
            do! setEndpointPermissions req canCreateDeleteUnit
            do! authorizeCreate req
            let! body = deserializeBody<Unit> req
            return! data.Units.Create { body with Id=0 }
        }
        create req workflow

    [<FunctionName("UnitPut")>]
    [<SwaggerOperation(Summary="Update a unit.", Description="<em>Authorization</em>: Units can be modified by any unit member that has either the `Owner` or `ManageMembers` permission on their membership. See also: [Units - List all unit members](#operation/unitGetAllMembers).", Tags=[|"Units"|])>]
    [<SwaggerRequestExample(typeof<UnitRequest>, typeof<UnitRequestExample>)>]
    [<SwaggerResponse(200, "A record of the updated unit", typeof<Unit>)>]
    [<SwaggerResponse(400, "The request body is malformed, or the unit name is missing.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You do not have permission to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided, or the specified unit parent does not exist.", typeof<ErrorModel>)>]
    let unitPut
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "units/{unitId}")>] req, unitId) =
        let workflow = pipeline {
            do! authenticate req
            do! setEndpointPermissions req (canModifyUnit unitId)
            do! ensureExists data.Units.Get unitId
            do! authorizeUpdate req
            let! body = deserializeBody<Unit> req
            do! assertUnitParentRelationshipIsNotCircular data unitId body.ParentId
            return! data.Units.Update { body with Id=unitId }
        }
        update req workflow

    [<FunctionName("UnitDelete")>]
    [<SwaggerOperation(Summary="Delete a unit.", Description="<em>Authorization</em>: Unit deletion is restricted to service administrators.", Tags=[|"Units"|])>]
    [<SwaggerResponse(204)>]
    [<SwaggerResponse(403, "You do not have permission to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The unit has children. These must be reassigned prior to deletion.", typeof<ErrorModel>)>]
    let unitDelete
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "units/{unitId}")>] req, unitId) =
        let workflow = pipeline {
            do! authenticate req
            do! setEndpointPermissions req canCreateDeleteUnit
            do! ensureExists data.Units.Get unitId
            do! authorizeDelete req
            do! assertUnitHasNoChildren data unitId
            return! data.Units.Delete unitId
        }
        delete req workflow       

    
    [<FunctionName("UnitGetAllMembers")>]
    [<SwaggerOperation(Summary="List all unit members", Description="List all people who do IT work for this unit along with any vacant positions.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A collection of membership records.", typeof<seq<UnitMember>>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    let unitGetAllMembers
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/members")>] req, unitId) =
        let workflow = pipeline {
            do! authenticate req
            do! setEndpointPermissions req (canModifyUnit unitId)
            let netid = authenticatedRequestor req
            do! ensureExists data.Units.Get unitId
            let! canModifyResult = canModifyUnit unitId data.Authorization netid
            let options = if canModifyResult then MembersWithNotes(unitId) else MembersWithoutNotes(unitId)
            return! data.Units.GetMembers options
        }
        get req workflow

    let getUnitRelations req unitId relationResolver = pipeline {
        do! authenticate req
        do! setEndpointPermissions req (canModifyUnit unitId)
        do! ensureExists data.Units.Get unitId
        return! relationResolver unitId
    } 

    [<FunctionName("UnitGetAllSupportedDepartments")>]
    [<SwaggerOperation(Summary="List all supported departments", Description="List all departments that receive IT support from this unit.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A collection of unit-department relationship records.", typeof<seq<SupportRelationship>>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    let unitGetAllSupportedDepartments
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/supportedDepartments")>] req, unitId) =
        let workflow = getUnitRelations req unitId data.Units.GetSupportedDepartments
        get req workflow

    [<FunctionName("UnitGetAllSupportedBuildings")>]
    [<SwaggerOperation(Summary="List all supported buildings", Description="List all buildings that receive IT support from this unit.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A collection of unit-building relationship records.", typeof<seq<BuildingRelationship>>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    let unitGetAllSupportedBuildings
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/supportedBuildings")>] req, unitId) =
        let workflow = getUnitRelations req unitId data.Units.GetSupportedBuildings
        get req workflow

    [<FunctionName("UnitGetAllChildren")>]
    [<SwaggerOperation(Summary="List all unit children", Description="List all units that fall below this unit in an organizational hierarchy.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A collection of unit records.", typeof<seq<Unit>>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    let unitGetAllChildren
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/children")>] req, unitId) =
        let workflow = getUnitRelations req unitId data.Units.GetChildren
        get req workflow

    [<FunctionName("UnitGetAllTools")>]
    [<SwaggerOperation(Summary="List all unit tools", Description="List all tools that are available to this unit.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A collection of tool records.", typeof<seq<Tool>>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    let unitGetAllTools
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/tools")>] req, unitId) =
        let workflow = getUnitRelations req unitId (fun _ -> data.Tools.GetAll ())
        get req workflow

    // *******************
    // ** Unit Memberships
    // *******************

    let setMembershipId id (a:UnitMember) = Ok { a with Id=id } |> async.Return

    [<FunctionName("MemberGetAll")>]
    [<SwaggerOperation(Summary="List all unit memberships", Tags=[|"Unit Memberships"|])>]
    [<SwaggerResponse(200, "A collection of unit membership records", typeof<seq<UnitMember>>)>]
    let memberGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "memberships")>] req) =
        let workflow = pipeline {
            do! authenticate req
            return! data.Memberships.GetAll ()
        }
        get req workflow

    [<FunctionName("MemberGetById")>]
    [<SwaggerOperation(Summary="Find a unit membership by ID", Tags=[|"Unit Memberships"|])>]
    [<SwaggerResponse(200, "A unit membership record", typeof<UnitMember>)>]
    [<SwaggerResponse(404, "No membership was found with the ID provided.", typeof<ErrorModel>)>]
    let memberGetById
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "memberships/{membershipId}")>] req, membershipId) =
        let workflow = pipeline {
            do! authenticate req
            let! membership = data.Memberships.Get membershipId
            do! setEndpointPermissions req (canModifyUnit membership.UnitId)
            return membership
        }
        get req workflow

    let ensureUnitMemberInDirectory (um:UnitMember) = pipeline {
        match (um.PersonId, um.NetId) with
        | (None, None) 
        | (Some(0), None) -> return None // This position is a vacancy.
        | (None, Some(netid))
        | (Some(0), Some(netid)) -> // We don't have this person in the directory. Add them now.
            let! person = ensurePersonInDirectory netid
            return Some(person.Id)
        | (Some(_), _) -> return um.PersonId // This position is filled by someone in the directory.
    }

    [<FunctionName("MemberCreate")>]
    [<SwaggerOperation(Summary="Create a unit membership.", Description="<em>Authorization</em>: Unit memberships can be created by any unit member that has either the `Owner` or `ManageMembers` permission on their unit membership. See also: [Units - List all unit members](#operation/unitGetAllMembers).", Tags=[|"Unit Memberships"|])>]
    [<SwaggerRequestExample(typeof<UnitMemberRequest>, typeof<MembershipRequestExample>)>]
    [<SwaggerResponse(201, "The newly created unit membership record", typeof<UnitMember>)>]
    [<SwaggerResponse(400, "The request body was malformed or the unitId field was missing.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "The specified unit does not exist.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The provided person is already a member of the provided unit.", typeof<ErrorModel>)>]
    let memberCreate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "memberships")>] req) =
        let workflow = pipeline {
            do! authenticate req
            let! body = deserializeBody<UnitMember> req
            do! setEndpointPermissions req (canModifyUnit body.UnitId)
            do! authorizeCreate req
            let! personId = ensureUnitMemberInDirectory body
            return! data.Memberships.Create { body with Id=0; PersonId=personId }
        }
        create req workflow

    [<FunctionName("MemberUpdate")>]
    [<SwaggerOperation(Summary="Update a unit membership.", Description="<em>Authorization</em>: Unit memberships can be updated by any unit member that has either the `Owner` or `ManageMembers` permission on their unit membership. See also: [Units - List all unit members](#operation/unitGetAllMembers).", Tags=[|"Unit Memberships"|])>]
    [<SwaggerRequestExample(typeof<UnitMemberRequest>, typeof<MembershipRequestExample>)>]
    [<SwaggerResponse(200, "The update unit membership record.", typeof<UnitMember>)>]
    [<SwaggerResponse(400, "The request body was malformed, the unitId field was missing.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No membership was found with the ID provided, or the specified unit does not exist.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The provided person is already a member of the provided unit.", typeof<ErrorModel>)>]
    let memberUpdate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "memberships/{membershipId}")>] req, membershipId) =
        let workflow = pipeline {
            do! authenticate req
            let! body = deserializeBody<UnitMember> req
            do! setEndpointPermissions req (canModifyUnit body.UnitId)
            do! ensureExists data.Memberships.Get membershipId
            do! authorizeUpdate req
            return! data.Memberships.Update { body with Id=membershipId }
        }
        update req workflow
  
    [<FunctionName("MemberDelete")>]
    [<SwaggerOperation(Summary="Delete a unit membership.", Description="<em>Authorization</em>: Unit memberships can be deleted by any unit member that has either the `Owner` or `ManageMembers` permission on their unit membership. See also: [Units - List all unit members](#operation/unitGetAllMembers).", Tags=[|"Unit Memberships"|])>]
    [<SwaggerResponse(204)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No membership was found with the ID provided.", typeof<ErrorModel>)>]
    let memberDelete
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "memberships/{membershipId}")>] req, membershipId) =
        let workflow = pipeline {
            do! authenticate req
            let! model = data.Memberships.Get membershipId
            do! setEndpointPermissions req (canModifyUnit model.UnitId)
            do! authorizeDelete req
            return! data.Memberships.Delete membershipId
        }
        delete req workflow


    // *******************
    // ** Unit Member Tools
    // *******************

    let setMemberToolId id (a:MemberTool) = Ok { a with Id=id } |> async.Return
    let authorizeMemberToolUnitModification req (tool:MemberTool,unitMember:UnitMember) =
        authorize req (canModifyUnitMemberTools unitMember.UnitId) tool
    let permissionMemberToolUnitModification req (tool:MemberTool,unitMember:UnitMember) =
        permission'' req (canModifyUnitMemberTools unitMember.UnitId) tool

    [<FunctionName("MemberToolsGetAll")>]
    [<SwaggerOperation(Summary="List all unit member tools", Tags=[|"Unit Member Tools"|])>]
    [<SwaggerResponse(200, "A collection of unit member tool records", typeof<seq<MemberTool>>)>]
    let memberToolsGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "membertools")>] req) =
        let workflow = pipeline { 
            do! authenticate req
            return! data.MemberTools.GetAll ()
        }
        get req workflow

    [<FunctionName("MemberToolGetById")>]
    [<SwaggerOperation(Summary="Find a unit member tool by ID", Tags=[|"Unit Member Tools"|])>]
    [<SwaggerResponse(200, "A unit member tool record", typeof<MemberTool>)>]
    [<SwaggerResponse(404, "No member tool record was found with the ID provided.", typeof<ErrorModel>)>]
    let memberToolGetById
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "membertools/{memberToolId}")>] req, memberToolId) =
        let workflow = pipeline {
            do! authenticate req
            let! memberTool = data.MemberTools.Get memberToolId
            let! membership = data.Memberships.Get memberTool.MembershipId
            do! setEndpointPermissions req (canModifyUnitMemberTools membership.UnitId)
            return memberTool
        }
        get req workflow

    [<FunctionName("MemberToolCreate")>]
    [<SwaggerOperation(Summary="Create a unit member tool.", Description="<em>Authorization</em>: Unit tool permissions can be created by any unit member that has either the `Owner` or `ManageTools` permission on their unit membership. See also: [Units - List all unit members](#operation/unitGetAllMembers).", Tags=[|"Unit Member Tools"|])>]
    [<SwaggerRequestExample(typeof<MemberTool>, typeof<MembertoolExample>)>]
    [<SwaggerResponse(201, "The newly created unit member tool record", typeof<MemberTool>)>]
    [<SwaggerResponse(400, "The request body was malformed, the tool was missing, or the member was missing.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You are not authorized to modify tools for this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "The specified member/tool does not exist.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The provided member already has access to the provided tool.", typeof<ErrorModel>)>]
    let memberToolCreate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "membertools")>] req) =
        let workflow = pipeline {
            do! authenticate req
            let! body = deserializeBody<MemberTool> req
            let! membership = data.Memberships.Get body.MembershipId
            do! setEndpointPermissions req (canModifyUnitMemberTools membership.UnitId)
            do! authorizeCreate req
            return! data.MemberTools.Create { body with Id=0 }
        }
        create req workflow

    [<FunctionName("MemberToolUpdate")>]
    [<SwaggerOperation(Summary="Update a unit member tool.", Description="<em>Authorization</em>: Unit tool permissions can be updated by any unit member that has either the `Owner` or `ManageTools` permission on their unit membership. See also: [Units - List all unit members](#operation/unitGetAllMembers).", Tags=[|"Unit Member Tools"|])>]
    [<SwaggerRequestExample(typeof<MemberTool>, typeof<MembertoolExample>)>]
    [<SwaggerResponse(200, "The update unit member tool record.", typeof<MemberTool>)>]
    [<SwaggerResponse(400, "The request body was malformed, the tool was missing, or the member was missing.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You are not authorized to modify tools for this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No member tool was found with the provided ID, or the specified member/tool does not exist.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The provided member already has access to the provided tool.", typeof<ErrorModel>)>]
    let memberToolUpdate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "membertools/{memberToolId}")>] req, memberToolId) =
        let workflow = pipeline {
            do! authenticate req
            let! body = deserializeBody<MemberTool> req
            do! ensureExists data.MemberTools.Get memberToolId
            let! membership = data.Memberships.Get body.MembershipId
            do! setEndpointPermissions req (canModifyUnitMemberTools membership.UnitId)
            do! authorizeUpdate req
            return! data.MemberTools.Update { body with Id=memberToolId }
        }
        update req workflow


    [<FunctionName("MemberToolDelete")>]
    [<SwaggerOperation(Summary="Delete a unit member tool.", Description="<em>Authorization</em>: Unit tool permissions can be deleted by any unit member that has either the `Owner` or `ManageTools` permission on their unit membership. See also: [Units - List all unit members](#operation/unitGetAllMembers).", Tags=[|"Unit Member Tools"|])>]
    [<SwaggerResponse(204)>]
    [<SwaggerResponse(403, "You are not authorized to modify this member tool.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No member tool was found with the ID provided.", typeof<ErrorModel>)>]
    let memberToolDelete
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "membertools/{memberToolId}")>] req, memberToolId) =
        let workflow = pipeline {
            do! authenticate req
            let! memberTool = data.MemberTools.Get memberToolId
            let! membership = data.Memberships.Get memberTool.MembershipId
            do! setEndpointPermissions req (canModifyUnitMemberTools membership.UnitId)
            do! authorizeDelete req
            return! data.MemberTools.Delete memberToolId
        }
        delete req workflow


    // *****************
    // ** Departments
    // *****************


    [<FunctionName("DepartmentGetAll")>]
    [<SwaggerOperation(Summary="List all departments.", Description="""Get a list of university departments. Available filters include:<br/>
    <ul><li><strong>q</strong>: filter by department name/code, ex: 'Parks' or 'PA-PARK'</ul></br>""", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, "A collection of department records", typeof<seq<Department>>)>]
    [<OptionalQueryParameter("q", typeof<string>)>]
    let departmentGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments")>] req) =
        let workflow = pipeline {
            do! authenticate req
            let! query = tryQueryParam req "q"
            return! data.Departments.GetAll query
        }
        get req workflow

    [<FunctionName("DepartmentGetId")>]
    [<SwaggerOperation(Summary="Find a department by ID.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, "A department record", typeof<Department>)>]
    [<SwaggerResponse(404, "No department was found with the ID provided.", typeof<ErrorModel>)>]
    let departmentGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}")>] req, departmentId) =
        let workflow = pipeline {
            do! authenticate req
            return! data.Departments.Get departmentId
        }
        get req workflow

    [<FunctionName("DepartmentGetAllMemberUnits")>]
    [<SwaggerOperation(Summary="List a department's member units.", Description="A member unit contains people that have an HR relationship with the department.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, "A collection of unit records", typeof<seq<Unit>>)>]
    [<SwaggerResponse(404, "No department was found with the ID provided.", typeof<ErrorModel>)>]
    let departmentGetMemberUnits
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}/memberUnits")>] req, departmentId) =
        let workflow = pipeline {
            do! authenticate req
            do! ensureExists data.Departments.Get departmentId
            return! data.Departments.GetMemberUnits departmentId
        }
        get req workflow

    [<FunctionName("DepartmentGetAllSupportingUnits")>]
    [<SwaggerOperation(Summary="List a department's supporting units.", Description="A supporting unit provides IT services for the department.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, "A collection of department relationship records", typeof<seq<SupportRelationship>>)>]
    [<SwaggerResponse(404, "No department was found with the ID provided.", typeof<ErrorModel>)>]
    let departmentGetSupportingUnits
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}/supportingUnits")>] req, departmentId) =
        let workflow = pipeline {
            do! authenticate req
            do! ensureExists data.Departments.Get departmentId
            return! data.Departments.GetSupportingUnits departmentId
        }
        get req workflow


    // ************************
    // ** Department Support Relationships
    // ************************

    let setRelationshipId id (a:SupportRelationship) = Ok { a with Id=id } |> async.Return

    [<FunctionName("SupportRelationshipsGetAll")>]
    [<SwaggerOperation(Summary="List all unit-department support relationships.", Tags=[|"Support Relationships"|])>]
    [<SwaggerResponse(200, "A collection of department support relationship records", typeof<SupportRelationship seq>)>]
    let supportRelationshipsGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "supportRelationships")>] req) =
        let workflow = pipeline {
            do! authenticate req
            return! data.SupportRelationships.GetAll ()
        }
        get req workflow

    [<FunctionName("SupportRelationshipsGetId")>]
    [<SwaggerOperation(Summary="Find a unit-department support relationships by ID", Tags=[|"Support Relationships"|])>]
    [<SwaggerResponse(200, "A department support relationship record", typeof<SupportRelationship>)>]
    [<SwaggerResponse(404, "No department support relationship was found with the ID provided.", typeof<ErrorModel>)>]
    let supportRelationshipsGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "supportRelationships/{relationshipId}")>] req, relationshipId) =
        let workflow = pipeline {
            do! authenticate req
            let! relationship = data.SupportRelationships.Get relationshipId
            return! permissionRelationUnitModification req relationship
        }
        get req workflow

    [<FunctionName("SupportRelationshipsCreate")>]
    [<SwaggerOperation(Summary="Create a unit-department support relationship.", Description="<em>Authorization</em>: Support relationships can be created by any unit member that has either the `Owner` or `ManageMembers` permission on their unit membership. See also: [Units - List all unit members](#operation/unitGetAllMembers).", Tags=[|"Support Relationships"|])>]
    [<SwaggerRequestExample(typeof<SupportRelationshipRequest>, typeof<SupportRelationshipRequestExample>)>]
    [<SwaggerResponse(201, "The newly created department support relationship record", typeof<SupportRelationship>)>]
    [<SwaggerResponse(400, "The request body was malformed, the unitId and/or departmentId field was missing.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "The the specified unit and/or department does not exist.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The provided unit already has a support relationship with the provided department.", typeof<ErrorModel>)>]
    let supportRelationshipsCreate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "supportRelationships")>] req) =
        let workflow = pipeline {
            do! authenticate req
            let! body = deserializeBody<SupportRelationship> req
            let safeBody = { body with Id = 0}
            let! authdBody = authorizeRelationUnitModification req safeBody
            return! data.SupportRelationships.Create authdBody         
        }
        create req workflow

    [<FunctionName("SupportRelationshipsUpdate")>]
    [<SwaggerOperation(Summary="Update a unit-department support relationship.", Description="<em>Authorization</em>: Support relationships can be modified by any unit member that has either the `Owner` or `ManageMembers` permission on their unit membership. See also: [Units - List all unit members](#operation/unitGetAllMembers).", Tags=[|"Support Relationships"|])>]
    [<SwaggerRequestExample(typeof<SupportRelationshipRequest>, typeof<SupportRelationshipRequestExample>)>]
    [<SwaggerResponse(200, "The updated department support relationship record", typeof<SupportRelationship>)>]
    [<SwaggerResponse(400, "The request body was malformed, the unitId and/or departmentId field was missing.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No support relationship was found with the ID provided, or the specified unit and/or department does not exist.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The provided unit already has a support relationship with the provided department.", typeof<ErrorModel>)>]
    let supportRelationshipsUpdate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "supportRelationships/{relationshipId}")>] req, relationshipId) =
        let workflow = pipeline {
            do! authenticate req
            let! body = deserializeBody<SupportRelationship> req
            let safeBody = { body with Id=relationshipId }
            let! _ = ensureEntityExistsForModel data.SupportRelationships.Get safeBody
            let! authdBody = authorizeRelationUnitModification req safeBody
            return! data.SupportRelationships.Update authdBody
        }
        update req workflow

    [<FunctionName("SupportRelationshipsDelete")>]
    [<SwaggerOperation(Summary="Delete a unit-department support relationship.", Description="<em>Authorization</em>: Support relationships can be deleted by any unit member that has either the `Owner` or `ManageMembers` permission on their unit membership. See also: [Units - List all unit members](#operation/unitGetAllMembers).", Tags=[|"Support Relationships"|])>]
    [<SwaggerResponse(204)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No support relationship was found with the ID provided.", typeof<ErrorModel>)>]
    let supportRelationshipsDelete
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "supportRelationships/{relationshipId}")>] req, relationshipId) =
        let workflow = pipeline {
            do! authenticate req
            let! model = data.SupportRelationships.Get relationshipId
            let! authdModel = authorizeRelationUnitModification req model
            return! data.SupportRelationships.Delete authdModel
        }
        delete req workflow


    // ********************
    // ** Tool Permissions
    // ********************

    [<FunctionName("ToolPermissionsGetAll")>]
    [<SwaggerIgnore>]
    // [<SwaggerOperation(Summary="List all person-tool-department relationships.", Tags=[|"Tool Permissions"|])>]
    // [<SwaggerResponse(200, "A collection of tool permission records", typeof<ToolPermission seq>)>]
    let toolPermissionsGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "toolPermissions")>] req) =
        let workflow = pipeline {
            do! authenticate req
            return! data.Tools.GetAllPermissions ()
        }
        get req workflow


    // *****************
    // ** Buildings
    // *****************

    [<FunctionName("BuildingsGetAll")>]
    [<SwaggerOperation(Summary="List all buildings.", Description="""Get a list of university buildings. Available filters include:<br/>
    <ul><li><strong>q</strong>: filter by building name/description/address, ex: 'ballantine' or 'bloomington'</ul></br>""", Tags=[|"Buildings"|])>]
    [<SwaggerResponse(200, "A collection of building records", typeof<seq<Building>>)>]
    [<OptionalQueryParameter("q", typeof<string>)>]
    let buildingGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "buildings")>] req) =
        let workflow = pipeline {
            do! authenticate req
            let! query = tryQueryParam req "q"
            return! data.Buildings.GetAll query
        }
        get req workflow

    [<FunctionName("BuildingGetId")>]
    [<SwaggerOperation(Summary="Find a department by ID.", Tags=[|"Buildings"|])>]
    [<SwaggerResponse(200, "A building record", typeof<Department>)>]
    [<SwaggerResponse(404, "No building was found with the ID provided.", typeof<ErrorModel>)>]
    let BuildingtGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "buildings/{buildingId}")>] req, buildingId) =
        let workflow = pipeline {
            do! authenticate req
            return! data.Buildings.Get buildingId
        }
        get req workflow    

    [<FunctionName("BuildingGetAllSupportingUnits")>]
    [<SwaggerOperation(Summary="List a buildings's supporting units.", Description="A supporting unit provides IT services for the building.", Tags=[|"Buildings"|])>]
    [<SwaggerResponse(200, "A collection of building relationship records", typeof<seq<BuildingRelationship>>)>]
    [<SwaggerResponse(404, "No building was found with the ID provided.", typeof<ErrorModel>)>]
    let buildingGetSupportingUnits
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "buildings/{buildingId}/supportingUnits")>] req, buildingId) =
        let workflow = pipeline {
            do! authenticate req
            let! building = data.Buildings.Get buildingId
            return! data.Buildings.GetSupportingUnits building
        }
        get req workflow

    // *********************************
    // ** Building Support Relationships
    // *********************************

    let setBuildingRelationshipId id (a:BuildingRelationship) = Ok { a with Id=id } |> async.Return

    [<FunctionName("BuildingRelationshipsGetAll")>]
    [<SwaggerOperation(Summary="List all unit-building support relationships.", Tags=[|"Building Relationships"|])>]
    [<SwaggerResponse(200, "A collection of building support relationship records", typeof<SupportRelationship seq>)>]
    let buildingRelationshipsGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "buildingRelationships")>] req) =
        let workflow = pipeline {
            do! authenticate req
            return! data.BuildingRelationships.GetAll ()
        }
        get req workflow

    [<FunctionName("BuildingRelationshipsGetId")>]
    [<SwaggerOperation(Summary="Find a unit-building support relationships by ID", Tags=[|"Building Relationships"|])>]
    [<SwaggerResponse(200, "A building support relationship record", typeof<SupportRelationship>)>]
    [<SwaggerResponse(404, "No support relationship was found with the ID provided.", typeof<ErrorModel>)>]
    let buildingRelationshipsGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "buildingRelationships/{relationshipId}")>] req, relationshipId) =
        let workflow = pipeline {
            do! authenticate req
            let! relationship = data.BuildingRelationships.Get relationshipId
            return! permissionRelationUnitModification req relationship
        }

        get req workflow

    [<FunctionName("BuildingRelationshipsCreate")>]
    [<SwaggerOperation(Summary="Create a unit-building support relationship.", Description="<em>Authorization</em>: Support relationships can be created by any unit member that has either the `Owner` or `ManageMembers` permission on their unit membership. See also: [Units - List all unit members](#operation/unitGetAllMembers).", Tags=[|"Building Relationships"|])>]
    [<SwaggerRequestExample(typeof<SupportRelationshipRequest>, typeof<SupportRelationshipRequestExample>)>]
    [<SwaggerResponse(201, "The newly created building support relationship record", typeof<SupportRelationship>)>]
    [<SwaggerResponse(400, "The request body was malformed, the unitId and/or buildingId field was missing.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "The specified unit and/or building does not exist.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The provided unit already has a support relationship with the provided building.", typeof<ErrorModel>)>]
    let buildingRelationshipsCreate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "buildingRelationships")>] req) =
        let workflow = pipeline {
            do! authenticate req
            let! body = deserializeBody<BuildingRelationship> req
            let safeBody = { body with Id=0 }
            let! authdBody = authorizeRelationUnitModification req safeBody
            return! data.BuildingRelationships.Create authdBody
        }
        create req workflow

    [<FunctionName("BuildingRelationshipsUpdate")>]
    [<SwaggerOperation(Summary="Update a unit-building support relationship.", Description="<em>Authorization</em>: Support relationships can be modified by any unit member that has either the `Owner` or `ManageMembers` permission on their unit membership. See also: [Units - List all unit members](#operation/unitGetAllMembers).", Tags=[|"Building Relationships"|])>]
    [<SwaggerRequestExample(typeof<SupportRelationshipRequest>, typeof<SupportRelationshipRequestExample>)>]
    [<SwaggerResponse(200, "The updated building support relationship record", typeof<SupportRelationship>)>]
    [<SwaggerResponse(400, "The request body was malformed, the unitId and/or buildingId field was missing.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No support relationship was found with the ID provided, or the specified unit and/or building does not exist.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The provided unit already has a support relationship with the provided building.", typeof<ErrorModel>)>]
    let buildingRelationshipsUpdate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "buildingRelationships/{relationshipId}")>] req, relationshipId) =
        let workflow = pipeline {
            do! authenticate req
            let! body = deserializeBody<BuildingRelationship> req
            let safeBody = { body with Id=relationshipId }
            let! _ = ensureEntityExistsForModel data.BuildingRelationships.Get safeBody
            let! authdBody = authorizeRelationUnitModification req safeBody
            return! data.BuildingRelationships.Update authdBody
        }
        update req workflow

    [<FunctionName("BuildingRelationshipsDelete")>]
    [<SwaggerOperation(Summary="Delete a unit-building support relationship.", Description="<em>Authorization</em>: Support relationships can be deleted by any unit member that has either the `Owner` or `ManageMembers` permission on their unit membership. See also: [Units - List all unit members](#operation/unitGetAllMembers).", Tags=[|"Building Relationships"|])>]
    [<SwaggerResponse(204)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No building support relationship was found with the ID provided.", typeof<ErrorModel>)>]
    let buildingRelationshipsDelete
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "buildingRelationships/{relationshipId}")>] req, relationshipId) =
        let workflow = pipeline {
            do! authenticate req
            let! relationship = data.BuildingRelationships.Get relationshipId
            let! _ = authorizeRelationUnitModification req relationship
            return! data.BuildingRelationships.Delete relationship
        }
        delete req workflow

    // *********************************
    // ** Legacy Endpoints
    // *********************************
    
    [<FunctionName("LegacyLspList")>]
    [<SwaggerIgnore>]
    let legacyLspList
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "LspdbWebService.svc/LspList")>] req) =
        let workflow = pipeline { return! data.Legacy.GetLspList () }
        getXml req workflow

    [<FunctionName("LegacyLspDepartments")>]
    [<SwaggerIgnore>]
    let legacyLspDepartments
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "LspdbWebService.svc/LspDepartments/{netid}")>] req, netid) =
        let workflow = pipeline { return! data.Legacy.GetLspDepartments netid }
        getXml req workflow    

    [<FunctionName("LegacyDepartmentLsps")>]
    [<SwaggerIgnore>]
    let legacyDepartmentLsps
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "LspdbWebService.svc/LspsInDept/{department}")>] req, department) =
        let workflow = pipeline { return! data.Legacy.GetDepartmentLsps department }
        getXml req workflow   

