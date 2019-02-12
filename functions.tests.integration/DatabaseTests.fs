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

    let await (asyncResult:Async<Result<'T,_>>) = 
        asyncResult |> Async.RunSynchronously

    let awaitAndUnpack<'T> (asyncResult:Async<Result<'T,_>>) = 
        match (await asyncResult) with 
        | Ok(value, _) -> value
        | Bad(msgs) -> 
            msgs
            |> sprintf "Failed to unpack result of type %s: %A" typedefof<'T>.Name 
            |> System.Exception 
            |> raise

    type Name = Name of string
    let name = Name(null)

    type UnitsRead(output: ITestOutputHelper)=
        inherit DatabaseIntegrationTestBase()
        let repo = DatabaseRepository(testConnectionString)

        [<Fact>]
        member __.``Units have non-zero IDs`` () =
            let actual = (repo.Units.GetAll None) |> awaitAndUnpack
            Assert.True(actual |> Seq.forall (fun a -> a.Id <> 0))

        [<Fact>]
        member __.``Get all top-level units`` () = 
            let actual = repo.Units.GetAll None |> awaitAndUnpack

            actual |> Seq.length |> should equal 1
            actual |> should contain cityOfPawnee

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
        member __.``Get members`` () = 
            let actual = repo.Units.GetMembers parksAndRec.Id |> awaitAndUnpack

            actual |> Seq.length |> should equal 3
            let ids = actual |> Seq.map (fun a -> a.Id)
            ids |> should contain swansonMembership.Id
            ids |> should contain knopeMembership.Id
            ids |> should contain parksAndRecVacancy.Id

        [<Fact>]
        member __.``Get children`` () = 
            let actual = repo.Units.GetChildren cityOfPawnee.Id |> awaitAndUnpack

            actual |> Seq.length |> should equal 2
            actual |> should contain parksAndRec
            actual |> should contain fourthFloor

        [<Fact>]
        member __.``Get supported departments`` () = 
            let actual = repo.Units.GetSupportedDepartments cityOfPawnee.Id |> awaitAndUnpack

            Seq.length actual |> should equal 1
            actual |> should contain supportRelationship

    type UnitsWrite(output: ITestOutputHelper)=
        inherit DatabaseIntegrationTestBase()
        let repo = DatabaseRepository(testConnectionString)

        [<Fact>]
        member __.``Create`` () = 
            let expected = { Id=0; Name="Test"; Description="Desc"; Url="Url"; ParentId=Some(1) }
            
            let actual = repo.Units.Create expected |> awaitAndUnpack
            let retrieved = repo.Units.Get actual.Id |> awaitAndUnpack

            actual |> should equal { expected with Id=actual.Id }
            retrieved |> should equal { expected with Id=actual.Id }

        [<Fact>]
        member __.``Update`` () = 
            let expected = { Id=1; Name="Test"; Description="Desc"; Url="Url"; ParentId=Some(1) }

            let actual = repo.Units.Update expected |> awaitAndUnpack
            let retrieved = repo.Units.Get expected.Id |> awaitAndUnpack
            
            actual |> should equal expected
            retrieved |> should equal expected

        [<Fact>]
        member __.``Delete`` () = 
            let _ = repo.Units.Delete fourthFloor |> awaitAndUnpack
            
            let actual = repo.Units.Get fourthFloor.Id |> await
            match actual with 
            | Bad([(status, msg)]) -> status |> should equal Status.NotFound
            | _ -> System.Exception("Should have failed") |> raise
            