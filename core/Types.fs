﻿module Core.Types

open System
open System.ComponentModel
open System.Net
open Dapper
open Newtonsoft.Json

let ok x = x |> Ok |> async.Return
let error(status, msg) = Error(status, msg) |> async.Return  

let WorkflowTimestamp = "WORKFLOW_TIMESTAMP"
let WorkflowUser = "WORKFLOW_USER"
let WorkflowPermissions = "WORKFLOW_PERMISSIONS"

type Status = HttpStatusCode
type Message = string
type Error = Status * Message

type PipelineBuilder() =
    member __.Bind  (a : Async<Result<'a, Error>>, f : 'a -> Async<Result<'b, Error>>) = async {
      let! result = a
      match result with
      | Ok value -> return! f value
      | Error err -> return (Error err)
    }
    member __.Return(value) : Async<Result<'b, Error>> = ok value
    member __.ReturnFrom(value) = value
    member __.Zero() = ()

let pipeline = PipelineBuilder()

type ErrorModel = 
  { errors: array<string> }

type UserPermissions = GET | POST | PUT | DELETE

type AppConfig = 
  { OAuth2ClientId: string
    OAuth2ClientSecret: string
    OAuth2TokenUrl: string
    OAuth2RedirectUrl: string
    DbConnectionString: string
    UseFakes: bool
    CorsHosts: string }

type Role =
    /// This person has an ancillary relationship to this unit. This can apply to administrative assistants or self-supporting faculty.
    | Related=1
    /// This person is a regular member of this unit.
    | Member=2
    /// This person has some delegated authority within this unit. 
    | Sublead=3
    /// This person has primary responsibility for and authority over this unit. 
    | Leader=4

type UnitPermissions =
    /// This person has read/write permissions on this entity
    | Owner=1
    /// This person has read-only permissions on this entity
    | Viewer=2
    /// This person can modify unit membership/composition
    | ManageMembers=3
    /// This person can modify unit tools
    | ManageTools=4

type Area =
    /// University Information Technology Services (UITS) IT units are part of IU's central tech and support organization.
    | Uits=1
    /// Edge IT units are integrated directly into colleges and academic departments.
    | Edge=2

/// The unique ID of a record
type Id = int
/// The unique ID of a Person record
type PersonId = int
/// The unique ID of a Unit Membership record
type MembershipId = int
/// The unique ID of a Unit record
type UnitId = int
/// The unique ID of a Department record
type DepartmentId = int
/// The unique ID of a Building record
type BuildingId = int
type Name = string
type NetId = string
type Filter = string

[<Flags>]
type Responsibilities =
    | None                  = 0b00000000000000000
    | ItLeadership          = 0b00000000000000001
    | BizSysAnalysis        = 0b00000000000000010
    | DataAdminAnalysis     = 0b00000000000000100
    | DatabaseArchDesign    = 0b00000000000001000
    | InstructionalTech     = 0b00000000000010000
    | ItProjectMgt          = 0b00000000000100000
    | ItSecurityPrivacy     = 0b00000000001000000
    | ItUserSupport         = 0b00000000010000000
    | ItMultiDiscipline     = 0b00000000100000000
    | Networks              = 0b00000001000000000
    | SoftwareAdminAnalysis = 0b00000010000000000
    | SoftwareDevEng        = 0b00000100000000000
    | SystemDevEng          = 0b00001000000000000
    | UserExperience        = 0b00010000000000000
    | WebAdminDevEng        = 0b00100000000000000

/// An academic or administrative department
[<CLIMutable>]
[<Table("departments")>]
type Department = 
  { /// The unique ID of this department record.
    [<Key>][<Column("id")>] Id: Id
    /// The name of this department.
    [<Column("name")>] Name: Name
    /// A description or longer name of this department.
    [<Column("description")>] Description: Name }

/// A university building or location
[<CLIMutable>]
[<Table("buildings")>]
type Building =
  { /// The unique ID of this department record.
    [<Key>][<Column("id")>] Id: Id
    /// The name of this department.
    [<Column("name")>] Name: Name
    /// A description or longer name of this department.
    [<Column("code")>] Code: Name
    [<Column("address")>] Address: string
    [<Column("city")>] City: string
    [<Column("state")>] State: string
    [<Column("country")>] Country: string
    [<Column("post_code")>] PostCode: string }

