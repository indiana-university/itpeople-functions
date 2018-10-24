namespace Integration 
open MyFunctions.Common.Types
open MyFunctions.Common

module Tests=

    open System
    open Xunit
    open SqlServerContainer
    open Chessie.ErrorHandling
    open MyFunctions.Common

    // Generally:
    // 1. Go fetch the mssql-server image.
    // 2. Create and start a container.
    // 3. Migrate and populate the DB with test data
    // 4. Exercise DB code
    // 5. Stop and remove the container.

    [<Fact>]
    let ``Get unit from DB`` () = async {
        // try 
            // let! started = start ()
            do! ensureReady()
            migrate ()
            let! id = populate ()
            let expected = Ok({Fakes.cito with Id=id},[])
            let! actual = Database.queryUnit connStr id |> Async.ofAsyncResult
            let actualUnit = lift (fun a -> a.Unit) actual
            Assert.Equal(expected, actualUnit)
        // finally
            // stop () |> ignore
    }

