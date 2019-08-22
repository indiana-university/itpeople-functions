// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Tests

open Core.Types
open Core.Fakes
open Functions.Api
open Xunit


module ApiTests =

    [<Fact>]
    let ``can generate OpenAPI spec`` ()=
        let result = generateOpenAPISpec ()
        ()         


    [<Fact>]
    let ``can serialize XML`` ()=
        let expected = """<?xml version="1.0" encoding="utf-16"?>
<ArrayOfLspInfo xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <LspInfo>
    <IsLA>false</IsLA>
    <NetworkID>lknope</NetworkID>
  </LspInfo>
</ArrayOfLspInfo>"""
        let actual = 
            { LspInfos = [| lspInfo|]} 
            |> serializeXml<LspInfoArray>
        Assert.Equal(expected, actual)

    [<Fact>]
    let ``can make XML content`` ()=
        let actual = 
            { LspInfos = [| lspInfo|]} 
            |> xmlResponse<LspInfoArray>
        printfn "XML content: %s" (actual.ToString())
