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
            cn.InsertAsync<Unit>(cito) |> Async.AwaitTask |> Async.RunSynchronously |> ignore

        [<Fact>]
        member __.``Ensure unit has non-0 id`` () = async {
            use cn = dbConnection ()
            let! actual = cn.GetListAsync<Unit>() |> Async.AwaitTask
            let head = Seq.head actual
            Assert.NotEqual(0, head.Id)
        }

        [<Fact>]
        member __.``Ensure unit has correct properties`` () = async {
            use cn = dbConnection ()
            let! actual = cn.GetListAsync<Unit>() |> Async.AwaitTask
            let head = Seq.head actual
            Assert.Equal(cito, head)
        }