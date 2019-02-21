namespace Integration

module ApiErrorTests = 

    open Xunit.Abstractions
    open Xunit
    open TestFixture
    open Functions.Fakes
    open Database.Fakes
    open FSharp.Data
    open FSharp.Data.HttpRequestHeaders
    open FsUnit.Xunit
    open Newtonsoft.Json

    type Body = 
        | NoBody
        | Json of string
        | Record of obj

    type ApiErrorTests(output: ITestOutputHelper)=
        inherit HttpTestBase(output)

        let authdContent = 
          [ (Authorization "Bearer eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJleHAiOiIxOTE1NTQ0NjQzIiwidXNlcl9uYW1lIjoiam9obmRvZSIsInVzZXJfaWQiOjF9.bCMuAfRby19tJHCOggz7KESMRxtPl_h7pLTQTx3ui4E")
            (ContentType HttpContentTypes.Json) ]

        let authdRequest method route body = 
            let body = 
                match body with
                | NoBody -> None
                | Json str -> str |> TextRequest |> Some
                | Record obj -> JsonConvert.SerializeObject(obj, Functions.Json.JsonSettings) |> TextRequest |> Some
            match body with
            | Some (b) -> Http.Request((sprintf "%s/%s" functionServerUrl route), httpMethod=method, body=b, headers=authdContent, silentHttpErrors=true)
            | None -> Http.Request((sprintf "%s/%s" functionServerUrl route), httpMethod=method, headers=authdContent, silentHttpErrors=true)

        [<Fact>]
        member __.``Unauthorized request yields 401 Unauthorized`` () = 
            let response = Http.Request(functionServerUrl+"/units", silentHttpErrors=true)
            response.StatusCode |> should equal 401

        [<Theory>]
        [<InlineDataAttribute("units")>]
        [<InlineDataAttribute("departments")>]
        [<InlineDataAttribute("memberships")>]
        [<InlineDataAttribute("supportRelationships")>]
        [<InlineDataAttribute("people")>]
        member __.``Get non-existent resource yields 404 Not Found`` (resource: string) = 
            let response = authdRequest "GET" "units/1000" NoBody
            response.StatusCode |> should equal 404

        [<Theory>]
        [<InlineDataAttribute("units")>]
        [<InlineDataAttribute("memberships")>]
        [<InlineDataAttribute("supportRelationships")>]
        member __.``Delete non-existent resource yields 404 Not Found`` (resource: string) = 
            let response = authdRequest "DELETE" "units/1000" NoBody
            response.StatusCode |> should equal 404

        [<Fact>]       
        member __.``Create resource with missing required field in request body yields 400 Bad Request`` () = 
            let response = Json("""{"description":"d", "url":"u", "parentId":undefined}""") |> authdRequest "POST" "units"
            response.StatusCode |> should equal 400

        [<Fact>]       
        member __.``Create a unit with non existent parent yields 400 Bad Request`` () = 
            let response = Record({ parksAndRec with ParentId = Some(1000) }) |> authdRequest "POST" "units" 
            response.StatusCode |> should equal 400

        [<Fact>]       
        member __.``Create a unit with existing unit name yields 409 Conflict`` () = 
            let response = Record(parksAndRec) |> authdRequest "POST" "units" 
            response.StatusCode |> should equal 409

        [<Fact>]       
        member __.``Update a unit with circular relationship yields 409 Conflict`` () = 
            let response = Record({ cityOfPawnee with ParentId=Some(parksAndRec.Id) }) |> authdRequest "PUT" "units/1" 
            response.StatusCode |> should equal 409

        [<Fact>]       
        member __.``Create a membership with non existent unit yields 400 Bad Request`` () = 
            let response = Record({ knopeMembership with UnitId=1000 }) |> authdRequest "POST" "memberships" 
            response.StatusCode |> should equal 400

        [<Fact>]       
        member __.``Create a membership with non existent person yields 400 Bad Request`` () = 
            let response = Record({ knopeMembership with PersonId=Some(1000) }) |> authdRequest "POST" "memberships" 
            response.StatusCode |> should equal 400

        [<Fact>]       
        member __.``Create a membership that duplicates existing memberships yields 409 Conflict`` () = 
            let response = Record(knopeMembership) |> authdRequest "POST" "memberships" 
            response.StatusCode |> should equal 409

        [<Fact>]       
        member __.``Update a membership that duplicates existing memberships yields 409 Conflict`` () = 
            let response = Record(knopeMembership) |> authdRequest "PUT" (sprintf "memberships/%d" swansonMembership.Id) 
            response.StatusCode |> should equal 409

        [<Fact>]       
        member __.``Create a support relationship with non existent unit yields 400 Bad Request`` () = 
            let response = Record({ supportRelationship with UnitId=1000 }) |> authdRequest "POST" "supportRelationships" 
            response.StatusCode |> should equal 400

        [<Fact>]       
        member __.``Create a support relationship with non existent department yields 400 Bad Request`` () = 
            let response = Record({ supportRelationship with DepartmentId=1000 }) |> authdRequest "POST" "supportRelationships" 
            response.StatusCode |> should equal 400

        [<Fact>]       
        member __.``Create a supportRelationship that duplicates existing relationship yields 409 Conflict`` () = 
            let response = Record(supportRelationship) |> authdRequest "POST" "supportRelationships" 
            response.StatusCode |> should equal 409
