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
    let cityOfPawnee:Unit = {Id=1; Name="City of Pawnee"; Description="City of Pawnee, Indiana"; Url=""; ParentId=None}
    let parksAndRec:Unit = {Id=2; Name="Parks and Rec"; Description="Parks and Recreation"; Url=""; ParentId=Some(cityOfPawnee.Id)}
    let fourthFloor:Unit = {Id=3; Name="Fourth Floor"; Description="It's spooky up there!"; Url=""; ParentId=Some(cityOfPawnee.Id)}

    // Departments
    let parksDept:Department = {Id=1; Name="PA-PARKS"; Description="Parks and Recreation Department" }
    
    // People
    let swanson:Person = {
        Id=1
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
        DepartmentId=parksDept.Id
        Department=parksDept
    }

    let knope:Person = {
        Id=2
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
        DepartmentId=parksDept.Id
        Department=parksDept
    }

    let wyatt:Person = {
        Id=3
        Hash="hash"
        NetId="bwyatt"
        Name="Wyatt, Ben"
        Position="Auditor"
        Location=""
        Campus=""
        CampusPhone=""
        CampusEmail="bwyatt@pawnee.in.us"
        Expertise="Board Games; Comic Books"
        Notes=""
        PhotoUrl="https://sasquatchbrewery.com/wp-content/uploads/2018/06/lil.jpg"
        Tools = Tools.ItProMail ||| Tools.ItProWeb
        Responsibilities = Responsibilities.ItProjectMgt
        DepartmentId=parksDept.Id
        Department=parksDept
    }

    let knopeMembershipRequest:UnitMemberRequest = {
        UnitId=parksAndRec.Id
        PersonId=Some(knope.Id)
        Role=Role.Sublead
        Permissions=Permissions.Viewer
        Title="Deputy Director"
        Tools=Tools.None
        Percentage=100
    }

    let swansonMembership:UnitMember = {
        Id=1
        UnitId=parksAndRec.Id
        PersonId=Some(swanson.Id)
        Unit=parksAndRec
        Person=Some(swanson)
        Title="Director"
        Role=Role.Leader
        Permissions=Permissions.Owner
        Percentage=100
        Tools=Tools.SuperPass
    }

    let knopeMembership = {
        Id=2
        UnitId=parksAndRec.Id
        PersonId=Some(knope.Id)
        Role=Role.Sublead
        Permissions=Permissions.Viewer
        Title="Deputy Director"
        Tools=Tools.None
        Percentage=100
        Person=Some(knope)
        Unit=parksAndRec
    }

    let parksAndRecVacancy = {
        Id=3
        UnitId=parksAndRec.Id
        PersonId=None
        Role=Role.Member
        Permissions=Permissions.Viewer
        Title="Assistant to the Manager"
        Tools=Tools.None
        Percentage=100
        Person=None
        Unit=parksAndRec
    }

    let wyattMembership:UnitMember = {
        Id=4
        UnitId=cityOfPawnee.Id
        PersonId=Some(wyatt.Id)
        Unit=cityOfPawnee
        Person=Some(wyatt)
        Title="Auditor"
        Role=Role.Leader
        Permissions=Permissions.Owner
        Percentage=100
        Tools=Tools.SuperPass
    }

    let supportRelationshipRequest:SupportRelationshipRequest = {
        UnitId=cityOfPawnee.Id
        DepartmentId=parksDept.Id
    }

    let supportRelationship:SupportRelationship = {
        Id=1
        UnitId=cityOfPawnee.Id
        DepartmentId=parksDept.Id
        Unit=cityOfPawnee
        Department=parksDept
    }

    /// A canned data implementation of IDatabaseRespository (for testing)

    let FakePeople = {
        TryGetId = fun netId -> stub (swanson.NetId, Some(swanson.Id))
        GetAll = fun query -> stub ([ swanson ] |> List.toSeq)
        Get = fun id -> stub swanson
        GetMemberships = fun personId -> stub ([ swansonMembership ] |> List.toSeq)
    }

    let FakeUnits = {
        GetAll = fun query -> stub ([ parksAndRec ] |> List.toSeq)
        Get = fun id -> stub parksAndRec
        GetMembers = fun id -> stub ([ swansonMembership ] |> List.toSeq) 
        GetChildren = fun id -> stub ([ fourthFloor ] |> List.toSeq) 
        GetSupportedDepartments = fun id -> stub ([ supportRelationship ] |> List.toSeq) 
        Create = fun unit -> stub parksAndRec
        Update = fun id unit -> stub parksAndRec
        Delete = fun id -> stub ()
    }

    let FakeDepartments = {
        GetAll = fun query -> stub ([ parksDept ] |> List.toSeq)
        Get = fun id -> stub parksDept
        GetMemberUnits = fun id -> stub ([ parksAndRec ] |> List.toSeq)
        GetSupportingUnits = fun id -> stub ([ supportRelationship ] |> List.toSeq)
    }

    let FakeMembershipRepository : MembershipRepository = {
        Get = fun id -> stub swansonMembership 
        GetAll = fun () -> stub ([ swansonMembership ] |> List.toSeq) 
        Create = fun membership -> stub membership
        Update = fun id membership -> stub membership
        Delete = fun id -> stub ()
    }

    let FakeSupportRelationships : SupportRelationshipRepository = {
        GetAll = fun () -> stub ([ supportRelationship ] |> List.toSeq) 
        Get = fun id -> stub supportRelationship
        Create = fun supportRelationship -> stub supportRelationship
        Update = fun id supportRelationship -> stub supportRelationship
        Delete = fun id -> stub ()
    }

    let FakesRepository = {
        People = FakePeople
        Units = FakeUnits
        Departments = FakeDepartments
        Memberships = FakeMembershipRepository
        SupportRelationships = FakeSupportRelationships
    }

    type ApiEndpointExample<'T>(example:'T) = 
        let ex = example;
        interface IExamplesProvider with
            member this.GetExamples () = ex :> obj
        interface IExamplesProvider<'T> with
            member this.GetExamples () = ex

    type JwtResponseExample () = inherit ApiEndpointExample<JwtResponse>(accessToken)
    type UnitsExample() = inherit ApiEndpointExample<seq<Unit>>([parksAndRec])
    type UnitExample() = inherit ApiEndpointExample<Unit>(parksAndRec)
    type DepartmentsExample() = inherit ApiEndpointExample<seq<Department>>([parksDept])
    type DepartmentExample() = inherit ApiEndpointExample<Department>(parksDept)
    type PeopleExample() = inherit ApiEndpointExample<seq<Person>>([swanson; knope; wyatt])
    type PersonExample() = inherit ApiEndpointExample<Person>(knope)
    type MembershipRequestExample() = inherit ApiEndpointExample<UnitMemberRequest>(knopeMembershipRequest)
    type MembershipResponseExample() = inherit ApiEndpointExample<UnitMember>(swansonMembership)
    type SupportRelationshipRequestExample() = inherit ApiEndpointExample<SupportRelationshipRequest>(supportRelationshipRequest)
    type SupportRelationshipResponseExample() = inherit ApiEndpointExample<SupportRelationship>(supportRelationship)
    type SupportRelationshipsResponseExample() = inherit ApiEndpointExample<seq<SupportRelationship>>([supportRelationship])