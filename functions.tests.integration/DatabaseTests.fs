// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Integration 

module DatabaseTests=

    open Xunit
    open Xunit.Abstractions
    open Dapper
    open Chessie.ErrorHandling
    open Functions.Types
    open Functions.Fakes
    open Functions.Database
    open TestFixture
    open PostgresContainer
    open FsUnit.Xunit
    open Database.Fakes

    let awaitAndUnpack<'T> (asyncResult:Async<Result<'T,_>>) = 
        let result = asyncResult |> Async.RunSynchronously
        match result with 
        | Ok(value, _) -> value
        | Bad(msgs) -> 
            msgs
            |> sprintf "Failed to unpack result of type %s: %A" typedefof<'T>.Name 
            |> System.Exception 
            |> raise

    type Name = Name of string
    let name = Name(null)

    type UnitsDto(output: ITestOutputHelper)=
        inherit DatabaseIntegrationTestBase()
        let repo = DatabaseRepository(testConnectionString) :> IDataRepository

        [<Fact>]
        member __.``Units have non-zero IDs`` () =
            let actual = (repo.GetUnits None) |> awaitAndUnpack
            Assert.True(actual |> Seq.forall (fun a -> a.Id <> 0))

        [<Fact>]
        member __.``Should return all units`` () = 
            let actual = 
                (repo.GetUnits None) 
                |> awaitAndUnpack 
                |> Seq.map (fun f -> f.Name) 
                |> Seq.sort
            let expected = [cityOfPawnee.Name; fourthFloor.Name; parksAndRec.Name;]
            Assert.Equal(expected, actual)

        [<Fact>]
        member __.``Should search`` () = 
            let actual = 
                repo.GetUnits (Some("Fourth"))
                |> awaitAndUnpack 
                |> Seq.map (fun f -> f.Name) 
            let expected = [fourthFloor.Name]
            Assert.Equal(expected, actual)

    type UnitDto(output: ITestOutputHelper)=
        inherit DatabaseIntegrationTestBase()
        let repo = DatabaseRepository(testConnectionString) :> IDataRepository
        let actual = (repo.GetUnits None) |> awaitAndUnpack |> Seq.head

        // [<Fact>]
        member __.``Properties`` () = 
            actual.Id |> should greaterThan 0
            actual.Name |> should equal cityOfPawnee.Name
            actual.Url |> should equal cityOfPawnee.Url
            actual.Description |> should equal cityOfPawnee.Description
        
        // [<Fact>]
        // member __.``Parent`` () = 
        //     let expected = Some({city with Id=cityId})
        //     actual.Parent |> should equal expected
        
        // [<Fact>]
        // member __.``Children`` () = 
        //     let children = actual.Children |> Seq.toList
        //     children |> should haveLength 1
        //     let actual = children |> Seq.head
        //     actual.Id |> should equal fourthFloorId
        //     actual.Name |> should equal fourthFloor.Name
        //     actual.Description |> should equal fourthFloor.Description

        //  [<Fact>]
        // member __.``Members count`` () =
        //     actual.Members |> Seq.length |> should equal 3

        //  [<Fact>]
        // member __.``Member - Swanson`` () =
        //     let actual = actual.Members |> Seq.find (fun m -> m.Id = swansonId)
        //     actual.Name |> should equal swanson.Name
        //     actual.Description |> should equal swanson.NetId
        //     actual.Title |> should equal "Director"
        //     actual.Role |> should equal Role.Leader
        //     actual.Percentage |> should equal 100
        //     actual.Tools |> Seq.toList |> should equal [Tools.AccountMgt]

        //  [<Fact>]
        // member __.``Member - Knope`` () =
        //     let actual = actual.Members |> Seq.find (fun m -> m.Id = knopeId)
        //     actual.Name |> should equal knope.Name
        //     actual.Description |> should equal knope.NetId
        //     actual.Title |> should equal "Deputy Director"
        //     actual.Role |> should equal Role.Sublead
        //     actual.Percentage |> should equal 100
        //     actual.Tools |> Seq.toList |> should be Empty

        //  [<Fact>]
        // member __.``Member - Li'l Sebastian`` () = 
        //     let actual = actual.Members |> Seq.find (fun m -> m.Id = sebastianId)
        //     actual.Id |> should equal sebastianId
        //     actual.Name |> should equal sebastian.Name
        //     actual.Description |> should equal sebastian.NetId
        //     actual.Title |> should equal "Mascot"
        //     actual.Role |> should equal Role.Member
        //     actual.Percentage |> should equal 100
        //     actual.Tools |> Seq.toList |> should be Empty
