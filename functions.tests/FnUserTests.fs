namespace Tests

open Chessie.ErrorHandling
open MyFunctions.Types
open MyFunctions.User
open MyFunctions.Common
open System
open Xunit
open System.Net.Http
open Microsoft.AspNetCore.Http

module FnUserTests =

    let getUserById id = 
        MyFunctions.Fakes.getFakeProfile()

    let await fn = 
        fn 
        |> Async.ofAsyncResult 
        |> Async.RunSynchronously

    [<Fact>]
    let ``getMe requires JWT`` () =
        let expected = Bad ([(Status.Unauthorized, MissingAuthHeader)])
        let req = TestFakes.requestWithNoJwt
        let appConfig = TestFakes.appConfig
        let actual = getMe req appConfig getUserById |> await
        Assert.Equal(expected, actual)
