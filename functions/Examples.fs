module Functions.Examples

open Core.Types
open Core.Fakes
open Swashbuckle.AspNetCore.Filters


type ApiEndpointExample<'T>(example:'T) = 
    let ex = example;
    interface IExamplesProvider with
        member this.GetExamples () = ex :> obj
    interface IExamplesProvider<'T> with
        member this.GetExamples () = ex

type JwtResponseExample () = inherit ApiEndpointExample<JwtResponse>(adminJwt)
type UnitsExample() = inherit ApiEndpointExample<seq<Unit>>([parksAndRec])
type UnitExample() = inherit ApiEndpointExample<Unit>(parksAndRec)
type UnitRequestExample() = inherit ApiEndpointExample<UnitRequest>(parksAndRecUnitRequest)
type DepartmentsExample() = inherit ApiEndpointExample<seq<Department>>([parksDept])
type DepartmentExample() = inherit ApiEndpointExample<Department>(parksDept)
type PeopleExample() = inherit ApiEndpointExample<seq<Person>>([knope; knope; wyatt])
type PersonExample() = inherit ApiEndpointExample<Person>(knope)
type PersonRequestExample() = inherit ApiEndpointExample<PersonRequest>(knopeRequest)
type MembershipRequestExample() = inherit ApiEndpointExample<UnitMemberRequest>(knopeMembershipRequest)
type MembershipExample() = inherit ApiEndpointExample<UnitMember>(knopeMembership)
type MembershipsExample() = inherit ApiEndpointExample<seq<UnitMember>>([swansonMembership; knopeMembership])
type MembertoolExample() = inherit ApiEndpointExample<MemberTool>(memberTool)
type MembertoolsExample() = inherit ApiEndpointExample<seq<MemberTool>>([memberTool])
type SupportRelationshipRequestExample() = inherit ApiEndpointExample<SupportRelationshipRequest>(supportRelationshipRequest)
type SupportRelationshipExample() = inherit ApiEndpointExample<SupportRelationship>(supportRelationship)
type SupportRelationshipsExample() = inherit ApiEndpointExample<seq<SupportRelationship>>([supportRelationship])
type ToolsExample() = inherit ApiEndpointExample<seq<Tool>>([tool])
type ToolExample() = inherit ApiEndpointExample<Tool>(tool)
type QueryExample() = inherit ApiEndpointExample<string>("term")
type ResponsibilityExample() = inherit ApiEndpointExample<seq<Responsibilities>>([Responsibilities.UserExperience; Responsibilities.SoftwareDevEng])
type UnitRoleExample() = inherit ApiEndpointExample<seq<Role>>([Role.Leader; Role.Member])
type UnitPermissionsExample() = inherit ApiEndpointExample<seq<UnitPermissions>>([UnitPermissions.Owner; UnitPermissions.ManageMembers])
type StringSeqExample() = inherit ApiEndpointExample<seq<string>>(["foo"; "bar"])
type ErrorExample() = inherit ApiEndpointExample<ErrorModel>({errors=[|"This message includes detailed error information."|]})