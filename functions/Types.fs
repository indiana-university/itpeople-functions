// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open System
open System.Net
open Chessie.ErrorHandling
open Dapper
open Serilog.Core
open System.ComponentModel.DataAnnotations
open Dapper
open Microsoft.Build.Framework

module Types = 

    let ROLE_ADMIN = "admin"
    let ROLE_USER = "user"

    let WorkflowTimestamp = "WORKFLOW_TIMESTAMP"
    let WorkflowUser = "WORKFLOW_USER"

    type Status = HttpStatusCode
    type Message = string
    type Error = Status * Message
    type ErrorModel = 
      { errors: array<string> }

    type UserPermissions = GET | POST | PUT | DELETE

    type AppConfig = 
      { OAuth2ClientId: string
        OAuth2ClientSecret: string
        OAuth2TokenUrl: string
        OAuth2RedirectUrl: string
        JwtSecret: string
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

    type Permissions =
        /// This person has read/write permissions on this entity
        | Owner=1
        /// This person has read-only permissions on this entity
        | Viewer=2

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
    type Name = string
    type NetId = string
    type Query = string

    [<Flags>]
    type Tools =
        | None          = 0b000000000
        | ItProWeb      = 0b000000001
        | ItProMail     = 0b000000010
        | IUware        = 0b000000100
        | MAS           = 0b000001000
        | ProductKeys   = 0b000010000
        | AccountMgt    = 0b000100000
        | AMSAdmin      = 0b001000000
        | UIPOUnblocker = 0b010000000
        | SuperPass     = 0b100000000

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

    /// A person doing or supporting IT work
    [<CLIMutable>]
    [<Table("people")>]
    type Person = 
      { /// The unique ID of this person record.
        [<Key>][<Column("id")>] Id: Id
        [<Column("hash")>] Hash: string
        /// The net id (username) of this person.
        [<Column("netid")>] NetId: NetId
        /// The preferred name of this person.
        [<Column("name")>] Name: Name
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
        /// A collection of IT-related tools accessible by this person.
        [<Column("tools")>] Tools: Tools
        /// The HR department to which this person belongs.
        [<Column("department_id")>] DepartmentId: Id
        /// The department in this relationship.
        [<ReadOnly(true)>] Department: Department }

    /// An IT unit
    [<CLIMutable>]
    [<Table("units")>]
    type Unit = 
      { /// The unique ID of this unit record.
        [<Key>][<Column("id")>] Id: Id
        /// The name of this unit.
        [<Required>][<Column("name")>] Name: Name
        /// A description of this unit.
        [<Column("description")>] Description: Name
        /// A URL for the website of this unit.
        [<Column("url")>] Url: string
        /// The unique ID of the parent unit of this unit.
        [<Column("parent_id")>][<Editable(true)>] ParentId: Id option 
          /// The parent unit of this unit
        [<ReadOnly(true)>] Parent: Unit option }

    [<CLIMutable>]
    type UnitRequest = 
      { /// (Required) The unique name of this unit.
        [<Required>]Name: Name
        /// A description of this unit.
        Description: Name
        /// A URL for the website of this unit.
        Url: string
        /// The ID of this unit's parent unit.
        ParentId: Id option }

    [<CLIMutable>]
    /// This relationship describes which IT Unit provides IT-related support for a given department.
    [<Table("support_relationships")>]
    type SupportRelationship = 
      { /// The unique ID of this unit record.
        [<Key>][<Column("id")>] Id: Id
        /// The ID of the unit in this relationship
        [<Required>][<Column("unit_id")>] UnitId: Id
        /// The ID of the department in this relationship
        [<Required>][<Column("department_id")>] DepartmentId: Id 
        /// The department in this relationship.
        [<ReadOnly(true)>] Department: Department
        /// The unit in this relationship.
        [<ReadOnly(true)>] Unit: Unit
      }

    [<CLIMutable>]
    type SupportRelationshipRequest = 
      { /// (Required) The ID of the unit in this relationship
        [<Required>]UnitId: Id
        /// (Required) The ID of the department in this relationship
        [<Required>]DepartmentId: Id 
      }

    [<CLIMutable>]
    [<Table("unit_members")>]
    type UnitMember = 
      { /// The unique ID of this unit member record.
        [<Key>][<Column("id")>] Id: Id
        /// The ID of the unit record.
        [<Required>][<Column("unit_id")>] UnitId: Id
        /// The role of the person in this membership as part of the unit.
        [<Required>][<Column("role")>] Role: Role
        /// The permissions of the person in this membership as part of the unit.
        [<Required>][<Column("permissions")>] Permissions: Permissions
        /// The ID of the person record. This can be null if the position is vacant.
        [<Column("person_id")>][<Editable(true)>] PersonId: Id option
        /// The title/position of this membership.
        [<Column("title")>] Title: string
        /// The percentage of time allocated to this position by this person (in case of split appointments).
        [<Column("percentage")>] Percentage: int
        /// The tools that can be used by the person in this position as part of this unit.
        [<Column("tools")>] Tools: Tools
        /// The person related to this membership.
        [<ReadOnly(true)>][<Column("person")>] Person: Person option
        /// The unit related to this membership.
        [<ReadOnly(true)>][<Column("unit")>] Unit: Unit }

    [<CLIMutable>]
    type UnitMemberRequest = 
      { /// (Required) The unique ID of the unit record.
        [<Required>]UnitId: int
        /// (Required) The role of the person in this membership as part of the unit.
        [<Required>]Role: Role
        /// (Required) The permissions of the person in this membership as part of the unit.
        [<Required>]Permissions: Permissions
        /// The ID of the person record. This can be null if the position is vacant.
        PersonId: int option
        /// The title/position of this membership.
        Title: string
        /// The percentage of time allocated to this position by this person (in case of split appointments).
        Percentage: int
        /// The tools that can be used by the person in this position as part of this unit.
        Tools: Tools }

    // DOMAIN MODELS

    type MessageResult = {
        Message: string
    }
    
    type FetchById<'T> = Id -> AsyncResult<'T,Error>
    type FetchAll<'T> = unit -> AsyncResult<'T,Error>

    type NoContent = unit

    type PeopleRepository = {
        /// Get a user record for a given net ID (e.g. 'jhoerr')
        TryGetId: NetId -> Async<Result<NetId * Id option,Error>>
        /// Get a list of all people
        GetAll: Query option -> Async<Result<Person seq,Error>>
        /// Get a single person by ID
        Get: PersonId -> Async<Result<Person,Error>>
        /// Get a list of a person's unit memberships
        GetMemberships: PersonId -> Async<Result<UnitMember seq,Error>>
    }

    type UnitRepository = {
        /// Get a list of all units
        GetAll: Query option -> Async<Result<Unit seq,Error>>
        /// Get a single unit by ID
        Get: Id -> Async<Result<Unit,Error>>
        /// Get a unit's members by unit ID        
        GetMembers: Unit -> Async<Result<UnitMember seq,Error>>
        /// Get a unit's supported departments by unit ID        
        GetSupportedDepartments: Unit -> Async<Result<SupportRelationship seq,Error>>
        // Get a unit's child units by parent unit Id
        GetChildren: Unit -> Async<Result<Unit seq,Error>>
        /// Create a unit
        Create: Unit -> Async<Result<Unit,Error>>
        /// Update a unit
        Update: Unit -> Async<Result<Unit,Error>>
        /// Delete a unit
        Delete: Unit -> Async<Result<unit,Error>>
    }

    type DepartmentRepository = {
        /// Get a list of all departments
        GetAll: Query option -> Async<Result<Department seq,Error>>
        /// Get a single department by ID
        Get: DepartmentId -> Async<Result<Department,Error>>
        /// Get a list of a department's member units
        GetMemberUnits: DepartmentId -> Async<Result<Unit seq,Error>>
        /// Get a list of a department's supporting units        
        GetSupportingUnits: DepartmentId -> Async<Result<SupportRelationship seq,Error>>
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
        Delete: UnitMember -> Async<Result<unit,Error>>
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
        Delete : SupportRelationship -> Async<Result<unit,Error>>
    }

    type DataRepository = {
        People: PeopleRepository
        Units: UnitRepository
        Departments: DepartmentRepository
        Memberships: MembershipRepository
        SupportRelationships: SupportRelationshipRepository
    }
    
    let stub a = async { return! a |> ok |> async.Return }

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

    let identity model : Id =
        match model with
        | Person p -> p.Id
        | Unit u -> u.Id
        | Department d -> d.Id
        | UnitMember um -> um.Id
        | SupportRelationship sr -> sr.Id

