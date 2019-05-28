namespace Integration

module ApiErrorTests = 

    open Xunit.Abstractions
    open Xunit
    open TestFixture
    open Core.Fakes
    open Core.Json
    open Core.Types
    open FsUnit.Xunit
    open Newtonsoft.Json
    open System.Net
    open System.Net.Http
    open System.Net.Http.Headers

    let httpClient = new HttpClient()

    let requestFor method route = 
        let uri = sprintf "%s/%s" functionServerUrl route |> System.Uri
        new HttpRequestMessage(method, uri)

    let withAuthentication (request:HttpRequestMessage) = 
        request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", Core.Fakes.uaaJwt.access_token)
        request
    
    let withRawBody str (request:HttpRequestMessage) =
        request.Content <- new StringContent(str, System.Text.Encoding.UTF8, "application/json")
        request

    let withBody obj (request:HttpRequestMessage) =
        withRawBody (JsonConvert.SerializeObject(value=obj, settings=JsonSettings)) request

    let shouldGetResponse expectedStatus (request:HttpRequestMessage) =
        let response = httpClient.SendAsync(request) |> Async.AwaitTask |> Async.RunSynchronously
        response.StatusCode |> should equal expectedStatus
        response

    let shouldGetContent<'T> (expectedContent:'T) (response:HttpResponseMessage) =
        let actualContent = 
            response.Content.ReadAsStringAsync() 
            |> Async.AwaitTask 
            |> Async.RunSynchronously
            |> fun str -> JsonConvert.DeserializeObject<'T>(str, JsonSettings)
        actualContent |> should equal expectedContent
        response

    let evaluateContent<'T> (evalFn:'T -> unit) (response:HttpResponseMessage) =
        let actualContent = 
            response.Content.ReadAsStringAsync() 
            |> Async.AwaitTask 
            |> Async.RunSynchronously
            |> fun str -> JsonConvert.DeserializeObject<'T>(str, JsonSettings)
        actualContent |> evalFn
        response

    type ApiTests(output: ITestOutputHelper)=
        inherit HttpTestBase(output)

        [<Fact>]       
        member __.``People search: netid`` () = 
            requestFor HttpMethod.Get "people?q=rswa"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [swanson]

        [<Fact>]       
        member __.``People search: netid is case insensitive`` () = 
            requestFor HttpMethod.Get "people?q=RSWA"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [swanson]

        [<Fact>]       
        member __.``People search: name`` () = 
            requestFor HttpMethod.Get "people?q=Ron"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [swanson]

        [<Fact>]       
        member __.``People search: name is case insensitive`` () = 
            requestFor HttpMethod.Get "people?q=RON"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [swanson]

        [<Fact>]       
        member __.``People search: single class`` () = 
            requestFor HttpMethod.Get "people?class=ItLeadership"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [knope; swanson]

        [<Fact>]       
        member __.``People search: class is case insensitive`` () = 
            requestFor HttpMethod.Get "people?class=itleadership"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [knope; swanson]

        [<Fact>]       
        member __.``People search: multiple classes are unioned`` () = 
            requestFor HttpMethod.Get "people?class=ItLeadership,ItProjectMgt"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [wyatt; knope; swanson;]

        [<Fact>]       
        member __.``People search: single interest`` () = 
            requestFor HttpMethod.Get "people?interest=waffles"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [knope]

        [<Fact>]       
        member __.``People search: multiple interests are unioned`` () = 
            requestFor HttpMethod.Get "people?interest=waffles,games"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [wyatt; knope]

        [<Fact>]       
        member __.``People search: multiple parameters are intersected`` () = 
            requestFor HttpMethod.Get "people?class=ItLeadership&interest=waffles"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [knope]

        [<Fact>]       
        member __.``People search: handles junk roles`` () = 
            requestFor HttpMethod.Get "people?class=FooBar,ItLeadership"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [knope; swanson]

        [<Fact>]       
        member __.``People search: handles junk interests`` () = 
            requestFor HttpMethod.Get "people?interest=waffles,foobar"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [knope]

        [<Fact>]       
        member __.``People search: single role`` () = 
            requestFor HttpMethod.Get "people?role=Leader"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [wyatt; swanson ]

        [<Fact>]       
        member __.``People search: multiple roles are unioned`` () = 
            requestFor HttpMethod.Get "people?role=Leader,Sublead"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [ wyatt; knope; swanson]

        [<Fact>]       
        member __.``People search: single permission`` () = 
            requestFor HttpMethod.Get "people?permission=Viewer"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [knope]

        [<Fact>]       
        member __.``People search: multiple permission are unioned`` () = 
            requestFor HttpMethod.Get "people?permission=Viewer,Owner"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [wyatt; knope; swanson]

        [<Fact>]       
        member __.``Donna is not in the directory`` () = 
            requestFor HttpMethod.Get "people?q=donna"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent []

        [<Fact>]       
        member __.``Lookup of Leslie yields directory record`` () = 
            requestFor HttpMethod.Get "people-lookup?q=leslie"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<seq<Person>> (fun people -> 
                people |> Seq.length |> should equal 1
                let a = people |> Seq.head
                a.NetId |> should equal knope.NetId)

        [<Fact>]       
        member __.``Lookup of Donna yields HR record`` () = 
            requestFor HttpMethod.Get "people-lookup?q=donna"
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<seq<Person>> (fun people -> 
                people |> Seq.length |> should equal 1
                let a = people |> Seq.head
                a.NetId |> should equal donnaHr.NetId)

        [<Fact>]       
        member __.``Add Donna to Parks unit`` () = 
            requestFor HttpMethod.Post "memberships"
            |> withAuthentication
            |> withBody 
                { UnitId=parksAndRec.Id
                  PersonId=None
                  NetId=Some("dmeagle")
                  Role=Role.Member
                  Permissions=UnitPermissions.Viewer
                  Title="Office Manager"
                  Percentage=100
                  Notes="" }
            |> shouldGetResponse HttpStatusCode.Created
            |> evaluateContent<UnitMember> (fun um -> 
                um.Person.Value.NetId  |> should equal donnaHr.NetId)

    type ApiErrorTests(output: ITestOutputHelper)=
        inherit HttpTestBase(output)

        [<Fact>]
        member __.``Unauthorized request yields 401 Unauthorized`` () = 
            requestFor HttpMethod.Get "units" 
            |> shouldGetResponse HttpStatusCode.Unauthorized

        [<Theory>]
        [<InlineDataAttribute("units")>]
        [<InlineDataAttribute("departments")>]
        [<InlineDataAttribute("memberships")>]
        [<InlineDataAttribute("membertools")>]
        [<InlineDataAttribute("supportRelationships")>]
        [<InlineDataAttribute("people")>]
        member __.``Get non-existent resource yields 404 Not Found`` (resource: string) = 
            sprintf "units/%s/1000" resource
            |> requestFor HttpMethod.Get
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.NotFound

        [<Theory>]
        [<InlineDataAttribute("units")>]
        [<InlineDataAttribute("memberships")>]
        [<InlineDataAttribute("membertools")>]
        [<InlineDataAttribute("supportRelationships")>]
        member __.``Delete non-existent resource yields 404 Not Found`` (resource: string) = 
            sprintf "units/%s/1000" resource
            |> requestFor HttpMethod.Delete
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.NotFound

        // *********************
        // Units
        // *********************

        [<Fact>]       
        member __.``Create a unit with missing required field in request body yields 400 Bad Request`` () = 
            requestFor HttpMethod.Post "units"
            |> withAuthentication
            |> withRawBody """{"description":"d", "url":"u", "parentId":undefined}"""
            |> shouldGetResponse HttpStatusCode.BadRequest

        [<Fact>]       
        member __.``Create a unit with non existent parent yields 400 Bad Request`` () = 
            requestFor HttpMethod.Post "units"
            |> withAuthentication
            |> withBody { parksAndRec with ParentId = Some(1000) }
            |> shouldGetResponse HttpStatusCode.BadRequest

        [<Fact>]       
        member __.``Create a unit with existing unit name yields 409 Conflict`` () = 
            requestFor HttpMethod.Post "units"
            |> withAuthentication
            |> withBody parksAndRec
            |> shouldGetResponse HttpStatusCode.Conflict

        [<Fact>]       
        member __.``Update a unit with circular relationship yields 409 Conflict`` () = 
            sprintf "units/%d" cityOfPawnee.Id
            |> requestFor HttpMethod.Put
            |> withAuthentication
            |> withBody { cityOfPawnee with ParentId=Some(parksAndRec.Id) }
            |> shouldGetResponse HttpStatusCode.Conflict

        [<Fact>]       
        member __.``Delete a unit with children yields 409 Conflict`` () = 
            sprintf "units/%d" cityOfPawnee.Id
            |> requestFor HttpMethod.Delete
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.Conflict

        // *********************
        // Unit Memberships
        // *********************

        [<Fact>]       
        member __.``Create a membership with non existent unit yields 400 Bad Request`` () = 
            requestFor HttpMethod.Post "memberships"
            |> withAuthentication
            |> withBody { knopeMembership with UnitId=1000 }
            |> shouldGetResponse HttpStatusCode.BadRequest

        [<Fact>]       
        member __.``Create a membership with non existent person yields 400 Bad Request`` () = 
            requestFor HttpMethod.Post "memberships"
            |> withAuthentication
            |> withBody { knopeMembership with PersonId=Some(1000) }
            |> shouldGetResponse HttpStatusCode.BadRequest

        [<Fact>]       
        member __.``Create a membership that duplicates existing memberships yields 409 Conflict`` () = 
            requestFor HttpMethod.Post "memberships"
            |> withAuthentication
            |> withBody knopeMembership
            |> shouldGetResponse HttpStatusCode.Conflict

        [<Fact>]       
        member __.``Update a membership that duplicates existing memberships yields 409 Conflict`` () = 
            sprintf "memberships/%d" swansonMembership.Id
            |> requestFor HttpMethod.Put
            |> withAuthentication
            |> withBody knopeMembership
            |> shouldGetResponse HttpStatusCode.Conflict

        // *********************
        // Support Relationships
        // *********************

        [<Fact>]       
        member __.``Create a support relationship with non existent unit yields 400 Bad Request`` () = 
            requestFor HttpMethod.Post "supportRelationships"
            |> withAuthentication
            |> withBody { supportRelationship with UnitId=1000 }
            |> shouldGetResponse HttpStatusCode.BadRequest

        [<Fact>]       
        member __.``Create a support relationship with non existent department yields 400 Bad Request`` () = 
            requestFor HttpMethod.Post "supportRelationships"
            |> withAuthentication
            |> withBody { supportRelationship with DepartmentId=1000 }
            |> shouldGetResponse HttpStatusCode.BadRequest

        [<Fact>]       
        member __.``Create a supportRelationship that duplicates existing relationship yields 409 Conflict`` () = 
            requestFor HttpMethod.Post "supportRelationships"
            |> withAuthentication
            |> withBody supportRelationship
            |> shouldGetResponse HttpStatusCode.Conflict

        // *****************
        // Member Tools
        // *****************

        [<Fact>]       
        member __.``Create a member tool with non existent membership yields 400 Bad Request`` () = 
            requestFor HttpMethod.Post "membertools"
            |> withAuthentication
            |> withBody { memberTool with MembershipId=1000 }
            |> shouldGetResponse HttpStatusCode.BadRequest

        [<Fact>]       
        member __.``Create a member tool with non existent tool yields 400 Bad Request`` () = 
            requestFor HttpMethod.Post "membertools"
            |> withAuthentication
            |> withBody { memberTool with ToolId=1000 }
            |> shouldGetResponse HttpStatusCode.BadRequest

        [<Fact>]       
        member __.``Update a nonexistent member tool yields 404 Not Found`` () = 
            requestFor HttpMethod.Put "membertools/1000"
            |> withAuthentication
            |> withBody memberTool
            |> shouldGetResponse HttpStatusCode.NotFound

        [<Fact>]       
        member __.``Update a member tool with non existent membership yields 400 Bad Request`` () = 
            requestFor HttpMethod.Put "membertools/1"
            |> withAuthentication
            |> withBody { memberTool with MembershipId=1000 }
            |> shouldGetResponse HttpStatusCode.BadRequest

        [<Fact>]       
        member __.``Update a member tool with non existent tool yields 400 Bad Request`` () = 
            requestFor HttpMethod.Put "membertools/1"
            |> withAuthentication
            |> withBody { memberTool with ToolId=1000 }
            |> shouldGetResponse HttpStatusCode.BadRequest
