module Tests

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
let ``Get unit from DB`` () = 
    let container = DateTime.Now.Ticks |> sprintf "mssql-test-%d" 
    let started = container |> start |> Async.RunSynchronously
    migrate ()
    let id = populate () |> Async.RunSynchronously
    let unit = Database.queryUnit connStr id |> Async.ofAsyncResult |> Async.RunSynchronously
    unit.ToString() |> Console.WriteLine
    let stopped = container |> stop |> Async.RunSynchronously
    container |> delete |> Async.RunSynchronously
    Assert.True(started)

