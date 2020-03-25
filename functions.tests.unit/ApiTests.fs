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
        let expected = """<?xml version="1.0" encoding="utf-8"?>
<ArrayOfLspInfo xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <LspInfo>
    <IsLA>true</IsLA>
    <NetworkID>bwyatt</NetworkID>
  </LspInfo>
</ArrayOfLspInfo>"""
        let actual = 
            { LspInfos = [| lspInfo|]} 
            |> serializeXml<LspInfoArray>
        Assert.Equal(expected, actual)

    [<Fact>]
    let ``can parse XML as IMS`` ()=
        let xml = """<?xml version="1.0" encoding="utf-8"?>
<LspDepartment xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <DeptCodeList>
    <a>PA-PARKS</a>
  </DeptCodeList>
  <NetworkID>bwyatt</NetworkID>
</LspDepartment>"""
        let xmlDoc = System.Xml.XmlDocument()
        let reader = new System.IO.StringReader(xml)
        xmlDoc.Load(reader)
        let depts = 
             xmlDoc.ChildNodes.[1].FirstChild.ChildNodes
            |> Seq.cast<System.Xml.XmlNode>
            |> Seq.map (fun node -> node.FirstChild.Value )
            |> Seq.toList
        Assert.Equal([| parksDept.Name |], depts)

    [<Fact>]
    let ``can make XML content`` ()=
        let actual = 
            { LspInfos = [| lspInfo|]} 
            |> xmlResponse<LspInfoArray>
        printfn "XML content: %s" (actual.ToString())

    [<Fact>]
    let ``can serialize LspDepartmentArray`` ()=
        let record = 
          { DeptCodeList = { Values = [|"BL-DEP1"; "BL-DEP2" |] }
            NetworkID = "netid" } 
        let actual = record |> serializeXml<LspDepartmentArray>
        printfn "XML content: %s" (actual.ToString())
