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

    let cito = {Id=1; Name="College IT Office (CITO)"; Description=""}
    let clientServices = {Id=1; Name="Client Services"; Description=""}

    let arsd = {Id=1; Name="BL-ARSD"; Description="Arts and Sciences Deans Office"}
    let dema = {Id=1; Name="BL-DEMA"; Description=""}
    let iuware = {Id=1; Name="IUware Tools"; Description=""}
    let itproMail = {Id=2; Name="IT Pro Mailing List"; Description=""}

    let getFakeUser netId = async {
        let! user = async.Return ulrik
        return user |> ok
    }

    let getFakeProfile arg = async {
        let! user = async.Return ulrik
        let! unit = async.Return cito
        let! department = async.Return arsd
        let! supportedDepartments = async.Return [arsd; dema]
        let! toolsAccess = async.Return [iuware; itproMail]
        let profile = {User=user;Unit=unit;Department=department;SupportedDepartments=supportedDepartments;ToolsAccess=toolsAccess}       
        return profile |> ok
    }

    let GetSimpleSearchByTerm term = async {
        let! users = async.Return [ulrik; brent]
        let! departments = async.Return [arsd; dema]
        let! units = async.Return [cito; clientServices ]
        let simpleSearch = {Users=users; Departments=departments; Units=units}
        return simpleSearch |> ok
    }

    type FakesRepository() =
        interface IDataRepository with 
            member this.GetUserByNetId netId = getFakeUser netId
            member this.GetProfileById id = getFakeProfile id
            member this.GetProfileByNetId netId = getFakeProfile netId
            member this.GetSimpleSearchByTerm term = GetSimpleSearchByTerm term