/// A person doing or supporting IT work
[<CLIMutable>]
[<Table("hr_people")>]
type HrPerson = 
  { /// The unique ID of this person record.
    [<Key>][<Column("id")>] Id: Id
    /// The net id (username) of this person.
    [<Column("netid")>] NetId: NetId
    /// The preferred name of this person.
    [<Column("name")>] Name: Name
    /// The preferred first name of this person.
    [<Column("name_first")>] NameFirst: string
    /// The preferred last name of this person.
    [<Column("name_last")>] NameLast: string
    /// The job position of this person as defined by HR. This may be different than the person's title in relation to an IT unit.
    [<Column("position")>] Position: string
    /// The primary campus with which this person is affiliated.
    [<Column("campus")>] Campus: string
    /// The campus phone number of this person.
    [<Column("campus_phone")>] CampusPhone: string
    /// The campus (work) email address of this person.
    [<Column("campus_email")>] CampusEmail: string 
    /// The short name of the person's HR department (e.g. UA-VPIT).
    [<Column("hr_department")>] HrDepartment: string
    /// The long name / description of the person's HR department.
    [<Column("hr_department_desc")>] HrDepartmentDescription: string }

/// A person doing or supporting IT work
[<CLIMutable>]
[<Table("people")>]
type Person = 
  { /// The unique ID of this person record.
    [<Key>][<Column("id")>] Id: Id
    /// The net id (username) of this person.
    [<Column("netid")>] NetId: NetId
    /// The preferred name of this person.
    [<Column("name")>] Name: Name
    /// The preferred first name of this person.
    [<Column("name_first")>] NameFirst: string
    /// The preferred last name of this person.
    [<Column("name_last")>] NameLast: string
    /// The job position of this person as defined by HR. This may be different than the person's title in relation to an IT unit.
    [<Column("position")>] Position: string
    /// The physical location (building, room) of this person.
    [<Column("location")>] Location: string
    /// The primary campus with which this person is affiliated.
    [<Column("campus")>] Campus: string
    /// The campus phone number of this person.
    [<Column("campus_phone")>] CampusPhone: string
    /// The campus (work) email address of this person.
    [<Column("campus_email")>] CampusEmail: string
    /// A collection of IT-related skills, expertise, or interests posessed by this person.
    [<Column("expertise")>] Expertise: string
    /// Administrative notes about this person, visible only to IT Admins.
    [<Column("notes")>] Notes: string
    /// A URL for a photograph (headshot) of this person.
    [<Column("photo_url")>] PhotoUrl: string
    /// A collection of IT-related responsibilites of this person.
    [<Column("responsibilities")>] Responsibilities: Responsibilities
    /// The HR department to which this person belongs.
    [<Column("department_id")>] DepartmentId: Id
    /// Whether this person is an administrator of the IT People service.
    [<Column("is_service_admin")>] IsServiceAdmin: bool
    /// The department in this relationship.
    [<ReadOnly(true)>] Department: Department }

[<CLIMutable>]
type PersonRequest =
  { /// The unique ID of this person record.
    [<JsonIgnore>]
    Id: Id
    /// The physical location (building, room) of this person.
    [<DefaultValue("")>]
    Location: string
    /// A collection of IT-related skills, expertise, or interests posessed by this person.
    [<DefaultValue("")>]
    Expertise: string
    /// A URL for a profile photo or headshot.
    [<DefaultValue("")>]
    PhotoUrl: string
    /// A collection of IT-related responsibilites of this person.
    [<DefaultValue("None")>]
    Responsibilities: Responsibilities }

/// An IT unit
[<CLIMutable>]
[<Table("units")>]
type Unit = 
  { /// The unique ID of this unit record.
    [<Key>][<Column("id")>] Id: Id
    /// The name of this unit.
    [<JsonProperty(Required = Required.Always)>]
    [<Column("name")>] Name: Name
    /// A description of this unit.
    [<DefaultValue("")>]
    [<Column("description")>] Description: Name
    /// A URL for the website of this unit.
    [<DefaultValue("")>]
    [<Column("url")>] Url: string
    /// A contact email for this unit.
    [<DefaultValue("")>]
    [<Column("email")>] Email: string
    /// The unique ID of the parent unit of this unit.
    [<DefaultValue(null)>]
    [<Column("parent_id")>][<Editable(true)>] ParentId: Id option 
      /// The parent unit of this unit
    [<ReadOnly(true)>] Parent: Unit option }

