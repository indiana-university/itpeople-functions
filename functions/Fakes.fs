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

    let swansonMembership:UnitMember = {
        Id=0
        UnitId=0
        PersonId=0
        Unit=parksAndRec
        Person=Some(swanson)
        Title="Director"
        Role=Role.Leader
        Permissions=Permissions.Owner
        Percentage=100
        Tools=Tools.SuperPass
    }

    let supportRelationship:SupportRelationship = {
        Id=0
        UnitId=0
        DepartmentId=0
        Unit=parksAndRec
        Department=parksDept
    }

    /// A canned data implementation of IDatabaseRespository (for testing)


    type FakesRepository() =
        interface IDataRepository with 
            member this.TryGetPersonId netId = stub (swanson.NetId, swanson.Id)
            member this.GetPeople query =  stub ([ swanson ] |> List.toSeq)
            member this.GetPerson id = stub swanson
            member this.GetPersonMemberships personId = stub ([ swansonMembership ] |> List.toSeq)
            member this.GetUnits query = stub ([ parksAndRec ] |> List.toSeq)
            member this.GetUnit id = stub parksAndRec
            member this.GetUnitMembers id = stub ([ swansonMembership ] |> List.toSeq) 
            member this.GetUnitChildren id = stub ([ fourthFloor ] |> List.toSeq) 
            member this.GetUnitSupportedDepartments id = stub ([ supportRelationship ] |> List.toSeq) 
            member this.GetMembership id = stub swansonMembership 
            member this.GetMemberships () = stub ([ swansonMembership ] |> List.toSeq) 
            member this.CreateMembership membership = stub membership
            member this.UpdateMembership id membership = stub membership
            member this.DeleteMembership id = stub ()
            member this.CreateUnit unit = stub parksAndRec
            member this.UpdateUnit id unit = stub parksAndRec
            member this.DeleteUnit id = stub ()
            member this.GetDepartments query = stub ([ parksDept ] |> List.toSeq)
            member this.GetDepartment id = stub parksDept
            member this.GetDepartmentMemberUnits id = stub ([ parksAndRec ] |> List.toSeq)
            member this.GetDepartmentSupportingUnits id = stub ([ supportRelationship ] |> List.toSeq)
            member this.GetSupportRelationships () = stub ([ supportRelationship ] |> List.toSeq) 
            member this.GetSupportRelationship id = stub supportRelationship
            member this.CreateSupportRelationship supportRelationship = stub supportRelationship
            member this.UpdateSupportRelationship id supportRelationship = stub supportRelationship
            member this.DeleteSupportRelationship id = stub ()
           

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
    
    type PersonExample() =
        interface IExamplesProvider<Person> with
            member this.GetExamples () = swanson
    
    type PeopleExample() =
        interface IExamplesProvider<seq<Person>> with
            member this.GetExamples () = [ swanson; knope ] |> List.toSeq

    type PersonMembershipsExample() =
        interface IExamplesProvider<seq<UnitMember>> with
            member this.GetExamples () = [ swansonMembership ] |> List.toSeq

    type PersonMembershipExample() =
        interface IExamplesProvider<UnitMember> with
            member this.GetExamples () = swansonMembership