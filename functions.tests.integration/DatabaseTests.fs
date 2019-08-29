// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Integration 

module DatabaseTests=

    open Xunit
    open Xunit.Abstractions
    open Core.Types
    open Core.Fakes
    open TestFixture
    open FsUnit.Xunit
    open Database.Fakes
    open Functions.DatabaseRepository

    let await (asyncResult:Async<Result<'T,_>>) = 
        asyncResult |> Async.RunSynchronously

    let awaitAndUnpack<'T> (asyncResult:Async<Result<'T,_>>) = 
        match (await asyncResult) with 
        | Ok value  -> value
        | Error msg -> 
            msg
            |> sprintf "Failed to unpack result of type %s: %A" typedefof<'T>.Name 
            |> System.Exception 
            |> raise

    type AuthTests(output:ITestOutputHelper)=
        inherit DatabaseIntegrationTestBase()
        let repo = Functions.DatabaseRepository.People(testConnectionString)

        [<Fact>]
        member __.``Fetches some person ID for valid netid`` () =
            let (netid, id) = (repo.TryGetId knope.NetId) |> awaitAndUnpack
            netid |> should equal knope.NetId
            id |> should equal (Some(knope.Id))

        [<Fact>]
        member __.``Fetches no person ID for invalid netid`` () =
            let (netid, id) = (repo.TryGetId "zzz") |> awaitAndUnpack
            netid |> should equal "zzz"
            id |> should equal (None)

    type UnitsRead(output: ITestOutputHelper)=
        inherit DatabaseIntegrationTestBase()
        let repo = Repository(testConnectionString)

        [<Fact>]
        member __.``Units have non-zero IDs`` () =
            let actual = (repo.Units.GetAll None) |> awaitAndUnpack
            Assert.True(actual |> Seq.forall (fun a -> a.Id <> 0))

        [<Fact>]
        member __.``Get all top-level units`` () = 
            let actual = repo.Units.GetAll None |> awaitAndUnpack

            actual |> Seq.length |> should equal 2
            actual |> should contain cityOfPawnee
            actual |> should contain edgeUnit

        [<Fact>]
        member __.``Get one`` () = 
            let actual = repo.Units.Get cityOfPawnee.Id |> awaitAndUnpack

            actual |> should equal cityOfPawnee

        [<Fact>]
        member __.``Search`` () = 
            let actual = repo.Units.GetAll (Some("Fourth")) |> awaitAndUnpack

            actual |> Seq.length |> should equal 1
            actual |> should contain fourthFloor
        
        [<Fact>]
        member __.``Get members with notes`` () = 

            let actual = 
                parksAndRec
                |> MembersWithNotes 
                |> repo.Units.GetMembers  
                |> awaitAndUnpack

            actual |> Seq.length |> should equal 3

            let ids = actual |> Seq.map (fun a -> a.Id)
            ids |> should contain swansonMembership.Id
            ids |> should contain knopeMembership.Id
            ids |> should contain parksAndRecVacancy.Id

            let knope = actual |> Seq.find (fun a -> a.Id = knopeMembership.Id)
            knope.Notes = knopeMembership.Notes

        [<Fact>]
        member __.``Get members without notes`` () = 

            let actual = 
                parksAndRec
                |> MembersWithoutNotes
                |> repo.Units.GetMembers  
                |> awaitAndUnpack

            actual |> Seq.length |> should equal 3
            actual |> Seq.forall (fun a -> a.Notes = "")

        [<Fact>]
        member __.``Get children`` () = 
            let actual = repo.Units.GetChildren cityOfPawnee |> awaitAndUnpack

            actual |> Seq.length |> should equal 2
            actual |> should contain parksAndRec
            actual |> should contain fourthFloor

        [<Fact>]
        member __.``Get supported departments`` () = 
            let actual = repo.Units.GetSupportedDepartments cityOfPawnee |> awaitAndUnpack

            Seq.length actual |> should equal 1
            actual |> should contain supportRelationship

    type UnitsWrite(output: ITestOutputHelper)=
        inherit DatabaseIntegrationTestBase()
        let repo = Repository(testConnectionString)

        [<Fact>]
        member __.``Create`` () = 
            let expected = { Id=0; Name="Test"; Description="Desc"; Url="Url"; ParentId=Some(cityOfPawnee.Id); Parent=Some(cityOfPawnee) }

            let actual = repo.Units.Create expected |> awaitAndUnpack
            let retrieved = repo.Units.Get actual.Id |> awaitAndUnpack

            actual |> should equal { expected with Id=actual.Id }
            retrieved |> should equal actual

        [<Fact>]
        member __.``Update`` () = 
            let expected = { Id=fourthFloor.Id; Name="Fourth Floor vNext"; Description="Re-org Fourth Flor"; Url="Url"; ParentId=Some(parksAndRec.Id); Parent=Some({parksAndRec with Parent=None}) }

            let actual = repo.Units.Update expected  |> awaitAndUnpack
            let retrieved = repo.Units.Get expected.Id |> awaitAndUnpack
            
            actual |> should equal expected
            retrieved |> should equal expected

        [<Fact>]
        member __.``Delete`` () = 
            let _ = repo.Units.Delete fourthFloor |> awaitAndUnpack
            
            let actual = repo.Units.Get fourthFloor.Id |> await
            match actual with 
            | Error (status, msg) -> status |> should equal Status.NotFound
            | _ -> System.Exception("Should have failed") |> raise

        [<Fact>]
        member __.``Gets unit when descended from parent`` () = 
            // This request should return parksAndRec because it is a child unit of the City of Pawnee
            let actual = repo.Units.GetDescendantOfParent (cityOfPawnee.Id, parksAndRec.Id) |> awaitAndUnpack
            
            actual.IsSome |> should be True
            actual.Value.Name |> should equal (parksAndRec.Name)
 
        [<Fact>]
        member __.``Doesn't get unit when not descended from parent`` () = 
            // This request should not return parksAndRec because it is not a child unit the Fourth Floor
            let actual = repo.Units.GetDescendantOfParent (fourthFloor.Id, parksAndRec.Id) |> awaitAndUnpack
            
            actual.IsNone |> should be True
        
            