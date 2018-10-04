namespace MyFunctions

open Types
open Chessie.ErrorHandling

module Fakes =

    let ulrik = {
        Id=1;
        Hash="";
        NetId="ulrik";
        Name="Knudsen, Ulrik Palle";
        Role=Role.Admin;
        Position="Chief Technology Officer";
        Location="SMR Room 024";
        Campus="IUBLA";
        CampusPhone="812-856-0207";
        CampusEmail="ulrik@iu.edu";
        Expertise="Life, the universe, everything";
        Responsibilities="IT Director";
        HrDepartmentId=1;
        UnitId=1
    }

    let brent = {
        Id=2;
        Hash="";
        NetId="bmoberly";
        Name="Moberly, Brent Maximus";
        Role=Role.ItPro;
        Position="Very Senior Software Developer Lead Architect Analyst";
        Location="CIB";
        Campus="IUBLA";
        CampusPhone="812-856-2138";
        CampusEmail="bmoberly@iu.edu";
        Expertise="Snivlin', grovlin', code expansion, copying/pasting from Stack Overflow";
        Responsibilities="Typing, shirking";
        HrDepartmentId=1;
        UnitId=1
    }

    let cito:Unit = {Id=1; Name="College IT Office (CITO)"; Description=""}
    let clientServices:Unit = {Id=1; Name="Client Services"; Description=""}

    let arsd:Department = {Id=1; Name="BL-ARSD"; Description="Arts and Sciences Deans Office"}
    let dema:Department = {Id=1; Name="BL-DEMA"; Description=""}
    let iuware = {Id=1; Name="IUware Tools"; Description=""}
    let itproMail = {Id=2; Name="IT Pro Mailing List"; Description=""}

    let getFakeUser netId = async {
        let! user = async.Return ulrik
        return user |> ok
    }

    let getFakeProfile arg = async {
        let! profile = async.Return {
            User=ulrik;
            Unit=cito;
            Department=arsd;
            SupportedDepartments=[arsd; dema];
            ToolsAccess=[iuware; itproMail]
        }       
        return profile |> ok
    }

    let getFakeSimpleSearchByTerm term = async {
        let! simpleSearch = async.Return {
            Users=[ulrik; brent]
            Departments=[arsd; dema]
            Units=[cito; clientServices]
        }
        return simpleSearch |> ok
    }

    let getFakeUnits () = async {
        let! units = async.Return { Units= [cito; clientServices] }
        return units |> ok
    }

    let getFakeUnit id = async {
        let! profile = async.Return {
            Unit=cito
            Admins=[ulrik]
            ItPros=[brent] 
            Selfs=[]
            SupportedDepartments=[arsd; dema]
        }
        return profile |> ok
    }

    let getFakeDepartments () = async {
        let! departments = async.Return {Departments = [arsd; dema]}
        return departments |> ok
    }

    let getFakeDepartment id = async {
        let! profile = async.Return {
            Department=arsd
            Servicers=[clientServices]
            Units=[cito]
        }
        return profile |> ok
    }

    type FakesRepository() =
        interface IDataRepository with 
            member this.GetUserByNetId netId = getFakeUser netId
            member this.GetProfileById id = getFakeProfile id
            member this.GetProfileByNetId netId = getFakeProfile netId
            member this.GetSimpleSearchByTerm term = getFakeSimpleSearchByTerm term
            member this.GetUnits () = getFakeUnits ()
            member this.GetUnitById id = getFakeUnit id
            member this.GetDepartments () = getFakeDepartments ()
            member this.GetDepartmentById id = getFakeDepartment id
