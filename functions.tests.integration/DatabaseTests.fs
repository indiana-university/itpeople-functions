namespace Integration 

module DatabaseTests=

    open System
    open Xunit
    open Dapper
    open Chessie.ErrorHandling
    open MyFunctions.Common.Types
    open MyFunctions.Common.Fakes
    open MyFunctions.Common.Database
    open TestFixture
    open PostgresContainer
    
    // type DatabaseTests() =
    //     inherit IntegrationTestBase()
    //     do  
    //         use cn = dbConnection ()
    //         cn.InsertAsync<Unit>(cito) |> Async.AwaitTask |> Async.RunSynchronously |> ignore

    //     [<Fact>]
    //     member __.``Ensure unit has non-0 id`` () = async {
    //         use cn = dbConnection ()
    //         let! actual = cn.GetListAsync<Unit>() |> Async.AwaitTask
    //         let head = Seq.head actual
    //         Assert.NotEqual(0, head.Id)
    //     }

    //     [<Fact>]
    //     member __.``Ensure unit has correct name`` () = async {
    //         use cn = dbConnection ()
    //         let! actual = cn.GetListAsync<Unit>() |> Async.AwaitTask
    //         let head = Seq.head actual
    //         Assert.Equal(cito.Name, head.Name)
    //     }