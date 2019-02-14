// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Tests

open Chessie.ErrorHandling
open Functions.Api
open Xunit


module ApiTests =

    [<Fact>]
    let ``can generate OpenAPI spec`` ()=
        let result = generateOpenAPISpec ()
        ()         
