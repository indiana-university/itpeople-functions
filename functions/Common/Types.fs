namespace Functions.Common

open System
open System.Net
open Chessie.ErrorHandling
open Dapper
open Newtonsoft.Json
open Dapper

module Types = 

    let ROLE_ADMIN = "admin"
    let ROLE_USER = "user"

    type Status = HttpStatusCode
    type Message = string
    type Error = Status * Message
    type ErrorModel = {
        errors: array<string>
    }

    type AppConfig = {
        OAuth2ClientId: string
        OAuth2ClientSecret: string
        OAuth2TokenUrl: string
        OAuth2RedirectUrl: string
        JwtSecret: string
        DbConnectionString: string
        UseFakes: bool
        CorsHosts: string
    }

    type Role =
        | Related=1
        | Member=2
        | Sublead=3
        | Leader=4


    type Id = int
    type Name = string
    type NetId = string

    [<CLIMutable>]
    type Entity = {
        Id: Id
        Name: Name
        Description: Name
    }

    [<CLIMutable>]
    type EntityRole = {
        Id: Id
        Name: Name
        Role: Role
    }

    [<Flags>]
    type Tools =
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
    type Person = {
        [<Column("id")>] Id: Id
        [<Column("hash")>] Hash: string
        [<Column("netId")>] NetId: NetId
        [<Column("name")>] Name: Name
        [<Column("position")>] Position: string
        [<Column("location")>] Location: string
        [<Column("campusPhone")>] CampusPhone: string
        [<Column("campusEmail")>] CampusEmail: string
        [<Column("campus")>] Campus: string
        [<Column("expertise")>] Expertise: string
        [<Column("notes")>] Notes: string
        [<Column("responsibilities")>] Responsibilities: Responsibilities
        [<Column("tools")>] Tools: Tools
        [<Column("photoUrl")>] PhotoUrl: string
        [<Column("hrDepartmentId")>] HrDepartmentId: Id
    }

    [<CLIMutable>]
    [<Table("departments")>]
    type Department = {
        [<Column("id")>] Id: Id
        [<Column("name")>] Name: Name
        [<Column("description")>] Description: Name
        [<Column("displayUnits")>] DisplayUnits: Boolean
    }

    [<CLIMutable>]
    [<Table("units")>]
    type Unit = {
        [<Column("id")>] Id: Id
        [<Column("parentId")>] ParentId: Id
        [<Column("name")>] Name: Name
        [<Column("description")>] Description: Name
        [<Column("url")>] Url: string
    }

    [<CLIMutable>]
    [<Table("supportedDepartments")>]
    type SupportedDepartment = {
        [<Column("departmentId")>] DepartmentId: Id
        [<Column("unitId")>] UnitId: Id
    }

    [<CLIMutable>]
    [<Table("unitMembers")>]
    type UnitMember = {
        [<Column("unitId")>] UnitId: Id
        [<Column("personId")>] PersonId: Id
        [<Column("title")>] Title: string
        [<Column("role")>] Role: Role
        [<Column("percentage")>] Percentage: int
        [<Column("tools")>] Tools: Tools
    }

    // DOMAIN MODELS
    type Member = Entity
    type UnitMembership = {
        Id: Id
        Name: Name
        Description: string
        PhotoUrl: string option
        Percentage: int
        Title: string
        Role: Role
        Tools: seq<Tools>
    }
    
    type PersonDto = {
        Id: Id
        NetId: NetId
        Name: Name
        Position: string
        Location: string
        CampusPhone: string
        CampusEmail: string
        Campus: string
        Expertise: seq<string>
        Notes: string
        PhotoUrl: string option
        Responsibilities: seq<Responsibilities>
        Tools: seq<Tools>
        Department: Department
        UnitMemberships: seq<UnitMembership>
    }

    type UnitDto = {
        Id: Id
        Name: Name
        Description: string
        Url: string option
        Members: seq<UnitMembership> option
        SupportedDepartments: seq<Department> option
        Children: seq<Unit> option
        Parent: Unit option
    }

    type DepartmentDto = {
        Id: Id
        Name: Name
        Description: string
        SupportingUnits: seq<Unit>
        Units: seq<Unit>
        Members: seq<Member>
    }

    type SimpleSearch = {
        Users: seq<Entity>
        Departments: seq<Entity>
        Units: seq<Entity>
    }

    type FetchById<'T> = Id -> AsyncResult<'T,Error>
    type FetchAll<'T> = unit -> AsyncResult<'T,Error>

    type IDataRepository =
        /// Get a user record for a given net ID (e.g. 'jhoerr')
        abstract member GetUserByNetId: NetId -> AsyncResult<Person,Error>
        /// Get a user profile for a given user ID
        abstract member GetProfile: Id -> AsyncResult<PersonDto,Error>
        /// Get all users, units, and departments matching a given search term
        abstract member GetSimpleSearchByTerm: string -> AsyncResult<SimpleSearch,Error>
        /// Get a list of all units
        abstract member GetUnits: unit -> AsyncResult<Unit seq,Error>
        /// Get a single unit by ID
        abstract member GetUnit: Id -> AsyncResult<UnitDto,Error>
        /// Get a list of all departments
        abstract member GetDepartments: unit -> AsyncResult<Department seq,Error>
        /// Get a single department by ID
        abstract member GetDepartment: Id -> AsyncResult<DepartmentDto,Error>

    type JwtClaims = {
        UserId: Id
        UserName: NetId
        Expiration: System.DateTime
    }
