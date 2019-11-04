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

    let withAuthentication (jwt:JwtResponse) (request:HttpRequestMessage) = 
        request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", jwt.access_token)
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

    let readContent (response:HttpResponseMessage) =
        response.Content.ReadAsStringAsync() 
        |> Async.AwaitTask 
        |> Async.RunSynchronously

    let parseContent<'T> (response:HttpResponseMessage) = 
        response
        |> readContent
        |> fun str -> JsonConvert.DeserializeObject<'T>(value=str, settings=JsonSettings)

    let toBytes (x : string) = System.Text.Encoding.ASCII.GetBytes x
    let parseXmlContent<'T> (response:HttpResponseMessage) = 
        let xml = response |> readContent
        let serializer = System.Xml.Serialization.XmlSerializer(typeof<'T>)
        use stream = new System.IO.MemoryStream(toBytes xml)
        serializer.Deserialize stream :?> 'T

    let shouldGetContent<'T> (expectedContent:'T) (response:HttpResponseMessage) =
        response |> parseContent<'T> |> should equal expectedContent
        response

    let shouldGetXmlContent<'T> (expectedContent:'T) (response:HttpResponseMessage) =
        response |> parseXmlContent<'T> |> should equal expectedContent
        response

    let evaluateContent<'T> (evalFn:'T -> unit) (response:HttpResponseMessage) =
        response |> parseContent<'T> |> evalFn
        response    

    let evaluateRawContent(evalFn:string -> unit) (response:HttpResponseMessage) =
        response |> readContent |> evalFn
        response    

    let evaluateXmlContent<'T> (evalFn:'T -> unit) (response:HttpResponseMessage) =
        response |> parseXmlContent<'T> |> evalFn
        response    

    let personUpdate = 
      { PersonRequest.Id=knope.Id
        Expertise="Pawnee History" 
        Responsibilities=Responsibilities.UserExperience|||Responsibilities.BizSysAnalysis
        Location="JJ's Diner" }

    let rawPersonUpdate = """{"id":0, "expertise":"Pawnee History", "responsibilities":"UserExperience,BizSysAnalysis", "location":"JJ's Diner"}"""

    let evaluatePersonUpdate (p:Person) = 
        p.Id |> should equal personUpdate.Id
        p.Expertise |> should equal personUpdate.Expertise
        p.Responsibilities |> should equal personUpdate.Responsibilities
        p.Location |> should equal personUpdate.Location


    type ApiTests(output: ITestOutputHelper)=
        inherit HttpTestBase(output)

        [<Fact>]       
        member __.``People search: netid`` () = 
            requestFor HttpMethod.Get "people?q=rswa"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [swanson]

        [<Fact>]       
        member __.``People search: netid is case insensitive`` () = 
            requestFor HttpMethod.Get "people?q=RSWA"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [swanson]

        [<Fact>]       
        member __.``People search: name`` () = 
            requestFor HttpMethod.Get "people?q=Ron"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [swanson]

        [<Fact>]       
        member __.``People search: name is case insensitive`` () = 
            requestFor HttpMethod.Get "people?q=RON"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [swanson]

        [<Fact>]       
        member __.``People search: single class`` () = 
            requestFor HttpMethod.Get "people?class=ItLeadership"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [knope; swanson]

        [<Fact>]       
        member __.``People search: class is case insensitive`` () = 
            requestFor HttpMethod.Get "people?class=itleadership"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [knope; swanson]

        [<Fact>]       
        member __.``People search: multiple classes are unioned`` () = 
            requestFor HttpMethod.Get "people?class=ItLeadership,ItProjectMgt"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [wyatt; knope; swanson;]

        [<Fact>]       
        member __.``People search: single interest`` () = 
            requestFor HttpMethod.Get "people?interest=waffles"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [knope]

        [<Fact>]       
        member __.``People search: multiple interests are unioned`` () = 
            requestFor HttpMethod.Get "people?interest=waffles,games"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [wyatt; knope]

        [<Fact>]       
        member __.``People search: multiple parameters are intersected`` () = 
            requestFor HttpMethod.Get "people?class=ItLeadership&interest=waffles"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [knope]

        [<Fact>]       
        member __.``People search: handles junk roles`` () = 
            requestFor HttpMethod.Get "people?class=FooBar,ItLeadership"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [knope; swanson]

        [<Fact>]       
        member __.``People search: handles junk interests`` () = 
            requestFor HttpMethod.Get "people?interest=waffles,foobar"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [knope]

        [<Fact>]       
        member __.``People search: single role`` () = 
            requestFor HttpMethod.Get "people?role=Leader"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [wyatt; swanson ]

        [<Fact>]       
        member __.``People search: multiple roles are unioned`` () = 
            requestFor HttpMethod.Get "people?role=Leader,Sublead"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [ wyatt; knope; swanson]

        [<Fact>]       
        member __.``People search: single campus: Pawnee`` () = 
            requestFor HttpMethod.Get "people?campus=Pawnee"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [ knope; swanson]

        [<Fact>]       
        member __.``People search: single campus: Indianapolis`` () = 
            requestFor HttpMethod.Get "people?campus=Indianapolis"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [ wyatt; admin ]

        [<Fact>]       
        member __.``People search: multiple campus: Indianapolis and Pawnee`` () = 
            requestFor HttpMethod.Get "people?campus=Pawnee,Indianapolis"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [ wyatt; admin; knope; swanson;  ]
            
        [<Fact>]       
        member __.``People search: single permission`` () = 
            requestFor HttpMethod.Get "people?permission=Viewer"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [knope]

        [<Fact>]       
        member __.``People search: multiple permission are unioned`` () = 
            requestFor HttpMethod.Get "people?permission=Viewer,Owner"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [wyatt; knope; swanson]

        [<Fact>]       
        member __.``People search: UITS`` () = 
            requestFor HttpMethod.Get "people?area=uits"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [wyatt; knope; swanson]

        [<Fact>]       
        member __.``People search: Edge`` () = 
            requestFor HttpMethod.Get "people?area=edge"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent []

        [<Fact>]       
        member __.``People search: UITS and Edge`` () = 
            requestFor HttpMethod.Get "people?area=uits,edge"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent [wyatt; knope; swanson]

        [<Fact>]       
        member __.``Donna is not in the directory`` () = 
            requestFor HttpMethod.Get "people?q=donna"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent []

        [<Fact>]       
        member __.``Lookup of Leslie yields directory record`` () = 
            requestFor HttpMethod.Get "people-lookup?q=leslie"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<seq<Person>> (fun people -> 
                people |> Seq.length |> should equal 1
                let a = people |> Seq.head
                a.NetId |> should equal knope.NetId)

        [<Fact>]       
        member __.``Lookup of Donna yields HR record`` () = 
            requestFor HttpMethod.Get "people-lookup?q=donna"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<seq<Person>> (fun people -> 
                people |> Seq.length |> should equal 1
                let a = people |> Seq.head
                a.NetId |> should equal donnaHr.NetId)

        [<Fact>]       
        member __.``Add Donna to Parks unit`` () = 
            requestFor HttpMethod.Post "memberships"
            |> withAuthentication adminJwt
            |> withBody 
                { UnitMemberRequest.UnitId=parksAndRec.Id
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

        [<Fact>]       
        member __.``People: get by id`` () = 
            requestFor HttpMethod.Get (sprintf "people/%d" knope.Id)
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent knope

        [<Fact>]       
        member __.``People: get by netid`` () = 
            requestFor HttpMethod.Get (sprintf "people/%s" knope.NetId)
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> shouldGetContent knope

        [<Fact>]       
        member __.``People: get memberships by id`` () = 
            requestFor HttpMethod.Get (sprintf "people/%d/memberships" knope.Id)
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<seq<UnitMember>> (fun memberships ->
                 memberships |> Seq.length |> should equal 1
                 let head = memberships |> Seq.head
                 head.Id |> should equal knopeMembership.Id)

        [<Fact>]       
        member __.``People: get memberships by netid`` () = 
            requestFor HttpMethod.Get (sprintf "people/%s/memberships" knope.NetId)
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<seq<UnitMember>> (fun memberships ->
                 memberships |> Seq.length |> should equal 1
                 let head = memberships |> Seq.head
                 head.Id |> should equal knopeMembership.Id)

        [<Fact>]       
        member __.``People: admin can update any record`` () = 
            requestFor HttpMethod.Put (sprintf "people/%d" knope.Id)
            |> withAuthentication adminJwt
            |> withBody personUpdate
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<Person> evaluatePersonUpdate

        [<Fact>]       
        member __.``People: knope can update own record`` () = 
            requestFor HttpMethod.Put (sprintf "people/%d" knope.Id)
            |> withAuthentication knopeJwt
            |> withBody personUpdate
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<Person> evaluatePersonUpdate

        [<Fact>]       
        member __.``People: unit owner swanson can update member knope`` () = 
            requestFor HttpMethod.Put (sprintf "people/%d" knope.Id)
            |> withAuthentication swansonJwt
            |> withBody personUpdate
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<Person> evaluatePersonUpdate

        [<Fact>]       
        member __.``People: unit member knope can't update owner swanson`` () = 
            requestFor HttpMethod.Put (sprintf "people/%d" swanson.Id)
            |> withAuthentication knopeJwt
            |> withBody personUpdate
            |> shouldGetResponse HttpStatusCode.Forbidden

        [<Fact>]       
        member __.``People: unit owner swanson can't someone in another unit`` () = 
            requestFor HttpMethod.Put (sprintf "people/%d" wyatt.Id)
            |> withAuthentication swansonJwt
            |> withBody personUpdate
            |> shouldGetResponse HttpStatusCode.Forbidden

        [<Fact>]       
        member __.``People: unit owner swanson can update member knope: raw`` () = 
            requestFor HttpMethod.Put (sprintf "people/%d" knope.Id)
            |> withAuthentication swansonJwt
            |> withRawBody rawPersonUpdate
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<Person> evaluatePersonUpdate

        [<Fact>]       
        member __.``Buildings: get all`` () = 
            requestFor HttpMethod.Get "buildings"
            |> withAuthentication swansonJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<seq<Building>> (fun buildings ->
                 buildings |> Seq.length |> should equal 1
                 let head = buildings |> Seq.head
                 head.Id |> should equal cityHall.Id)

        [<Fact>]       
        member __.``Buildings: get one`` () = 
            requestFor HttpMethod.Get (sprintf "buildings/%d" cityHall.Id)
            |> withAuthentication swansonJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<Building> (fun building ->
                 building.Id |> should equal cityHall.Id)
        
        [<Fact>]       
        member __.``Buildings: search name`` () = 
            requestFor HttpMethod.Get "buildings?q=pawn"
            |> withAuthentication swansonJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<seq<Building>> (fun buildings ->
                 buildings |> Seq.length |> should equal 1
                 let head = buildings |> Seq.head
                 head.Id |> should equal cityHall.Id)

        [<Fact>]       
        member __.``Buildings: search address`` () = 
            requestFor HttpMethod.Get "buildings?q=main"
            |> withAuthentication swansonJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<seq<Building>> (fun buildings ->
                 buildings |> Seq.length |> should equal 1
                 let head = buildings |> Seq.head
                 head.Id |> should equal cityHall.Id)

        [<Fact>]       
        member __.``Buildings: search code`` () = 
            requestFor HttpMethod.Get "buildings?q=pa123"
            |> withAuthentication swansonJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<seq<Building>> (fun buildings ->
                 buildings |> Seq.length |> should equal 1
                 let head = buildings |> Seq.head
                 head.Id |> should equal cityHall.Id)

        [<Fact>]       
        member __.``Buildings: search code with dash`` () = 
            requestFor HttpMethod.Get "buildings?q=pa-123"
            |> withAuthentication swansonJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<seq<Building>> (fun buildings ->
                 buildings |> Seq.length |> should equal 1
                 let head = buildings |> Seq.head
                 head.Id |> should equal cityHall.Id)

        [<Fact>]       
        member __.``Building relationships: get all`` () = 
            requestFor HttpMethod.Get "buildingRelationships"
            |> withAuthentication swansonJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<seq<BuildingRelationship>> (fun relationships ->
                 relationships |> Seq.length |> should equal 1
                 let head = relationships |> Seq.head
                 head.Id |> should equal buildingRelationship.Id)

        [<Fact>]       
        member __.``Building relationships: get one`` () = 
            requestFor HttpMethod.Get (sprintf "buildingRelationships/%d" buildingRelationship.Id)
            |> withAuthentication swansonJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<BuildingRelationship> (fun building ->
                 building.Id |> should equal buildingRelationship.Id)

        [<Fact>]       
        member __.``Building: get supporting units`` () = 
            requestFor HttpMethod.Get (sprintf "buildings/%d/supportingUnits" cityHall.Id)
            |> withAuthentication swansonJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateContent<seq<BuildingRelationship>> (fun relationships ->
                 relationships |> Seq.length |> should equal 1
                 let head = relationships |> Seq.head
                 head.Id |> should equal buildingRelationship.Id)

        [<Fact>]       
        member __.``Building relationships: delete`` () = 
            requestFor HttpMethod.Delete (sprintf "buildingRelationships/%d" buildingRelationship.Id)
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.NoContent
            |> evaluateRawContent (fun content -> content |> should equal "")

        [<Fact>]       
        member __.``Support relationships: delete`` () = 
            requestFor HttpMethod.Delete (sprintf "supportRelationships/%d" supportRelationship.Id)
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.NoContent
            |> evaluateRawContent (fun content -> content |> should equal "")
        
        [<Fact>]       
        member __.``Legacy: LspList`` () = 
            requestFor HttpMethod.Get "LspdbWebService.svc/LspList"
            |> withAuthentication swansonJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateXmlContent<LspInfoArray> (fun arr ->
                 arr.LspInfos |> Seq.length |> should equal 1
                 let head = arr.LspInfos |> Seq.head
                 head |> should equal lspInfo)
        
        [<Fact>]       
        member __.``Legacy: LspDepartments`` () = 
            requestFor HttpMethod.Get (sprintf "LspdbWebService.svc/LspDepartments/%s" wyatt.NetId)
            |> withAuthentication swansonJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateXmlContent<LspDepartmentArray> (fun arr ->
                 arr.NetworkID |> should equal wyatt.NetId 
                 arr.DeptCodeList.Values |> should equal [| parksDept.Name |] )

        [<Fact>]       
        member __.``Legacy: DepartmentLsps`` () = 
            requestFor HttpMethod.Get (sprintf "LspdbWebService.svc/LspsInDept/%s" parksDept.Name)
            |> withAuthentication swansonJwt
            |> shouldGetResponse HttpStatusCode.OK
            |> evaluateXmlContent<LspContactArray> (fun arr ->
                 arr.LspContacts |> Seq.length |> should equal 1
                 let head = arr.LspContacts |> Seq.head
                 head |> should equal lspContact )

    type ApiErrorTests(output: ITestOutputHelper)=
        inherit HttpTestBase(output)

        [<Fact>]
        member __.``Unauthorized request yields 401 Unauthorized`` () = 
            requestFor HttpMethod.Get "units" 
            |> shouldGetResponse HttpStatusCode.Unauthorized

        [<Theory>]
        [<InlineDataAttribute("units")>]
        [<InlineDataAttribute("departments")>]
        [<InlineDataAttribute("buildings")>]
        [<InlineDataAttribute("memberships")>]
        [<InlineDataAttribute("membertools")>]
        [<InlineDataAttribute("supportRelationships")>]
        [<InlineDataAttribute("buildingRelationships")>]
        [<InlineDataAttribute("people")>]
        member __.``Get non-existent resource yields 404 Not Found`` (resource: string) = 
            sprintf "units/%s/1000" resource
            |> requestFor HttpMethod.Get
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.NotFound

        [<Theory>]
        [<InlineDataAttribute("units")>]
        [<InlineDataAttribute("memberships")>]
        [<InlineDataAttribute("membertools")>]
        [<InlineDataAttribute("supportRelationships")>]
        [<InlineDataAttribute("buildingRelationships")>]
        member __.``Delete non-existent resource yields 404 Not Found`` (resource: string) = 
            sprintf "units/%s/1000" resource
            |> requestFor HttpMethod.Delete
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.NotFound


        [<Fact>]       
        member __.``Get non-existent person`` () = 
            requestFor HttpMethod.Get "people/foo"
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.NotFound

        // *********************
        // Units
        // *********************

        [<Fact>]       
        member __.``Create a unit with missing required field in request body yields 400 Bad Request`` () = 
            requestFor HttpMethod.Post "units"
            |> withAuthentication adminJwt
            |> withRawBody """{"description":"d", "url":"u", "parentId":undefined}"""
            |> shouldGetResponse HttpStatusCode.BadRequest

        [<Fact>]       
        member __.``Create a unit with non existent parent yields 404 Not Found`` () = 
            requestFor HttpMethod.Post "units"
            |> withAuthentication adminJwt
            |> withBody { parksAndRec with Name="test"; ParentId = Some(1000) }
            |> shouldGetResponse HttpStatusCode.NotFound

        [<Fact>]       
        member __.``Update a unit with circular relationship yields 409 Conflict`` () = 
            sprintf "units/%d" cityOfPawnee.Id
            |> requestFor HttpMethod.Put
            |> withAuthentication adminJwt
            |> withBody { cityOfPawnee with ParentId=Some(parksAndRec.Id) }
            |> shouldGetResponse HttpStatusCode.Conflict

        [<Fact>]       
        member __.``Delete a unit with children yields 409 Conflict`` () = 
            sprintf "units/%d" cityOfPawnee.Id
            |> requestFor HttpMethod.Delete
            |> withAuthentication adminJwt
            |> shouldGetResponse HttpStatusCode.Conflict

        // *********************
        // Unit Memberships
        // *********************

        [<Fact>]       
        member __.``Create a membership with non existent unit yields 404 Not Found`` () = 
            requestFor HttpMethod.Post "memberships"
            |> withAuthentication adminJwt
            |> withBody { knopeMembership with UnitId=1000 }
            |> shouldGetResponse HttpStatusCode.NotFound

        [<Fact>]       
        member __.``Create a membership with non existent person yields 404 Not Found`` () = 
            requestFor HttpMethod.Post "memberships"
            |> withAuthentication adminJwt
            |> withBody { knopeMembership with PersonId=Some(1000) }
            |> shouldGetResponse HttpStatusCode.NotFound

        [<Fact>]       
        member __.``Create a membership that duplicates existing memberships yields 409 Conflict`` () = 
            requestFor HttpMethod.Post "memberships"
            |> withAuthentication adminJwt
            |> withBody knopeMembership
            |> shouldGetResponse HttpStatusCode.Conflict

        [<Fact>]       
        member __.``Update a membership that duplicates existing memberships yields 409 Conflict`` () = 
            sprintf "memberships/%d" swansonMembership.Id
            |> requestFor HttpMethod.Put
            |> withAuthentication adminJwt
            |> withBody knopeMembership
            |> shouldGetResponse HttpStatusCode.Conflict

        // *********************
        // Support Relationships
        // *********************

        [<Fact>]       
        member __.``Create a support relationship with non existent unit yields 404 Not Found`` () = 
            requestFor HttpMethod.Post "supportRelationships"
            |> withAuthentication adminJwt
            |> withBody { supportRelationship with UnitId=1000 }
            |> shouldGetResponse HttpStatusCode.NotFound

        [<Fact>]       
        member __.``Create a support relationship with non existent department yields 404 Not Found`` () = 
            requestFor HttpMethod.Post "supportRelationships"
            |> withAuthentication adminJwt
            |> withBody { supportRelationship with DepartmentId=1000 }
            |> shouldGetResponse HttpStatusCode.NotFound

        [<Fact>]       
        member __.``Create a supportRelationship that duplicates existing relationship yields 409 Conflict`` () = 
            requestFor HttpMethod.Post "supportRelationships"
            |> withAuthentication adminJwt
            |> withBody supportRelationship
            |> shouldGetResponse HttpStatusCode.Conflict

        // *********************
        // Building Relationships
        // *********************

        [<Fact>]       
        member __.``Create a building relationship with non existent unit yields 404 Not Found`` () = 
            requestFor HttpMethod.Post "buildingRelationships"
            |> withAuthentication adminJwt
            |> withBody { buildingRelationship with UnitId=1000 }
            |> shouldGetResponse HttpStatusCode.NotFound

        [<Fact>]       
        member __.``Create a building relationship with non existent department yields 404 Not Found`` () = 
            requestFor HttpMethod.Post "buildingRelationships"
            |> withAuthentication adminJwt
            |> withBody { buildingRelationship with BuildingId=1000 }
            |> shouldGetResponse HttpStatusCode.NotFound

        [<Fact>]       
        member __.``Create a building relationship that duplicates existing relationship yields 409 Conflict`` () = 
            requestFor HttpMethod.Post "buildingRelationships"
            |> withAuthentication adminJwt
            |> withBody buildingRelationship
            |> shouldGetResponse HttpStatusCode.Conflict

        // *****************
        // Member Tools
        // *****************

        [<Fact>]       
        member __.``Create a member tool with non existent membership yields 404 Not Found`` () = 
            requestFor HttpMethod.Post "membertools"
            |> withAuthentication adminJwt
            |> withBody { memberTool with MembershipId=1000 }
            |> shouldGetResponse HttpStatusCode.NotFound

        [<Fact>]       
        member __.``Create a member tool with non existent tool yields 404 Not Found`` () = 
            requestFor HttpMethod.Post "membertools"
            |> withAuthentication adminJwt
            |> withBody { memberTool with ToolId=1000 }
            |> shouldGetResponse HttpStatusCode.NotFound

        [<Fact>]       
        member __.``Update a nonexistent member tool yields 404 Not Found`` () = 
            requestFor HttpMethod.Put "membertools/1000"
            |> withAuthentication adminJwt
            |> withBody memberTool
            |> shouldGetResponse HttpStatusCode.NotFound

        [<Fact>]       
        member __.``Update a member tool with non existent membership yields 404 Not Found`` () = 
            requestFor HttpMethod.Put "membertools/1"
            |> withAuthentication adminJwt
            |> withBody { memberTool with MembershipId=1000 }
            |> shouldGetResponse HttpStatusCode.NotFound

        [<Fact>]       
        member __.``Update a member tool with non existent tool yields 404 Not Found`` () = 
            requestFor HttpMethod.Put "membertools/1"
            |> withAuthentication adminJwt
            |> withBody { memberTool with ToolId=1000 }
            |> shouldGetResponse HttpStatusCode.NotFound
