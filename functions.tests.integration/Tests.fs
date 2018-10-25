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
    open System

    // Generally:
    // 1. Go fetch the mssql-server image.
    // 2. Create and start a container.
    // 3. Migrate and populate the DB with test data
    // 4. Exercise DB code
    // 5. Stop and remove the container.

    type IntegrationFixture() =
        let mutable serverAlreadyStarted = false
        do
            SimpleCRUD.SetDialect(SimpleCRUD.Dialect.PostgreSQL)
            serverAlreadyStarted <- ensureStarted () |> Async.RunSynchronously
        interface IDisposable with
            member __.Dispose () =
                if not serverAlreadyStarted then stop () |> ignore

    [<CollectionDefinition("Integration collection")>]
    type IntegrationCollection() =
        interface ICollectionFixture<IntegrationFixture>

    [<Collection("Integration collection")>]
    type IntegrationTestBase(fixture: IntegrationFixture) =
        do migrate ()

    type IntegrationTestProofOfConcept(fixture: IntegrationFixture) =
        inherit IntegrationTestBase(fixture)

        [<Fact>]
        member __.``Get unit from DB 1`` () = async {
            use cn = new NpgsqlConnection(connStr)
            let! id = cn.InsertAsync<Unit>(cito) |> Async.AwaitTask
            Console.WriteLine(sprintf "cito id: %d" (id.GetValueOrDefault(0)))
            let! actual = cn.GetAsync<Unit>(id) |> Async.AwaitTask
            let expected = {cito with Id=id.GetValueOrDefault(0)}
            Assert.Equal(expected, actual)
        }

        // [<Fact>]
        // member __.``Get unit from DB 2`` () = async {
        //     use cn = new NpgsqlConnection(connStr)
        //     let! id = cn.InsertAsync<Unit>(biology) |> Async.AwaitTask
        //     Console.WriteLine(sprintf "bio id: %d" (id.GetValueOrDefault(0)))
        //     let! all = cn.GetListAsync<Unit>() |> Async.AwaitTask
        //     all |> Seq.iter (fun u -> u.ToString() |> Console.WriteLine)
        //     let! actual = cn.GetAsync<Unit>(id) |> Async.AwaitTask
        //     let expected = {biology with Id=id.GetValueOrDefault(0)}
        //     Assert.Equal(expected, actual)
        // }
