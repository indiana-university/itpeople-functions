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
    
    type DatabaseTests(output: ITestOutputHelper)=
        inherit DatabaseIntegrationTestBase()
        do  
            use cn = dbConnection ()
            cn.InsertAsync<Unit>(city) |> Async.AwaitTask |> Async.RunSynchronously |> ignore
            cn.InsertAsync<Unit>(parksAndRec) |> Async.AwaitTask |> Async.RunSynchronously |> ignore

        [<Fact>]
        member __.``Ensure all units have a non-0 id`` () = async {
            use cn = dbConnection ()
            let! actual = cn.GetListAsync<Unit>() |> Async.AwaitTask
            Assert.True(actual |> Seq.forall (fun a -> a.Id <> 0))
        }

        [<Fact>]
        member __.``Ensure parksAndRec has correct properties`` () = async {
            use cn = dbConnection ()
            let! actual = cn.GetAsync<Unit>(parksAndRec.Id) |> Async.AwaitTask
            Assert.Equal(parksAndRec, actual)
        }