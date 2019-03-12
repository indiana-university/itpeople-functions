namespace Integration

module ApiErrorTests = 

    open Xunit.Abstractions
    open Xunit
    open TestFixture
    open Functions.Fakes
    open Functions.Json
    open FsUnit.Xunit
    open Newtonsoft.Json
    open System.Net
    open System.Net.Http
    open System.Net.Http.Headers

    let httpClient = new HttpClient()

    type ApiErrorTests(output: ITestOutputHelper)=
        inherit HttpTestBase(output)

        let requestFor method route = 
            let uri = sprintf "%s/%s" functionServerUrl route |> System.Uri
            new HttpRequestMessage(method, uri)

        let withAuthentication (request:HttpRequestMessage) = 
            request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJleHAiOiIxOTE1NTQ0NjQzIiwidXNlcl9uYW1lIjoiam9obmRvZSIsInVzZXJfaWQiOjF9.bCMuAfRby19tJHCOggz7KESMRxtPl_h7pLTQTx3ui4E")
            request
        
        let withRawBody str (request:HttpRequestMessage) =
            request.Content <- new StringContent(str, System.Text.Encoding.UTF8, "application/json")
            request

        let withBody obj (request:HttpRequestMessage) =
            withRawBody (JsonConvert.SerializeObject(value=obj, settings=JsonSettings)) request

        let shouldGetResponse expectedStatus (request:HttpRequestMessage) =
            let response = httpClient.SendAsync(request) |> Async.AwaitTask |> Async.RunSynchronously
            response.StatusCode |> should equal expectedStatus

        [<Fact>]
        member __.``Unauthorized request yields 401 Unauthorized`` () = 
            requestFor HttpMethod.Get "units" 
            |> shouldGetResponse HttpStatusCode.Unauthorized

        [<Theory>]
        [<InlineDataAttribute("units")>]
        [<InlineDataAttribute("departments")>]
        [<InlineDataAttribute("memberships")>]
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
        [<InlineDataAttribute("supportRelationships")>]
        member __.``Delete non-existent resource yields 404 Not Found`` (resource: string) = 
            sprintf "units/%s/1000" resource
            |> requestFor HttpMethod.Delete
            |> withAuthentication
            |> shouldGetResponse HttpStatusCode.NotFound

        [<Fact>]       
        member __.``Create resource with missing required field in request body yields 400 Bad Request`` () = 
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
