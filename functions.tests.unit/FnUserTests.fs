namespace Tests

open Chessie.ErrorHandling
open Functions.Common.Types
open Functions.Common.Jwt
open Functions.Common.Util
open Functions.Api.Common
open Xunit

module FnUserTests =

    let getUserById id = 
        Functions.Common.Fakes.getFakeProfile()

    let await fn = 
        fn 
        |> Async.ofAsyncResult 
        |> Async.RunSynchronously

    let fakeTrial user = asyncTrial {
        return "ok!"
    }

    [<Fact>]
    let ``getMe requires JWT`` () =
        let expected = Bad ([(Status.Unauthorized, MissingAuthHeader)])
        let req = TestFakes.requestWithNoJwt
        let appConfig = TestFakes.appConfig
        let actual = doWithAuth req appConfig fakeTrial |> await
        Assert.Equal(expected, actual)

module UtilTests =

    [<Fact>]
    let ``can map enum flags to array`` ()=
        let expected = [ Tools.AccountMgt; Tools.AMSAdmin ]
        let actual = (Tools.AMSAdmin ||| Tools.AccountMgt) |> mapFlagsToSeq<Tools>
        Assert.Equal(expected, actual)
