// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Tests

open Chessie.ErrorHandling
open Functions.Types
open Functions.Http
open Functions.Fakes
open Functions.Json
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
            Url= "url"
          }, [])
        let actual = tryDeserialize Status.BadRequest """{
          "id": 0,
          "name": "name",
          "url": "url",
          "parentId": 1,
          "description": "description"
        }"""
        Assert.Equal(expected, actual);
