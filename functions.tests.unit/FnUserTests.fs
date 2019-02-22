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


module UtilTests =

    [<Fact>]
    let ``can map enum flags to array`` ()=
        let expected = [ Tools.AccountMgt; Tools.AMSAdmin ]
        let actual = (Tools.AMSAdmin ||| Tools.AccountMgt) |> mapFlagsToSeq<Tools>
        Assert.Equal(expected, actual)
