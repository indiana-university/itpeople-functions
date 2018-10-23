namespace MyFunctions.Common

open Types
open Chessie.ErrorHandling

module Fakes =

    let ulrik = {
        Id=1
        Hash=""
        NetId="ulrik"
        Name="Knudsen, Ulrik Palle"
        Position="Chief Technology Officer"
        Location="SMR Room 024"
        Campus="IUBLA"
        CampusPhone="812-856-0207"
        CampusEmail="ulrik@iu.edu"
        Expertise="Life, the universe, everything"
        Notes="foo"
        Role=Role.Admin
        Tools = Tools.IUware
        Responsibilities = Responsibilities.BizSysAnalysis
        HrDepartmentId=1
    }

    let brent = {
        Id=2
        Hash=""
        NetId="bmoberly"
        Name="Moberly, Brent Maximus"
        Position="Very Senior Software Developer Lead Architect Analyst"
        Location="CIB"
        Campus="IUBLA"
        CampusPhone="812-856-2138"
        CampusEmail="bmoberly@iu.edu"
        Expertise="Snivlin', grovlin', code expansion, copying/pasting from Stack Overflow"
        Notes="foo"
        Tools = Tools.IUware
        Role=Role.ItPro
        Responsibilities = Responsibilities.BizSysAnalysis
        HrDepartmentId=1
    }

    let cito:Unit = {Id=1; Name="College IT Office (CITO)"; Description=""; Url=""}
    let biology:Unit = {Id=2; Name="Biology IT"; Description=""; Url=""}
    let clientServices:Unit = {Id=3; Name="Client Services"; Description=""; Url=""}

    let arsd:Department = {Id=1; Name="BL-ARSD"; Description="Arts and Sciences Deans Office"; DisplayUnits=false}
    let dema:Department = {Id=1; Name="BL-DEMA"; Description=""; DisplayUnits=false}
    let iuware = {Id=1; Name="IUware Tools"; Description=""}
    let itproMail = {Id=2; Name="IT Pro Mailing List"; Description=""}

    let getFakeUser () = asyncTrial {
        let! user = async.Return ulrik
        return user
    }

    let getFakeProfile () : AsyncResult<UserProfile,Error> = asyncTrial {
        let! profile = async.Return {
            User=ulrik;
            Department=arsd;
            UnitMemberships = 
              [ {MemberWithRole.Id=cito.Id; Name=cito.Name; Role=Role.Admin}
                {MemberWithRole.Id=biology.Id; Name=biology.Name; Role=Role.CoAdmin} ]
        }       
        return profile
    }

    let getFakeSimpleSearchByTerm () : AsyncResult<SimpleSearch,Error> = asyncTrial {
        let result = {
                Users=[ulrik; brent]
                Departments=[arsd; dema]
                Units=[cito; clientServices]
            }
        return result
    }

    let getFakeUnits () = asyncTrial {
        let! units = async.Return { Units= [cito; clientServices] }
        return units
    }

    let getFakeUnit () = asyncTrial {
        let! profile = async.Return {
            Unit=cito
            Members=
              [ {MemberWithRole.Id=ulrik.Id; Name=ulrik.Name; Role=Role.Admin}
                {MemberWithRole.Id=brent.Id; Name=brent.Name; Role=Role.ItPro} ]
            SupportedDepartments=[arsd; dema]
        }
        return profile
    }

    let getFakeDepartments () = asyncTrial {
        let! departments = async.Return {Departments = [arsd; dema]}
        return departments
    }

    let getFakeDepartment () = asyncTrial {
        let! profile = async.Return {
            Department=arsd
            SupportingUnits=[cito]
            Units=[clientServices]
            Members= 
              [ {Member.Id=brent.Id; Name=brent.Name; Description=""}
                {Member.Id=ulrik.Id; Name=ulrik.Name; Description=""} ]
        }
        return profile
    }

    /// A canned data implementation of IDatabaseRespository (for testing)

    type FakesRepository() =
        interface IDataRepository with 
            member this.GetUserByNetId netId = getFakeUser ()
            member this.GetProfile id = getFakeProfile ()
            member this.GetSimpleSearchByTerm term = getFakeSimpleSearchByTerm ()
            member this.GetUnits () = getFakeUnits ()
            member this.GetUnit id = getFakeUnit ()
            member this.GetDepartments () = getFakeDepartments ()
            member this.GetDepartment id = getFakeDepartment ()
