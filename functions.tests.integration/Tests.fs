namespace Integration 

module Tests=

    open Dapper
    open Xunit
    open SqlServerContainer
    open Chessie.ErrorHandling
    open MyFunctions.Common.Types
    open MyFunctions.Common.Fakes
    open MyFunctions.Common.Database
    open Npgsql
    
    // Generally:
    // 1. Go fetch the mssql-server image.
    // 2. Create and start a container.
    // 3. Migrate and populate the DB with test data
    // 4. Exercise DB code
    // 5. Stop and remove the container.

    [<Fact>]
    let ``Get unit from DB`` () = async {
        SimpleCRUD.SetDialect(SimpleCRUD.Dialect.PostgreSQL)
        //try 
        //    let! started = start ()
        migrate ()
        use cn = new NpgsqlConnection(connStr)
        let! id = cn.InsertAsync<Unit>(cito) |> Async.AwaitTask
        let! actual = cn.GetAsync<Unit>(id) |> Async.AwaitTask
        let expected = {cito with Id=id.GetValueOrDefault(0)}
        Assert.Equal(expected, actual)
        //finally
        //    stop () |> ignore
    }

