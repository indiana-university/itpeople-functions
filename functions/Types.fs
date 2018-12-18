// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open System
open System.Net
open Chessie.ErrorHandling
open Dapper
open Serilog.Core

module Types = 

    let ROLE_ADMIN = "admin"
    let ROLE_USER = "user"

    type Status = HttpStatusCode
    type Message = string
    type Error = Status * Message
    type ErrorModel = 
      { errors: array<string> }

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
        | Related=1
        | Member=2
        | Sublead=3
        | Leader=4

    type Id = int
    type Name = string
    type NetId = string

    [<CLIMutable>]
    type Entity = 
      { Id: Id
        Name: Name
        Description: Name }

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
    

    [<CLIMutable>]
    [<Table("people")>]
    type Person = 
      { [<Key>][<Column("id")>] Id: Id
        [<Column("hash")>] Hash: string
        [<Column("netid")>] NetId: NetId
        [<Column("name")>] Name: Name
        [<Column("position")>] Position: string
        [<Column("location")>] Location: string
        [<Column("campus")>] Campus: string
        [<Column("campus_phone")>] CampusPhone: string
        [<Column("campus_email")>] CampusEmail: string
        [<Column("expertise")>] Expertise: string
        [<Column("notes")>] Notes: string
        [<Column("photo_url")>] PhotoUrl: string
        [<Column("responsibilities")>] Responsibilities: Responsibilities
        [<Column("tools")>] Tools: Tools
        [<Column("department_id")>] HrDepartmentId: Id }

    [<CLIMutable>]
    [<Table("departments")>]
    type Department = 
      { [<Key>][<Column("id")>] Id: Id
        [<Column("name")>] Name: Name
        [<Column("description")>] Description: Name
        [<Column("display_units")>] DisplayUnits: bool }

    [<CLIMutable>]
    [<Table("units")>]
    type Unit = 
      { [<Key>][<Column("id")>] Id: Id
        [<Column("name")>] Name: Name
        [<Column("description")>] Description: Name
        [<Column("url")>] Url: string }

    [<CLIMutable>]
    [<Table("unit_relations")>]
    type UnitRelation = 
      { [<Key>][<Required>][<Column("child_id")>] ChildUnitId: Id
        [<Key>][<Required>][<Column("parent_id")>] ParentUnitId: Id }

    [<CLIMutable>]
    [<Table("supported_departments")>]
    type SupportedDepartment = 
      { [<Key>][<Required>][<Column("unit_id")>] UnitId: Id
        [<Key>][<Required>][<Column("department_id")>] DepartmentId: Id }

    [<CLIMutable>]
    [<Table("unit_members")>]
    type UnitMember = 
      { [<Key>][<Required>][<Column("unit_id")>] UnitId: int
        [<Key>][<Required>][<Column("person_id")>] PersonId: int
        [<Column("title")>] Title: string
        [<Column("role")>] Role: Role
        [<Column("percentage")>] Percentage: int
        [<Column("tools")>] Tools: Tools
        [<ReadOnly(true)>][<Column("name")>] Name: string
        [<ReadOnly(true)>][<Column("photo_url")>] PhotoUrl: string
        [<ReadOnly(true)>][<Column("description")>] Description: string }

    // DOMAIN MODELS
    type Member = Entity
    type UnitMembership = 
      { Id: Id
        Name: Name
        Description: string
        PhotoUrl: string
        Percentage: int
        Title: string
        Role: Role
        Tools: seq<Tools> }
    
    type PersonDto = 
      { Id: Id
        NetId: NetId
        Name: Name
        Position: string
        Location: string
        CampusPhone: string
        CampusEmail: string
        Campus: string
        Expertise: seq<string>
        Notes: string
        PhotoUrl: string
        Responsibilities: seq<Responsibilities>
        Tools: seq<Tools>
        Department: Department
        UnitMemberships: seq<UnitMembership> }

    type UnitDto = 
      { Id: Id
        Name: Name
        Description: string
        Url: string
        Members: seq<UnitMembership>
        SupportedDepartments: seq<Department>
        Children: seq<Unit>
        Parent: Unit option }

    type DepartmentDto = 
      { Id: Id
        Name: Name
        Description: string
        SupportingUnits: seq<Unit>
        Units: seq<Unit>
        Members: seq<Member> }

    type SimpleSearch = 
      { Users: seq<Entity>
        Departments: seq<Entity>
        Units: seq<Entity> }
    
    type MessageResult = {
        Message: string
    }
    
    type FetchById<'T> = Id -> AsyncResult<'T,Error>
    type FetchAll<'T> = unit -> AsyncResult<'T,Error>

    type IDataRepository =
        /// Get a user record for a given net ID (e.g. 'jhoerr')
        abstract member TryGetPersonId: NetId -> Async<Result<(NetId*Id),Error>>
        /// Get a user profile for a given user ID
        abstract member GetProfile: Id -> Async<Result<PersonDto,Error>>
        /// Get all users, units, and departments matching a given search term
        abstract member GetSimpleSearchByTerm: string -> Async<Result<SimpleSearch,Error>>
        /// Get a list of all units
        abstract member GetUnits: unit -> Async<Result<Unit seq,Error>>
        /// Get a single unit by ID
        abstract member GetUnit: Id -> Async<Result<UnitDto,Error>>
        /// Get a list of all departments
        abstract member GetDepartments: unit -> Async<Result<Department seq,Error>>
        /// Get a single department by ID
        abstract member GetDepartment: Id -> Async<Result<DepartmentDto,Error>>

    type UaaResponse = {
        access_token: string
    }

    type JwtClaims = 
      { UserId: Id
        UserName: NetId
        Expiration: System.DateTime }
