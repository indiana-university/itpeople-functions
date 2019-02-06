// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open Types
open Json
open Chessie.ErrorHandling
open Swashbuckle.AspNetCore.Filters


module Fakes =

    // UaaResponse 
    let accessToken = { access_token = "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJleHAiOiIxNTE1NTQ0NjQzIiwidXNlcl9pZCI6MSwidXNlcl9uYW1lIjoiam9obmRvZSIsInVzZXJfcm9sZSI6ImFkbWluIn0.akuT7-xDFxrev-T9Dv0Wdumx1HK5L2hQAOU51igIjUE" }

    // Units
    let cityOfPawnee:Unit = {Id=0; Name="City of Pawnee"; Description="City of Pawnee, Indiana"; Url=""; ParentId=None}
    let parksAndRec:Unit = {Id=0; Name="Parks and Rec"; Description="Parks and Recreation"; Url=""; ParentId=Some(cityOfPawnee.Id)}
    let fourthFloor:Unit = {Id=0; Name="Fourth Floor"; Description="It's spooky up there!"; Url=""; ParentId=Some(cityOfPawnee.Id)}

    // Departments
    let parksDept:Department = {Id=0; Name="PA-PARKS"; Description="Parks and Recreation Department" }
    
    // People
    let swanson:Person = {
        Id=0
        Hash="hash"
        NetId="rswanso"
        Name="Swanson, Ron"
        Position="Parks and Rec Director "
        Location=""
        Campus=""
        CampusPhone=""
        CampusEmail="rswanso@pawnee.in.us"
        Expertise="Woodworking; Honor"
        Notes=""
        PhotoUrl="http://flavorwire.files.wordpress.com/2011/11/ron-swanson.jpg"
        Tools = Tools.ItProMail ||| Tools.ItProWeb
        Responsibilities = Responsibilities.ItLeadership
        HrDepartmentId=parksDept.Id
    }

    let knope:Person = {
        Id=0
        Hash="hash"
        NetId="lknope"
        Name="Knope, Lesie Park"
        Position="Parks and Rec Deputy Director "
        Location=""
        Campus=""
        CampusPhone=""
        CampusEmail="lknope@pawnee.in.us"
        Expertise="Canvasing; Waffles"
        Notes=""
        PhotoUrl="https://en.wikipedia.org/wiki/Leslie_Knope#/media/File:Leslie_Knope_(played_by_Amy_Poehler).png"
        Tools = Tools.ItProMail ||| Tools.ItProWeb
        Responsibilities = Responsibilities.ItLeadership ||| Responsibilities.ItProjectMgt
        HrDepartmentId=parksDept.Id
    }

    let sebastian:Person = {
        Id=0
        Hash="hash"
        NetId="lsebastian"
        Name="Sebastian, L'il"
        Position="Mascot and Guiding Light"
        Location=""
        Campus=""
        CampusPhone=""
        CampusEmail="lknope@pawnee.in.us"
        Expertise="Hay; Being Small"
        Notes=""
        PhotoUrl="https://sasquatchbrewery.com/wp-content/uploads/2018/06/lil.jpg"
        Tools = Tools.ItProMail ||| Tools.ItProWeb
        Responsibilities = Responsibilities.UserExperience
        HrDepartmentId=parksDept.Id
    }

    let fakeSimpleSearchResult = 
      { Users=
          [ { swanson with Id=1 }
            { knope with Id=2 }
            { sebastian with Id=3 } ]
        Departments=
          [ { parksDept with Id=1 } ]
        Units=
          [ { cityOfPawnee with Id=1 }
            { parksAndRec with Id=2 }
            { fourthFloor with Id=3 } ] }

    /// A canned data implementation of IDatabaseRespository (for testing)

    let satisfyWith a = async { return! a |> ok |> async.Return }

    type FakesRepository() =
        interface IDataRepository with 
            member this.TryGetPersonId netId = (swanson.NetId, swanson.Id) |> satisfyWith
            member this.GetPeople query = [ swanson ] |> List.toSeq |> satisfyWith
            member this.GetPerson id = swanson |> satisfyWith
            member this.GetSimpleSearchByTerm term = fakeSimpleSearchResult |> satisfyWith
            member this.GetUnits query = [ parksAndRec ] |> List.toSeq |> satisfyWith
            member this.GetUnit id = parksAndRec |> satisfyWith
            member this.CreateUnit unit = parksAndRec |> satisfyWith
            member this.UpdateUnit id unit = parksAndRec |> satisfyWith
            member this.GetDepartments query = [ parksDept ] |> List.toSeq |> satisfyWith
            member this.GetDepartment id = parksDept |> satisfyWith

    type JwtResponseExample() =
        interface IExamplesProvider<JwtResponse> with
            member this.GetExamples() = accessToken

    type UnitExample() =
        interface IExamplesProvider<Unit> with
            member this.GetExamples () = parksAndRec

    type UnitsExample() =
        interface IExamplesProvider<seq<Unit>> with
            member this.GetExamples () = [ parksAndRec ] |> List.toSeq

    type DepartmentExample() =
        interface IExamplesProvider<Department> with
            member this.GetExamples () = parksDept
 
    type DepartmentsExample() =
        interface IExamplesProvider<seq<Department>> with
            member this.GetExamples () = [ parksDept ] |> List.toSeq
    
    type SimpleSearchExample() =
        interface IExamplesProvider<SimpleSearch> with
            member this.GetExamples () = fakeSimpleSearchResult

    type PersonExample() =
        interface IExamplesProvider<Person> with
            member this.GetExamples () = swanson