[<CLIMutable>]
type UnitRequest = 
  { /// The unique name of this unit.
    [<JsonProperty(Required = Required.Always)>]
    Name: Name
    /// A description of this unit.
    [<DefaultValue("")>]
    Description: Name
    /// A URL for the website of this unit.
    [<DefaultValue("")>]
    Url: string
    /// The ID of this unit's parent unit.
    [<DefaultValue(null)>]
    ParentId: Id option }

[<CLIMutable>]
/// This relationship describes which IT Unit provides IT-related support for a given department.
[<Table("support_relationships")>]
type SupportRelationship = 
  { /// The unique ID of this unit record.
    [<Key>][<Column("id")>] Id: Id
    /// The ID of the unit in this relationship
    [<JsonProperty(Required = Required.Always)>]
    [<Column("unit_id")>] UnitId: Id
    /// The ID of the department in this relationship
    [<JsonProperty(Required = Required.Always)>]
    [<Column("department_id")>] DepartmentId: Id 
    /// The department in this relationship.
    [<ReadOnly(true)>] Department: Department
    /// The unit in this relationship.
    [<ReadOnly(true)>] Unit: Unit
  }

[<CLIMutable>]
type SupportRelationshipRequest = 
  { /// The ID of the unit in this relationship
    [<JsonProperty(Required = Required.Always)>]
    UnitId: Id
    /// The ID of the department in this relationship
    [<JsonProperty(Required = Required.Always)>]
    DepartmentId: Id }

[<CLIMutable>]
/// This relationship describes which IT Unit provides IT-related support for a given building.
[<Table("building_relationships")>]
type BuildingRelationship = 
  { /// The unique ID of this unit record.
    [<Key>][<Column("id")>] Id: Id
    /// The ID of the unit in this relationship
    [<JsonProperty(Required = Required.Always)>]
    [<Column("unit_id")>] UnitId: Id
    /// The ID of the department in this relationship
    [<JsonProperty(Required = Required.Always)>]
    [<Column("building_id")>] BuildingId: Id 
    /// The unit in this relationship.
    [<ReadOnly(true)>] Unit: Unit
    /// The building in this relationship.
    [<ReadOnly(true)>] Building: Building
  }

type BuildingRelationshipRequest = 
  { /// The ID of the unit in this relationship
    [<JsonProperty(Required = Required.Always)>]
    UnitId: Id
    /// The ID of the building in this relationship
    [<JsonProperty(Required = Required.Always)>]
    BuildingId: Id }

[<CLIMutable>]
[<Table("tools")>]
type Tool =
  { /// The unique ID of this tool record.
    [<Key>][<Column("id")>] Id: Id
    /// The name of this tool.
    [<JsonProperty(Required = Required.Always)>]
    [<Column("name")>] Name: Name
    /// A description of this tool.
    [<DefaultValue("")>]
    [<Column("description")>] Description: Name
    /// A description of this tool.
    [<DefaultValue("")>]
    [<Column("ad_path")>] ADPath: string
    /// Whether this tool is scoped to a department via a unit-department support relationship.
    [<DefaultValue(false)>]
    [<Column("department_scoped")>] DepartmentScoped: bool }

[<CLIMutable>]
[<Table("unit_member_tools")>]
type MemberTool =
  { /// The unique ID of this member tool record.
    [<Key>][<Column("id")>] Id: Id
    /// The ID of the member in this relationship
    [<JsonProperty(Required = Required.Always)>]
    [<Column("membership_id")>] MembershipId: Id
    /// The ID of the tool in this relationship
    [<JsonProperty(Required = Required.Always)>]
    [<Column("tool_id")>] ToolId: Id }

