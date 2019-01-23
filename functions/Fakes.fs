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
    let city:Unit = {Id=0; Name="City of Pawnee"; Description="City of Pawnee, Indiana"; Url=""}
    let parksAndRec:Unit = {Id=0; Name="Parks and Rec"; Description="Parks and Recreation"; Url=""}
    let fourthFloor:Unit = {Id=0; Name="Fourth Floor"; Description="It's spooky up there!"; Url=""}

    // Departments
    let parksDept:Department = {Id=0; Name="PA-PARKS"; Description="Parks and Recreation Department"; DisplayUnits=true}
    
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

    // People DTOs
    let swansonDto:PersonDto = {
        Id=swanson.Id
        NetId=swanson.NetId
        Name=swanson.Name
        Position=swanson.Position
        Location=swanson.Location
        Campus=swanson.Campus
        CampusPhone=swanson.CampusPhone
        CampusEmail=swanson.CampusEmail
        Expertise=swanson.Expertise.Split(";")
        Notes=swanson.Notes
        PhotoUrl=swanson.PhotoUrl
        Tools = swanson.Tools |> mapFlagsToSeq
        Responsibilities = swanson.Responsibilities |> mapFlagsToSeq
        Department=parksDept
        UnitMemberships=
          [ {Id=parksAndRec.Id; Name=parksAndRec.Name; Description=""; Role=Role.Leader; Title="Director"; Tools=[ Tools.AccountMgt ]; PhotoUrl=swanson.PhotoUrl; Percentage=100} ]
    }

    let knopeDto:PersonDto = {
        Id=knope.Id
        NetId=knope.NetId
        Name=knope.Name
        Position=knope.Position
        Location=knope.Location
        Campus=knope.Campus
        CampusPhone=knope.CampusPhone
        CampusEmail=knope.CampusEmail
        Expertise=knope.Expertise.Split(";")
        Notes=knope.Notes
        PhotoUrl=knope.PhotoUrl
        Tools = knope.Tools |> mapFlagsToSeq
        Responsibilities = knope.Responsibilities |> mapFlagsToSeq
        Department=parksDept
        UnitMemberships=
          [ {Id=parksAndRec.Id; Name=parksAndRec.Name; Description=""; Role=Role.Sublead; Title="Deputy Director"; Tools=[ ]; PhotoUrl=knope.PhotoUrl; Percentage=100} ]
    }

    let sebastianDto:PersonDto = {
        Id=sebastian.Id
        NetId=sebastian.NetId
        Name=sebastian.Name
        Position=sebastian.Position
        Location=sebastian.Location
        Campus=sebastian.Campus
        CampusPhone=sebastian.CampusPhone
        CampusEmail=sebastian.CampusEmail
        Expertise=sebastian.Expertise.Split(";")
        Notes=sebastian.Notes
        PhotoUrl=sebastian.PhotoUrl
        Tools = sebastian.Tools |> mapFlagsToSeq
        Responsibilities = sebastian.Responsibilities |> mapFlagsToSeq
        Department=parksDept
        UnitMemberships=
          [ {Id=parksAndRec.Id; Name=parksAndRec.Name; Description=""; Role=Role.Member; Title="Mascot"; Tools=[ ]; PhotoUrl=sebastian.PhotoUrl; Percentage=100} ]
        
    }

    let iuware = {Id=1; Name="IUware Tools"; Description=""}
    let itproMail = {Id=2; Name="IT Pro Mailing List"; Description=""}

    let fakePersonId = (swanson.NetId, swanson.Id)

    let fakePerson id = { swansonDto with Id=id }

    let fakeSimpleSearchResult = 
      { Users=
          [ {Id=1; Name=swanson.Name; Description=""}
            {Id=2; Name=knope.Name; Description=""}
            {Id=3; Name=sebastian.Name; Description=""} ]
        Departments=
          [ {Id=1; Name=parksDept.Name; Description=""} ]
        Units=
          [ {Id=1; Name=city.Name; Description=""}
            {Id=2; Name=parksAndRec.Name; Description=""}
            {Id=3; Name=fourthFloor.Name; Description=""} ] }

    let fakeUnits = [{parksAndRec with Id=2}; {city with Id=1}] |> List.toSeq

    let fakeUnit id = 
      { Id=id
        Name=parksAndRec.Name
        Description=parksAndRec.Description
        Url=parksAndRec.Url
        Members= [ 
            {Id=1; Name=swanson.Name; Description=""; Role=Role.Leader; Title="Director"; Tools=[ Tools.AccountMgt ]; PhotoUrl=swanson.PhotoUrl; Percentage=100}
            {Id=2; Name=knope.Name; Description=""; Role=Role.Sublead; Title="Deputy Director"; Tools=[ ]; PhotoUrl=knope.PhotoUrl; Percentage=100}
            {Id=3; Name=sebastian.Name; Description=""; Role=Role.Member; Title="Mascot"; Tools=[ ]; PhotoUrl=sebastian.PhotoUrl; Percentage=100} 
        ]
        SupportedDepartments= [ {parksDept with Id=1} ]
        Children= [ { fourthFloor with Id=3 } ]
        Parent= Some(city) }

    let fakeDepartments = [ {parksDept with Id=1} ] |> List.toSeq

    let fakeDepartment id = 
      { Id=id
        Name=parksDept.Name
        Description=parksDept.Description
        SupportingUnits=[ { fourthFloor with Id=3} ]
        Units=[ {parksAndRec with Id=2}]
        Members= 
          [ {Member.Id=1; Name=swanson.Name; Description=""}
            {Member.Id=2; Name=knope.Name; Description=""}
            {Member.Id=3; Name=sebastian.Name; Description=""} ] }

    /// A canned data implementation of IDatabaseRespository (for testing)

    let satisfyWith a = async { return! a |> ok |> async.Return }

    type FakesRepository() =
        interface IDataRepository with 
            member this.TryGetPersonId netId = fakePersonId |> satisfyWith
            member this.GetProfile id = fakePerson id |> satisfyWith
            member this.GetSimpleSearchByTerm term = fakeSimpleSearchResult |> satisfyWith
            member this.GetUnits () = fakeUnits |> satisfyWith
            member this.GetUnit id = fakeUnit id |> satisfyWith
            member this.GetDepartments () = fakeDepartments |> satisfyWith
            member this.GetDepartment id = fakeDepartment id |> satisfyWith

    type JwtResponseExample() =
        interface IExamplesProvider<JwtResponse> with
            member this.GetExamples() = accessToken

    type UnitExample() =
        interface IExamplesProvider<UnitDto> with
            member this.GetExamples () = fakeUnit 1

    type UnitsExample() =
        interface IExamplesProvider<seq<Unit>> with
            member this.GetExamples () = fakeUnits

    type DepartmentExample() =
        interface IExamplesProvider<DepartmentDto> with
            member this.GetExamples () = fakeDepartment 1
 
    type DepartmentsExample() =
        interface IExamplesProvider<seq<Department>> with
            member this.GetExamples () = fakeDepartments
    
    type SimpleSearchExample() =
        interface IExamplesProvider<SimpleSearch> with
            member this.GetExamples () = fakeSimpleSearchResult

    type PersonExample() =
        interface IExamplesProvider<PersonDto> with
            member this.GetExamples () = fakePerson 1