namespace Tests

open Chessie.ErrorHandling
open Functions.Common.Types
open Functions.Common.Jwt
open Functions.Api.User
open Xunit

module FnUserTests =

    let getUserById id = 
        Functions.Common.Fakes.getFakeProfile()

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