[<CLIMutable>]
[<Table("unit_members")>]
type UnitMember = 
  { /// The unique ID of this unit member record.
    [<Key>][<Column("id")>] Id: Id
    /// The ID of the unit record.
    [<JsonProperty(Required = Required.Always)>]
    [<Column("unit_id")>] UnitId: UnitId
    /// The role of the person in this membership as part of the unit.
    [<JsonProperty(Required = Required.Always)>]
    [<Column("role")>] Role: Role
    /// The permissions of the person in this membership as part of the unit. Defaults to 'viewer'.
    [<DefaultValue(UnitPermissions.Viewer)>]
    [<Column("permissions")>] Permissions: UnitPermissions
    /// The ID of the person record. This can be null if the position is vacant.
    [<DefaultValue(null)>]
    [<Column("person_id")>][<Editable(true)>] PersonId: PersonId option
    /// The title/position of this membership.
    [<DefaultValue("")>]
    [<Column("title")>] Title: string
    /// The percentage of time allocated to this position by this person (in case of split appointments).
    [<DefaultValue(100)>]
    [<Column("percentage")>] Percentage: int
    /// Notes about this person (for admins/reporting eyes only.)
    [<DefaultValue("")>]
    [<Column("notes")>] Notes: string
    /// The netid of the person related to this membership.
    [<ReadOnly(true)>][<Column("netid")>] NetId: NetId option
    /// The person related to this membership.
    [<ReadOnly(true)>][<Column("person")>] Person: Person option
    /// The unit related to this membership.
    [<ReadOnly(true)>][<Column("unit")>] Unit: Unit
    /// The tools that can be used by the person in this position as part of this unit.
    [<ReadOnly(true)>][<Column("member_tools")>] MemberTools: seq<MemberTool> }

[<CLIMutable>]
[<Table("unit_members")>]
type UnitMemberRequest = 
  { /// The unique ID of the unit record.
    [<JsonProperty(Required = Required.Always)>]
    UnitId: UnitId
    /// The role of the person in this membership as part of the unit.
    [<JsonProperty(Required = Required.Always)>]
    Role: Role
    /// The permissions of the person in this membership as part of the unit. Defaults to 'viewer'.
    [<DefaultValue(UnitPermissions.Viewer)>]
    Permissions: UnitPermissions
    /// The ID of the person record. This can be null if the position is vacant.
    [<DefaultValue(null)>]
    PersonId: PersonId option
    /// The NetId of the person, if they are not already in the IT people directory. This can be null if the position is vacant.
    [<DefaultValue(null)>]
    NetId: NetId option
    /// The title/position of this membership.
    [<DefaultValue("")>]
    Title: string
    /// The percentage of time allocated to this position by this person (in case of split appointments).
    [<DefaultValue(100)>]
    Percentage: int 
    /// Ad-hoc notes about this person's relationship to the unit, to be used by unit managers.
    [<DefaultValue("")>]
    Notes: string }

[<CLIMutable>]
type ToolPermission = 
  { /// The netid of the grantee
    [<Column("netid")>] NetId: NetId
    /// The name of the tool to which permissions have been granted 
    [<Column("tool_name")>] ToolName: Name
    /// For department-scoped tools, the name of the department
    [<Column("department_name")>] DepartmentName: Name }

[<CLIMutable>]
type HistoricalPersonUnitMetadata =
  { [<Column("id")>] Id: Id
    [<Column("unit")>] Unit: string
    [<Column("unit_id")>] UnitId: int
    [<Column("hr_department")>] HrDepartment: string
    [<Column("role")>] Role: Role
    [<Column("permissions")>] Permissions: UnitPermissions
    [<Column("title")>] Title: string
    [<Column("notes")>] Notes: string }

[<CLIMutable>]
[<Table("historical_people")>]
type HistoricalPerson =
  { /// The netid of the removed person
    [<Column("netid")>] NetId: NetId
    /// A JSON blob with an array of HistoricalPersonUnitMetadata. 
    [<Column("metadata")>] Metadata: string
    /// The name of the tool to which permissions have been granted 
    [<Column("removed_on")>] RemovedOn: DateTime }

open System.Xml.Serialization

[<CLIMutable>]
[<Serializable>]
type LspInfo =
  { [<XmlElement("IsLA")>] IsLA: bool
    [<XmlElement("NetworkID")>] NetworkID: string }

[<CLIMutable>]
[<Serializable>]
[<XmlRoot("ArrayOfLspInfo")>]
type LspInfoArray = {
  [<XmlElement("LspInfo")>] 
  LspInfos: LspInfo []
}

[<CLIMutable>]
[<Serializable>]
type DeptCodeList =
  { [<XmlElement("a")>] Values: string[] }

[<CLIMutable>]
[<Serializable>]
[<XmlRoot("LspDepartment")>]
type LspDepartmentArray = {
  [<XmlElement("DeptCodeList")>] DeptCodeList: DeptCodeList
  [<XmlElement("NetworkID")>] NetworkID: string
}

