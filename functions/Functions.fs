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
            DatabaseRepository.Repository(config.DbConnectionString, config.SharedSecret)
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
        Ok req |> async.Return
        
    /// Logging: Add the authenticated user to the request properties
    let recordAuthenticatedUser req (netid:NetId) =
        addProperty req WorkflowUser netid
        ok netid

    let recordUserPermissions req model perms =
        addProperty req WorkflowPermissions perms
        ok model
    
    /// Log and rethrow an unhandled exception.
    let handle req exn = 
        logFatal log req exn
        raise exn

    let authenticate req = 
        authenticateRequest publicKey req
        >>= recordAuthenticatedUser req

    let permission req authFn model =
        let user = getProperty req WorkflowUser
        determineAuthenticatedUserPermissions data.Authorization authFn user
        >>= recordUserPermissions req model

    let authorize req authFn model =
        authenticate req
        >>= permission req authFn
        >>= authorizeRequest data.Authorization model authFn 

    /// Execute a workflow for an authenticated user and return a response.
    let execute (successStatus:Status) (req:HttpRequestMessage) workflow  = 
        async {
            try
                let workflow = timestamp >=> workflow
                let! result = workflow(req)
                return createResponse req config log successStatus result
            with exn -> return handle req exn
        } |> Async.StartAsTask

    let get req workflow = execute Status.OK req workflow
    let create req workflow = execute Status.Created req workflow
    let update req workflow = execute Status.OK req workflow
    let delete req workflow = execute Status.NoContent req workflow


    let inline ensureEntityExistsForModel (getter:Id->Async<Result<'a,Error>>) model : Async<Result<'b,Error>> = async {
        let! result = getter (identity model)
        match result with 
        | Ok _ -> return Ok model
        | Error msg -> return Error msg
    } 

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
        contentResponse req "*" status content

    [<FunctionName("PingGet")>]
    [<SwaggerIgnore>]
    let ping
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")>] req) =
        new StringContent("Pong!") 
        |> contentResponse req "*" Status.OK

    // *****************
    // ** Authentication
    // *****************

    [<FunctionName("AuthGet")>]
    [<SwaggerOperation(Summary="Get OAuth JWT", Description="Exchanges a UAA OAuth code for an application-scoped JWT. The JWT is required to make authenticated requests to this API.", Tags=[|"Authentication"|])>]
    [<SwaggerResponse(200, "A JWT access token scoped for the IT People API.", typeof<JwtResponse>)>]
    [<SwaggerResponse(400, "The provided code was missing, invalid, or expired.", typeof<ErrorModel>)>]
    let authGet
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "auth")>] req) =

        // workflow partials
        let createUaaTokenRequest = createUaaTokenRequest config
        let requestTokenFromUaa = postAsync<JwtResponse> config.OAuth2TokenUrl
        let resolveAppUserId = data.People.TryGetId
        let recordLoginAndReturnJwt req jwt =
            decodeJwt publicKey jwt.access_token
            >>= recordAuthenticatedUser req 
            >>= (fun _ -> ok jwt)

        // workflow definition
        let workflow =  
            queryParam "oauth_code"
            >=> createUaaTokenRequest
            >=> requestTokenFromUaa
            >=> recordLoginAndReturnJwt req

        get req workflow

    // *****************
    // ** People
    // *****************

    [<FunctionName("PeopleGetAll")>]
    [<SwaggerOperation(Summary="Search IT people", Description="""Search for IT people. Available filters include:<br/>
    <ul><li><strong>q</strong>: filter by name/netid, ex: 'Ron' or 'rswanso'
    <li><strong>class</strong>: filter by job classification/responsibility, ex: 'UserExperience' or 'UserExperience,WebAdminDevEng'
    <li><strong>interest</strong>: filter by interest, ex: 'serverless' or 'node,lambda'
    <li><strong>role</strong>: filter by unit role, ex: 'Leader' or 'Leader,Member'
    <li><strong>permission</strong>: filter by unit permissions, ex: 'Owner' or 'Owner,ManageMembers'
    </ul></br>
    Search results are unioned within a filter and intersected across filters. For example, 'interest=node,lambda' will 
    return people with an interest in either 'node' OR 'lambda', whereas `role=ItLeadership&interest=node` will only return
    people who are both in 'ItLeadership' AND have an interest in 'node'.""", Tags=[|"People"|])>]
    [<SwaggerResponse(200, "A collection of person records.", typeof<seq<Person>>)>]
    [<OptionalQueryParameter("q", typeof<string>)>]
    [<OptionalQueryParameter("class", typeof<seq<Responsibilities>>)>]
    [<OptionalQueryParameter("interest", typeof<seq<string>>)>]
    [<OptionalQueryParameter("role", typeof<seq<Role>>)>]
    [<OptionalQueryParameter("permission", typeof<seq<UnitPermissions>>)>]
    let peopleGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people")>] req) =
        let getQueryParams _ =
            let delimiters = [|','; ';'; '+'|]
            let nonzero x = x <> 0
            let query =
                match tryQueryParam' req "q" with
                | Some(str) -> str
                | None -> ""
            let classes =
                let parseInt s = 
                    try Enum.Parse<Responsibilities>(s, true) |> int 
                    with _ -> 0 
                match tryQueryParam' req "class" with
                | Some(str) -> str.Split delimiters |> Seq.sumBy (trim >> parseInt)
                | None -> 0
            let interests = 
                match tryQueryParam' req "interest" with
                | Some(str) -> str.Split delimiters |> Array.map trim
                | None -> Array.empty                
            let roles = 
                let parseInt s = 
                    try Enum.Parse<Role>(s, true) |> int 
                    with _ -> 0 
                match tryQueryParam' req "role" with
                | Some(str) -> str.Split delimiters |> Seq.map (trim >> parseInt) |> Seq.filter nonzero |> Seq.toArray
                | None -> [||]
            let permissions = 
                let parseInt s = 
                    try Enum.Parse<UnitPermissions>(s, true) |> int 
                    with _ -> 0 
                match tryQueryParam' req "permission" with
                | Some(str) -> str.Split delimiters |> Seq.map (trim >> parseInt) |> Seq.filter nonzero |> Seq.toArray
                | None -> [||]
            { Query=query
              Classes=classes
              Interests=interests
              Roles=roles
              Permissions=permissions } |> ok

        let workflow = 
            authenticate 
            >=> getQueryParams
            >=> data.People.GetAll

        get req workflow

    let ensurePersonInDirectory =
        data.People.TryGetId
        >=> fun (netid, idOption) ->
            match idOption with
            | Some(id) -> 
                data.People.GetById id
            | None ->  
                data.People.GetHr netid
                >>= data.People.Create

    [<FunctionName("PeopleLookupAll")>]
    [<SwaggerOperation(Summary="Search all staff", Description="Search for staff, including IT People, by name or username (netid).", Tags=[|"People"|])>]
    [<SwaggerResponse(200, "A collection of person records.", typeof<seq<Person>>)>]
    [<OptionalQueryParameter("q", typeof<string>)>]
    let peopleLookupAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people-lookup")>] req) =
        let workflow = 
            authenticate 
            >=> fun _ -> queryParam "q" req
            >=> data.People.GetAllWithHr
        get req workflow

    [<FunctionName("PeopleGetById")>]
    [<SwaggerOperation(Summary="Find a person by ID", Tags=[|"People"|])>]
    [<SwaggerResponse(200, "A person record.", typeof<Person>)>]
    [<SwaggerResponse(404, "No person was found with the ID provided.", typeof<ErrorModel>)>]
    let peopleGetById
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{personId}")>] req, personId:string) =

        let getPerson = 
            match personId with
            | Int id -> fun _ -> data.People.GetById id
            | _      -> fun _ -> ensurePersonInDirectory personId

        let workflow = 
            authenticate
            >=> getPerson
            >=> fun p -> permission req (canModifyPerson p.Id) p

        get req workflow

    [<FunctionName("PeopleGetAllMemberships")>]
    [<SwaggerOperation(Summary="List a person's unit memberships", Description="List all units for which this person does IT work.", Tags=[|"People"|])>]
    [<SwaggerResponse(200, "A collection of units of which this person is a member.", typeof<seq<UnitMember>>)>]
    [<SwaggerResponse(404, "No person was found with the ID provided.", typeof<ErrorModel>)>]
    let peopleGetAllMemberships
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "people/{personId}/memberships")>] req, personId) =
        let workflow = 
            authenticate
            >=> fun _ -> data.People.GetById personId
            >=> fun _ -> data.People.GetMemberships personId
        get req workflow

    let setPersonId id (a:PersonRequest) = ok {a with Id=id}

    [<FunctionName("PersonPut")>]
    [<SwaggerOperation(Summary="Update a person's location, expertise, and responsibilities/job classes.", Tags=[|"People"|])>]
    [<SwaggerRequestExample(typeof<UnitRequest>, typeof<PersonRequestExample>)>]
    [<SwaggerResponse(200, "A record of the updated person", typeof<Person>)>]
    [<SwaggerResponse(400, "The request body is malformed.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You do not have permission to modify this person.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No person was found with the ID provided.", typeof<ErrorModel>)>]
    let personPut
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "people/{personId}")>] req, personId) =
        let workflow =
            deserializeBody<PersonRequest>
            >=> setPersonId personId
            >=> ensureEntityExistsForModel data.People.GetById      
            >=> authorize req (canModifyPerson personId)
            >=> data.People.Update
        update req workflow


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
        let workflow = 
            authenticate 
            >=> fun _ -> tryQueryParam req "q"
            >=> data.Units.GetAll
            >=> permission req canCreateDeleteUnit
        get req workflow

    [<FunctionName("UnitGetId")>]
    [<SwaggerOperation(Summary="Find a unit by ID.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A unit record.", typeof<Unit>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    let unitGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}")>] req, unitId) =
        let workflow = 
            authenticate
            >=> fun _ -> data.Units.Get unitId 
            >=> permission req (canModifyUnit unitId)
        get req workflow
            
    [<FunctionName("UnitPost")>]
    [<SwaggerOperation(Summary="Create a unit.", Tags=[|"Units"|])>]
    [<SwaggerRequestExample(typeof<UnitRequest>, typeof<UnitRequestExample>)>]
    [<SwaggerResponse(201, "A record of the newly created unit.", typeof<Unit>)>]
    [<SwaggerResponse(400, "The request body is malformed, or the unit name is missing.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The request body specifies a name that is already in use by another unit.", typeof<ErrorModel>)>]
    let unitPost
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "units")>] req) =
        let workflow =
            deserializeBody<Unit>
            >=> setUnitId 0      
            >=> authorize req canCreateDeleteUnit
            >=> unitValidator.ValidForCreate
            >=> data.Units.Create
        create req workflow

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
        let workflow =
            deserializeBody<Unit>
            >=> setUnitId unitId
            >=> ensureEntityExistsForModel data.Units.Get      
            >=> authorize req (canModifyUnit unitId)
            >=> unitValidator.ValidForUpdate
            >=> data.Units.Update
        update req workflow

    [<FunctionName("UnitDelete")>]
    [<SwaggerOperation(Summary="Delete a unit.", Tags=[|"Units"|])>]
    [<SwaggerResponse(204)>]
    [<SwaggerResponse(403, "You do not have permission to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The unit has children. These must be reassigned prior to deletion.", typeof<ErrorModel>)>]
    let unitDelete
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "units/{unitId}")>] req, unitId) =
        let workflow =
            fun _ -> data.Units.Get unitId
            >=> authorize req canCreateDeleteUnit
            >=> unitValidator.ValidForDelete
            >=> data.Units.Delete
        delete req workflow

    let getUnitMemberFetchOptions unitId netid  = async {
        let! unitResult = data.Units.Get unitId
        match unitResult with
        | Ok(unit) -> 
            let! canModifyResult = canModifyUnit unit.Id data.Authorization netid
            match canModifyResult with
            | Ok(true) -> return MembersWithNotes(unit) |> Ok
            | _ -> return MembersWithoutNotes(unit) |> Ok
        | Error(msg) -> return Error(msg)
    }
    
    [<FunctionName("UnitGetAllMembers")>]
    [<SwaggerOperation(Summary="List all unit members", Description="List all people who do IT work for this unit along with any vacant positions.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A collection of membership records.", typeof<seq<UnitMember>>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    let unitGetAllMembers
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/members")>] req, unitId) =
        let workflow =
            authenticate
            >=> getUnitMemberFetchOptions unitId
            >=> data.Units.GetMembers
            >=> permission req (canModifyUnit unitId)
        get req workflow

    [<FunctionName("UnitGetAllSupportedDepartments")>]
    [<SwaggerOperation(Summary="List all supported departments", Description="List all departments that receive IT support from this unit.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A collection of department records.", typeof<seq<UnitMember>>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    let unitGetAllSupportedDepartments
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/supportedDepartments")>] req, unitId) =
        let workflow = 
            authenticate
            >=> fun _ ->  data.Units.Get unitId
            >=> data.Units.GetSupportedDepartments
            >=> permission req (canModifyUnit unitId)
        get req workflow

    [<FunctionName("UnitGetAllChildren")>]
    [<SwaggerOperation(Summary="List all unit children", Description="List all units that fall below this unit in an organizational hierarchy.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A collection of unit records.", typeof<seq<Unit>>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    let unitGetAllChildren
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/children")>] req, unitId) =
        let workflow = 
            authenticate
            >=> fun _ ->  data.Units.Get unitId
            >=> data.Units.GetChildren
            >=> permission req (canModifyUnit unitId)
        get req workflow

    [<FunctionName("UnitGetAllTools")>]
    [<SwaggerOperation(Summary="List all unit tools", Description="List all tools that are available to this unit.", Tags=[|"Units"|])>]
    [<SwaggerResponse(200, "A collection of tool records.", typeof<seq<Tool>>)>]
    [<SwaggerResponse(404, "No unit was found with the ID provided.", typeof<ErrorModel>)>]
    let unitGetAllTools
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "units/{unitId}/tools")>] req, unitId) =
        let workflow = 
            authenticate
            >=> fun _ ->  data.Units.Get unitId
            >=> fun _ -> data.Tools.GetAll ()
            >=> permission req (canModifyUnit unitId)
        get req workflow

    // *******************
    // ** Unit Memberships
    // *******************

    let membershipValidator = membershipValidator(data)
    let setMembershipId id (a:UnitMember) = Ok { a with Id=id } |> async.Return
    let authorizeMembershipUnitModification req (membership:UnitMember) =
        authorize req (canModifyUnit membership.UnitId) membership
    let permissionMembershipUnitModification req (membership:UnitMember) =
        permission req (canModifyUnit membership.UnitId) membership     

    [<FunctionName("MemberGetAll")>]
    [<SwaggerOperation(Summary="List all unit memberships", Tags=[|"Unit Memberships"|])>]
    [<SwaggerResponse(200, "A collection of unit membership records", typeof<seq<UnitMember>>)>]
    let memberGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "memberships")>] req) =
        let workflow = 
            authenticate
            >=> fun _ -> data.Memberships.GetAll ()
        get req workflow

    [<FunctionName("MemberGetById")>]
    [<SwaggerOperation(Summary="Find a unit membership by ID", Tags=[|"Unit Memberships"|])>]
    [<SwaggerResponse(200, "A unit membership record", typeof<UnitMember>)>]
    [<SwaggerResponse(404, "No membership was found with the ID provided.", typeof<ErrorModel>)>]
    let memberGetById
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "memberships/{membershipId}")>] req, membershipId) =
        let workflow = 
            authenticate
            >=> fun _ -> data.Memberships.Get membershipId
            >=> permissionMembershipUnitModification req
        get req workflow

    let ensureUnitMemberInDirectory (um:UnitMember) =
        match (um.PersonId, um.NetId) with
        | (None, None) 
        | (Some(0), None) -> Ok um |> ar // This position is a vacancy.
        | (None, Some(netid))
        | (Some(0), Some(netid)) -> // We don't have this person in the directory. Add them now.
            ensurePersonInDirectory netid
            >>= fun person -> ok { um with PersonId=Some(person.Id)}
        | (Some(_), _) -> Ok um |> ar // This position is filled by someone in the directory.

    [<FunctionName("MemberCreate")>]
    [<SwaggerOperation(Summary="Create a unit membership.", Tags=[|"Unit Memberships"|])>]
    [<SwaggerRequestExample(typeof<UnitMemberRequest>, typeof<MembershipRequestExample>)>]
    [<SwaggerResponse(201, "The newly created unit membership record", typeof<UnitMember>)>]
    [<SwaggerResponse(400, "The request body was malformed, the unitId field was missing, or the specified unit does not exist.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The provided person is already a member of the provided unit.", typeof<ErrorModel>)>]
    let memberCreate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "memberships")>] req) =
        let workflow = 
            deserializeBody<UnitMember>
            >=> setMembershipId 0
            >=> authorizeMembershipUnitModification req
            >=> ensureUnitMemberInDirectory
            >=> membershipValidator.ValidForCreate
            >=> data.Memberships.Create
        create req workflow

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
        let workflow = 
            deserializeBody<UnitMember>
            >=> setMembershipId membershipId
            >=> ensureEntityExistsForModel data.Memberships.Get
            >=> authorizeMembershipUnitModification req
            >=> ensureUnitMemberInDirectory
            >=> membershipValidator.ValidForUpdate
            >=> data.Memberships.Update
        update req workflow
  
    [<FunctionName("MemberDelete")>]
    [<SwaggerOperation(Summary="Delete a unit membership.", Tags=[|"Unit Memberships"|])>]
    [<SwaggerResponse(204)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No membership was found with the ID provided.", typeof<ErrorModel>)>]
    let memberDelete
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "memberships/{membershipId}")>] req, membershipId) =
        let workflow =
            fun _ -> data.Memberships.Get membershipId
            >=> authorizeMembershipUnitModification req
            >=> membershipValidator.ValidForDelete
            >=> data.Memberships.Delete
        delete req workflow


    // *******************
    // ** Unit Member Tools
    // *******************

    let memberToolValidator = memberToolValidator(data)
    let setMemberToolId id (a:MemberTool) = Ok { a with Id=id } |> async.Return
    let authorizeMemberToolUnitModification req (tool:MemberTool,unitMember:UnitMember) =
        authorize req (canModifyUnitMemberTools unitMember.UnitId) tool
    let permissionMemberToolUnitModification req (tool:MemberTool,unitMember:UnitMember) =
        permission req (canModifyUnitMemberTools unitMember.UnitId) tool

    [<FunctionName("MemberToolsGetAll")>]
    [<SwaggerOperation(Summary="List all unit member tools", Tags=[|"Unit Member Tools"|])>]
    [<SwaggerResponse(200, "A collection of unit member tool records", typeof<seq<MemberTool>>)>]
    let memberToolsGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "membertools")>] req) =
        let workflow = 
            authenticate
            >=> fun _ -> data.MemberTools.GetAll ()
        get req workflow

    [<FunctionName("MemberToolGetById")>]
    [<SwaggerOperation(Summary="Find a unit member tool by ID", Tags=[|"Unit Member Tools"|])>]
    [<SwaggerResponse(200, "A unit member tool record", typeof<MemberTool>)>]
    [<SwaggerResponse(404, "No member tool record was found with the ID provided.", typeof<ErrorModel>)>]
    let memberToolGetById
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "membertools/{memberToolId}")>] req, memberToolId) =
        let workflow = 
            authenticate
            >=> fun _ -> data.MemberTools.Get memberToolId
            >=> data.MemberTools.GetMember 
            >=> permissionMemberToolUnitModification req
        get req workflow

    [<FunctionName("MemberToolCreate")>]
    [<SwaggerOperation(Summary="Create a unit member tool.", Tags=[|"Unit Member Tools"|])>]
    [<SwaggerRequestExample(typeof<MemberTool>, typeof<MembertoolExample>)>]
    [<SwaggerResponse(201, "The newly created unit member tool record", typeof<MemberTool>)>]
    [<SwaggerResponse(400, "The request body was malformed, the tool was missing or incorrect, or the member was missing or incorrect.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You are not authorized to modify tools for this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The provided member already has access to the provided tool.", typeof<ErrorModel>)>]
    let memberToolCreate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "membertools")>] req) =
        let workflow =
            deserializeBody<MemberTool>
            >=> setMemberToolId 0
            >=> memberToolValidator.ValidForCreate
            >=> data.MemberTools.GetMember 
            >=> authorizeMemberToolUnitModification req
            >=> data.MemberTools.Create
        create req workflow

    [<FunctionName("MemberToolUpdate")>]
    [<SwaggerOperation(Summary="Update a unit member tool.", Tags=[|"Unit Member Tools"|])>]
    [<SwaggerRequestExample(typeof<MemberTool>, typeof<MembertoolExample>)>]
    [<SwaggerResponse(200, "The update unit member tool record.", typeof<MemberTool>)>]
    [<SwaggerResponse(400, "The request body was malformed, the tool was missing or incorrect, or the member was missing or incorrect.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You are not authorized to modify tools for this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No member tool was found with the provided ID.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The provided member already has access to the provided tool.", typeof<ErrorModel>)>]
    let memberToolUpdate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "membertools/{memberToolId}")>] req, memberToolId) =
        let workflow = 
            deserializeBody<MemberTool>
            >=> setMemberToolId memberToolId
            >=> ensureEntityExistsForModel data.MemberTools.Get
            >=> memberToolValidator.ValidForUpdate
            >=> data.MemberTools.GetMember  
            >=> authorizeMemberToolUnitModification req
            >=> data.MemberTools.Update
        update req workflow


    [<FunctionName("MemberToolDelete")>]
    [<SwaggerOperation(Summary="Delete a member.", Tags=[|"Unit Member Tools"|])>]
    [<SwaggerResponse(204)>]
    [<SwaggerResponse(403, "You are not authorized to modify this member tool.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No member tool was found with the ID provided.", typeof<ErrorModel>)>]
    let memberToolDelete
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "membertools/{memberToolId}")>] req, memberToolId) =
        let workflow =
            fun _ -> data.MemberTools.Get memberToolId
            >=> memberToolValidator.ValidForDelete
            >=> data.MemberTools.GetMember  
            >=> authorizeMemberToolUnitModification req
            >=> data.MemberTools.Delete
        delete req workflow


    // *****************
    // ** Departments
    // *****************


    [<FunctionName("DepartmentGetAll")>]
    [<SwaggerOperation(Summary="List all departments.", Description="Search for departments by name and/or description.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, "A collection of department records", typeof<seq<Department>>)>]
    [<OptionalQueryParameter("q", typeof<string>)>]
    let departmentGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments")>] req) =
        let workflow = 
            authenticate
            >=> fun _ -> tryQueryParam req "q"
            >=> data.Departments.GetAll
        get req workflow

    [<FunctionName("DepartmentGetId")>]
    [<SwaggerOperation(Summary="Find a department by ID.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, "A department record", typeof<Department>)>]
    [<SwaggerResponse(404, "No department was found with the ID provided.", typeof<ErrorModel>)>]
    let departmentGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}")>] req, departmentId) =
        let workflow =
            authenticate
            >=> fun _ -> data.Departments.Get departmentId
        get req workflow

    [<FunctionName("DepartmentGetAllMemberUnits")>]
    [<SwaggerOperation(Summary="List a department's member units.", Description="A member unit contains people that have an HR relationship with the department.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, "A collection of unit records", typeof<seq<Unit>>)>]
    [<SwaggerResponse(404, "No department was found with the ID provided.", typeof<ErrorModel>)>]
    let departmentGetMemberUnits
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}/memberUnits")>] req, departmentId) =
        let workflow = 
            authenticate
            >=> fun _ -> data.Departments.Get departmentId
            >=> data.Departments.GetMemberUnits
        get req workflow

    [<FunctionName("DepartmentGetAllSupportingUnits")>]
    [<SwaggerOperation(Summary="List a department's supporting units.", Description="A member unit contains people that have an HR relationship with the department.", Tags=[|"Departments"|])>]
    [<SwaggerResponse(200, "A collection of unit records", typeof<seq<Unit>>)>]
    [<SwaggerResponse(404, "No department was found with the ID provided.", typeof<ErrorModel>)>]
    let departmentGetSupportingUnits
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "departments/{departmentId}/supportingUnits")>] req, departmentId) =
        let workflow =
            authenticate
            >=> fun _ -> data.Departments.Get departmentId
            >=> data.Departments.GetSupportingUnits
        get req workflow


    // ************************
    // ** Support Relationships
    // ************************

    let setRelationshipId id (a:SupportRelationship) = Ok { a with Id=id } |> async.Return
    let relationshipValidator = supportRelationshipValidator data
    let authorizeSupportRelationshipUnitModification req (rel:SupportRelationship) =
        authorize req (canModifyUnit rel.UnitId) rel
    let permissionSupportRelationshipUnitModification req (rel:SupportRelationship) =
        permission req (canModifyUnit rel.UnitId) rel

    [<FunctionName("SupportRelationshipsGetAll")>]
    [<SwaggerOperation(Summary="List all unit-department support relationships.", Tags=[|"Support Relationships"|])>]
    [<SwaggerResponse(200, "A collection of support relationship records", typeof<SupportRelationship seq>)>]
    let supportRelationshipsGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "supportRelationships")>] req) =
        let workflow = 
            authenticate
            >=> fun _ -> data.SupportRelationships.GetAll ()
        get req workflow

    [<FunctionName("SupportRelationshipsGetId")>]
    [<SwaggerOperation(Summary="Find a unit-department support relationships by ID", Tags=[|"Support Relationships"|])>]
    [<SwaggerResponse(200, "A support relationship record", typeof<SupportRelationship>)>]
    [<SwaggerResponse(404, "No support relationship was found with the ID provided.", typeof<ErrorModel>)>]
    let supportRelationshipsGetId
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "supportRelationships/{relationshipId}")>] req, relationshipId) =
        let workflow =
            authenticate
            >=> fun _ -> data.SupportRelationships.Get relationshipId
            >=> permissionSupportRelationshipUnitModification req
        get req workflow

    [<FunctionName("SupportRelationshipsCreate")>]
    [<SwaggerOperation(Summary="Create a unit-department support relationship", Tags=[|"Support Relationships"|])>]
    [<SwaggerRequestExample(typeof<SupportRelationshipRequest>, typeof<SupportRelationshipRequestExample>)>]
    [<SwaggerResponse(201, "The newly created support relationship record", typeof<SupportRelationship>)>]
    [<SwaggerResponse(400, "The request body was malformed, the unitId and/or departmentId field was missing, or the specified unit and/or department does not exist.", typeof<ErrorModel>)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(409, "The provided unit already has a support relationship with the provided department.", typeof<ErrorModel>)>]
    let supportRelationshipsCreate
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "supportRelationships")>] req) =
        let workflow = 
            deserializeBody<SupportRelationship>
            >=> setRelationshipId 0
            >=> relationshipValidator.ValidForCreate
            >=> authorizeSupportRelationshipUnitModification req
            >=> data.SupportRelationships.Create          
        create req workflow

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
        let workflow = 
            deserializeBody<SupportRelationship>
            >=> setRelationshipId relationshipId
            >=> ensureEntityExistsForModel data.SupportRelationships.Get
            >=> relationshipValidator.ValidForUpdate
            >=> authorizeSupportRelationshipUnitModification req
            >=> data.SupportRelationships.Update
        update req workflow

    [<FunctionName("SupportRelationshipsDelete")>]
    [<SwaggerOperation(Summary="Delete a unit-department support relationship", Tags=[|"Support Relationships"|])>]
    [<SwaggerResponse(204)>]
    [<SwaggerResponse(403, "You are not authorized to modify this unit.", typeof<ErrorModel>)>]
    [<SwaggerResponse(404, "No support relationship was found with the ID provided.", typeof<ErrorModel>)>]
    let supportRelationshipsDelete
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "supportRelationships/{relationshipId}")>] req, relationshipId) =
        let workflow = 
            fun _ -> data.SupportRelationships.Get relationshipId
            >=> relationshipValidator.ValidForDelete
            >=> authorizeSupportRelationshipUnitModification req
            >=> data.SupportRelationships.Delete
        delete req workflow


    // ********************
    // ** Tool Permissions
    // ********************

    [<FunctionName("ToolPermissionsGetAll")>]
    [<SwaggerOperation(Summary="List all person-tool-department relationships.", Tags=[|"Tool Permissions"|])>]
    [<SwaggerResponse(200, "A collection of tool permission records", typeof<ToolPermission seq>)>]
    let toolPermissionsGetAll
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "toolPermissions")>] req) =
        let workflow = 
            authenticate
            >=> fun _ -> data.Tools.GetAllPermissions ()
        get req workflow