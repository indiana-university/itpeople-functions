// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Tests

open Core.Types
open Core.Json
open System
open Xunit
open Newtonsoft.Json

module JsonTests =

    [<Fact>]
    let ``Deserialize record with Option type`` () =
        let expected = 
          Ok({
            Id= 0
            Name= "name"
            Description= "description"
            ParentId= Some(1)
            Parent=None
            Url= "url"
            Email="email@example.com"
          })
        let actual = tryDeserialize Status.BadRequest """{
          "id": 0,
          "name": "name",
          "url": "url",
          "email": "email@example.com",
          "parentId": 1,
          "description": "description"
        }"""
        Assert.Equal(expected, actual);

    type DU =
    | Foo of int * string
    | Bar of string * string

    [<Fact>]
    let ``Serialize complex DU`` () = 
      let expected = Foo(3, "hello")
      // printfn "DU tostring: %A" expected
      let json = serialize expected
      // printfn "serialized: %s" json
      let actual = deserialize<DU> json
      Assert.Equal(expected, actual);

    [<Fact>]
    let ``Deserialize PersonRequest`` () =
      let json = """{"id":0, "expertise":"Pawnee History", "responsibilities":"UserExperience,BizSysAnalysis", "location":"JJ's Diner"}"""
      let actual = deserialize<PersonRequest> json
      Assert.Equal("Pawnee History", actual.Expertise)
      Assert.Equal(Responsibilities.UserExperience|||Responsibilities.BizSysAnalysis, actual.Responsibilities)
