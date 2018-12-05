namespace Integration 

module DatabaseTests=

    open Xunit
    open Xunit.Abstractions
    open Dapper
    open Chessie.ErrorHandling
    open Functions.Common.Types
    open Functions.Common.Fakes
    open Functions.Common.Database
    open TestFixture
    open PostgresContainer
    open FsUnit.Xunit
    
    let awaitAndUnpack<'T> (asyncResult:AsyncResult<'T,_>) = 
        let result = asyncResult |> Async.ofAsyncResult |> Async.RunSynchronously
        match result with 
        | Ok(value, _) -> value
        | Bad(msgs) -> 
            msgs
            |> sprintf "Failed to unpack result of type %s: %A" typedefof<'T>.Name 
            |> System.Exception 
            |> raise

    let matchUnit expectedId (expected:Unit) (actual:Unit) =
        actual.Id |> should equal expectedId
        actual.Name |> should equal expected.Name
        actual.Url |> should equal expected.Url
        actual.Description |> should equal expected.Description

    type InsertionTests(output: ITestOutputHelper)=
        inherit DatabaseIntegrationTestBase()

        [<Fact>]
        member __.``Unit ids are non-zero`` () = 
            cityId |> should be (greaterThan 0)
            parksAndRecId |> should be (greaterThan 0)
            fourthFloorId |> should be (greaterThan 0)

        [<Fact>]
        member __.``People ids are non-zero`` () = 
            knopeId |> should be (greaterThan 0)
            swansonId |> should be (greaterThan 0)
            sebastianId |> should be (greaterThan 0)

        [<Fact>]
        member __.``Department ids are non-zero`` () = 
            parksDeptId |> should be (greaterThan 0)

    type UnitsDto(output: ITestOutputHelper)=
        inherit DatabaseIntegrationTestBase()
        let repo = DatabaseRepository(connectionString) :> IDataRepository
        let actual = repo.GetUnits() |> awaitAndUnpack

        [<Fact>]
        member __.``Units have non-zero IDs`` () =
            Assert.True(actual |> Seq.forall (fun a -> a.Id <> 0))

        [<Fact>]
        member __.``Units should only return top-level units`` () = 
            let actual = actual |> Seq.map (fun f -> f.Name) |> Seq.sort
            let expected = [city.Name]
            Assert.Equal(expected, actual)

    type UnitDto(output: ITestOutputHelper)=
        inherit DatabaseIntegrationTestBase()
        let repo = DatabaseRepository(connectionString) :> IDataRepository
        let actual = repo.GetUnit(parksAndRecId) |> awaitAndUnpack

        [<Fact>]
        member __.``Properties`` () = 
            actual.Id |> should equal parksAndRecId
            actual.Name |> should equal parksAndRec.Name
            actual.Url |> should equal parksAndRec.Url
            actual.Description |> should equal parksAndRec.Description
        
        [<Fact>]
        member __.``Parent`` () = 
            actual.Parent |> should equal (Some({city with Id=cityId}))
        
        [<Fact>]
        member __.``Children`` () = 
            let children = actual.Children |> Seq.toList
            children |> should haveLength 1
            matchUnit fourthFloorId fourthFloor (children |> Seq.head)

        [<Fact>]
        member __.``Members count`` () =
            actual.Members |> Seq.length |> should equal 3

        [<Fact>]
        member __.``Member - Swanson`` () =
            let actual = actual.Members |> Seq.find (fun m -> m.Id = swansonId)
            actual.Name |> should equal swanson.Name
            actual.Description |> should equal swanson.NetId
            actual.Title |> should equal "Director"
            actual.Role |> should equal Role.Leader
            actual.Percentage |> should equal 100
            actual.Tools |> Seq.toList |> should equal [Tools.AccountMgt]

        [<Fact>]
        member __.``Member - Knope`` () =
            let actual = actual.Members |> Seq.find (fun m -> m.Id = knopeId)
            actual.Name |> should equal knope.Name
            actual.Description |> should equal knope.NetId
            actual.Title |> should equal "Deputy Director"
            actual.Role |> should equal Role.Sublead
            actual.Percentage |> should equal 100
            actual.Tools |> Seq.toList |> should be Empty

        [<Fact>]
        member __.``Member - Li'l Sebastian`` () = 
            let actual = actual.Members |> Seq.find (fun m -> m.Id = sebastianId)
            actual.Id |> should equal sebastianId
            actual.Name |> should equal sebastian.Name
            actual.Description |> should equal sebastian.NetId
            actual.Title |> should equal "Mascot"
            actual.Role |> should equal Role.Member
            actual.Percentage |> should equal 100
            actual.Tools |> Seq.toList |> should be Empty