[<CLIMutable>]
[<Serializable>]
type LspContact =
  { [<XmlElement("Email")>] Email: string 
    [<XmlElement("FullName")>] FullName: string 
    [<XmlElement("GroupInternalEmail")>] GroupInternalEmail: string 
    [<XmlElement("NetworkID")>] NetworkID: string 
    [<XmlElement("Phone")>] Phone: string 
    [<XmlElement("PreferredEmail")>] PreferredEmail: string 
    [<XmlElement("isLspAdmin")>] IsLSPAdmin: bool }

[<CLIMutable>]
[<Serializable>]
[<XmlRoot("ArrayOfLspContact")>]
type LspContactArray = {
  [<XmlElement("LspContact")>] 
  LspContacts: LspContact []
}



// DOMAIN MODELS

type MessageResult = {
    Message: string
}

type NoContent = unit

type PeopleQuery = 
  { Query: string
    Classes: int
    Interests: array<string>
    Roles: array<int>
    Permissions: array<int>
    Campuses: array<string>
    Area: int }

type PeopleRepository = {
    /// Get a user record for a given net ID (e.g. 'jhoerr')
    TryGetId: NetId -> Async<Result<NetId * Id option,Error>>
    /// Get a list of all people
    GetAll: PeopleQuery -> Async<Result<Person seq,Error>>
    /// Get a unioned list of IT and HR people, filtered by name/netid
    GetAllWithHr: NetId -> Async<Result<Person seq,Error>>
    /// Get a single HR person by NetId
    GetHr: NetId -> Async<Result<Person,Error>>
    /// Get a single person by ID
    GetById: Id -> Async<Result<Person,Error>>
    /// Get a single person by NetId
    GetByNetId: NetId -> Async<Result<Person,Error>>
    /// Create a person from canonical HR data
    Create: Person -> Async<Result<Person,Error>>
    /// Get a list of a person's unit memberships, by the person's ID
    GetMemberships: Id -> Async<Result<UnitMember seq,Error>>
    /// Update a person
    Update: PersonRequest -> Async<Result<Person,Error>>
}

type UnitMemberRecordFieldOptions = 
    | MembersWithoutNotes of Id
    | MembersWithNotes of Id

type UnitRepository = {
    /// Get a list of all units
    GetAll: Filter option -> Async<Result<Unit seq,Error>>
    /// Get a single unit by ID
    Get: Id -> Async<Result<Unit,Error>>
    /// Get a unit's members by unit ID 
    GetMembers: UnitMemberRecordFieldOptions -> Async<Result<UnitMember seq,Error>>
    /// Get a unit's supported departments by unit ID        
    GetSupportedDepartments: Id -> Async<Result<SupportRelationship seq,Error>>
    /// Get a unit's supported buildings by unit ID        
    GetSupportedBuildings: Id -> Async<Result<BuildingRelationship seq,Error>>
    // Get a unit's child units by parent unit Id
    GetChildren: Id -> Async<Result<Unit seq,Error>>
    /// Create a unit
    Create: Unit -> Async<Result<Unit,Error>>
    /// Update a unit
    Update: Unit -> Async<Result<Unit,Error>>
    /// Delete a unit
    Delete: Id -> Async<Result<unit,Error>>
    /// 
    GetDescendantOfParent: (Id * Id) -> Async<Result<Unit option,Error>>
}

type DepartmentRepository = {
    /// Get a list of all departments
    GetAll: Filter option -> Async<Result<Department seq,Error>>
    /// Get a single department by ID
    Get: DepartmentId -> Async<Result<Department,Error>>
    /// Get a list of a department's member units
    GetMemberUnits: Id -> Async<Result<Unit seq,Error>>
    /// Get a list of a department's supporting units        
    GetSupportingUnits: Id -> Async<Result<SupportRelationship seq,Error>>
}

type MembershipRepository = {
    /// Get a membership by ID        
    GetAll: unit -> Async<Result<UnitMember seq,Error>>
    /// Get a membership by ID        
    Get: Id -> Async<Result<UnitMember,Error>>
    /// Create a unit membership
    Create: UnitMember -> Async<Result<UnitMember,Error>>
    /// Update a unit membership
    Update: UnitMember -> Async<Result<UnitMember,Error>>
    /// Delete a unit membership
    Delete: Id -> Async<Result<unit,Error>>
}

