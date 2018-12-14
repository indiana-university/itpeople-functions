// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Tests

open Chessie.ErrorHandling
open Functions.Types
open Functions.Jwt
open Functions.Util
open Functions.Json
open Functions.Api
open Xunit

module FnUserTests =

    let getUserById id = 
        Functions.Fakes.getFakeProfile()


    // [<Fact>]
    // let ``getMe requires JWT`` () =
    //     let expected = Bad ([(Status.Unauthorized, MissingAuthHeader)])
    //     let req = TestFakes.requestWithNoJwt
    //     let appConfig = TestFakes.appConfig
    //     let actual = await authorizeRequest' req appConfig fakeTrial
    //     Assert.Equal(expected, actual)

module UtilTests =

    [<Fact>]
    let ``can map enum flags to array`` ()=
        let expected = [ Tools.AccountMgt; Tools.AMSAdmin ]
        let actual = (Tools.AMSAdmin ||| Tools.AccountMgt) |> mapFlagsToSeq<Tools>
        Assert.Equal(expected, actual)
