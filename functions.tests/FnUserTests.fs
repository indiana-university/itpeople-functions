namespace Tests

open Chessie.ErrorHandling
open MyFunctions.Common.Types
open MyFunctions.Common.Jwt
open MyFunctions.User
open Xunit

module FnUserTests =

    let getUserById id = 
        MyFunctions.Common.Fakes.getFakeProfile()

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