type ToolsRepository = {
    /// Get all tools
    GetAll: unit -> Async<Result<Tool seq,Error>>
    /// Get all member tool permissions
    GetAllPermissions: unit -> Async<Result<ToolPermission seq,Error>>
    /// Get a tool by ID        
    Get: Id -> Async<Result<Tool,Error>>
}

type MemberToolsRepository = {
    /// Get all member tools
    GetAll: unit -> Async<Result<MemberTool seq,Error>>
    /// Get a member tool by ID        
    Get: Id -> Async<Result<MemberTool,Error>>
    /// Create a unit membership
    Create: MemberTool -> Async<Result<MemberTool,Error>>
    /// Update a unit membership
    Update: MemberTool -> Async<Result<MemberTool,Error>>
    /// Delete a unit membership
    Delete: Id -> Async<Result<unit,Error>>
}

type SupportRelationshipRepository = {
    /// Get a list of all support relationships
    GetAll: unit -> Async<Result<SupportRelationship seq,Error>>
    /// Get a single support relationsihps
    Get : Id -> Async<Result<SupportRelationship,Error>>
    /// Crate a support relationship
    Create: SupportRelationship -> Async<Result<SupportRelationship,Error>>
    /// Update a support relationship
    Update: SupportRelationship -> Async<Result<SupportRelationship,Error>>
    /// Delete a support relationsihps
    Delete : Id -> Async<Result<unit,Error>>
}

type BuildingRepository = {
    /// Get a list of all departments
    GetAll: Filter option -> Async<Result<Building seq,Error>>
    /// Get a single department by ID
    Get: Id -> Async<Result<Building,Error>>
    /// Get a list of a buildings's supporting units        
    GetSupportingUnits: Building -> Async<Result<BuildingRelationship seq,Error>>
}


type BuildingRelationshipRepository = {
    /// Get a list of all support relationships
    GetAll: unit -> Async<Result<BuildingRelationship seq,Error>>
    /// Get a single support relationsihps
    Get : Id -> Async<Result<BuildingRelationship,Error>>
    /// Crate a support relationship
    Create: BuildingRelationship -> Async<Result<BuildingRelationship,Error>>
    /// Update a support relationship
    Update: BuildingRelationship -> Async<Result<BuildingRelationship,Error>>
    /// Delete a support relationsihps
    Delete : Id -> Async<Result<unit,Error>>
}

type AuthorizationRepository = {
    /// Given an OAuth token_key URL and return the public key.
    UaaPublicKey: string -> Async<Result<string,Error>>
    IsServiceAdmin: NetId -> Async<Result<bool, Error>>
    IsUnitManager: Id -> NetId -> Async<Result<bool, Error>>
    IsUnitToolManager: Id -> NetId -> Async<Result<bool, Error>>
    CanModifyPerson: Id -> NetId -> Async<Result<bool,Error>>
}

type LegacyRepository = {
    GetLspList: unit -> Async<Result<LspInfoArray, Error>>
    GetLspDepartments: string -> Async<Result<LspDepartmentArray, Error>>
    GetDepartmentLsps: string -> Async<Result<LspContactArray, Error>>
}

type DataRepository = {
    People: PeopleRepository
    Units: UnitRepository
    Departments: DepartmentRepository
    Buildings: BuildingRepository
    Memberships: MembershipRepository
    MemberTools: MemberToolsRepository
    Tools: ToolsRepository
    SupportRelationships: SupportRelationshipRepository
    BuildingRelationships: BuildingRelationshipRepository
    Authorization: AuthorizationRepository
    Legacy: LegacyRepository
}

let stub a = a |> Ok |> async.Return

type JwtResponse = {
    /// The OAuth JSON Web Token (JWT) that represents the logged-in user. The JWT must be passed in an HTTP Authentication header in the form: 'Bearer <JWT>'
    access_token: string
}

type JwtClaims = 
  { UserId: Id
    UserName: NetId
    Expiration: System.DateTime }

type Model = 
    | Person of Person
    | Unit of Unit
    | Department of Department
    | UnitMember of UnitMember
    | SupportRelationship of SupportRelationship

let inline identity (model:^T) = 
    let id = (^T : (member Id:Id) model)
    id

let inline unitId (model:^T) = 
    let id = (^T : (member UnitId:UnitId) model)
    id

let inline departmentId (model:^T) = 
    let id = (^T : (member DepartmentId:DepartmentId) model)
    id

let inline buildingId (model:^T) = 
    let id = (^T : (member BuildingId:BuildingId) model)
    id
