namespace Functions.Common

open Types
open Chessie.ErrorHandling

module Fakes =

    let cito:Unit = {Id=1; Name="College IT Office (CITO)"; Description=""; Url=""}
    let biology:Unit = {Id=2; Name="Biology IT"; Description=""; Url=""}
    let clientServices:Unit = {Id=3; Name="Client Services"; Description=""; Url=""}

    let arsd:Department = {Id=1; Name="BL-ARSD"; Description="Arts and Sciences Deans Office"; DisplayUnits=false}
    let dema:Department = {Id=1; Name="BL-DEMA"; Description=""; DisplayUnits=false}

    let ronswanson:PersonDto = {
        Id=1
        NetId="rswanso"
        Name="Swanson, Ron"
        Position="Parks and Rec Director "
        Location="SMR Room 024"
        Campus="IUBLA"
        CampusPhone="812-856-0207"
        CampusEmail="rswanso@iu.edu"
        Expertise="Woodworking, honor"
        Notes="foo"
        PhotoUrl=Some("http://flavorwire.files.wordpress.com/2011/11/ron-swanson.jpg")
        Tools = [ Tools.IUware ]
        Responsibilities = [ Responsibilities.BizSysAnalysis ] 
        Department=arsd
        UnitMemberships=[]|>List.toSeq
    }

    let brent:PersonDto = {
        Id=2
        NetId="bmoberly"
        Name="Moberly, Brent Maximus"
        Position="Very Senior Software Developer Lead Architect Analyst"
        Location="CIB"
        Campus="IUBLA"
        CampusPhone="812-856-2138"
        CampusEmail="bmoberly@iu.edu"
        Expertise="Snivlin', grovlin', code expansion, copying/pasting from Stack Overflow"
        Notes="foo"
        PhotoUrl=None
        Tools = [ Tools.IUware ]
        Responsibilities = [ Responsibilities.BizSysAnalysis ] 
        Department=arsd
        UnitMemberships=[]|>List.toSeq
    }

    let iuware = {Id=1; Name="IUware Tools"; Description=""}
    let itproMail = {Id=2; Name="IT Pro Mailing List"; Description=""}

    let getFakeUser () = asyncTrial {
        let! user = async.Return {
            Id=ronswanson.Id
            NetId=ronswanson.NetId
            Name=ronswanson.Name
            Hash="abcd1234"
            Position=ronswanson.Position
            Location=ronswanson.Location
            Campus=ronswanson.Campus
            CampusEmail=ronswanson.CampusEmail
            CampusPhone=ronswanson.CampusPhone
            Expertise=ronswanson.Expertise
            Notes=ronswanson.Notes
            Responsibilities=ronswanson.Responsibilities |> Seq.head
            Tools=ronswanson.Tools |> Seq.head
            HrDepartmentId=1
        }
        return user
    }

    let getFakeProfile () : AsyncResult<PersonDto,Error> = asyncTrial {
        let! profile = async.Return ronswanson
        return profile
    }

    let getFakeSimpleSearchByTerm () : AsyncResult<SimpleSearch,Error> = asyncTrial {
        let result = {
                Users=[
                    {Id=ronswanson.Id; Name=ronswanson.Name; Description=""}
                    {Id=brent.Id; Name=brent.Name; Description=""}
                ]
                Departments=[
                    {Id=arsd.Id; Name=arsd.Name; Description=""}
                    {Id=dema.Id; Name=dema.Name; Description=""}
                ]
                Units=[
                    {Id=cito.Id; Name=cito.Name; Description=""}
                    {Id=clientServices.Id; Name=clientServices.Name; Description=""}
                ]
            }
        return result
    }

    let getFakeUnits () = asyncTrial {
        let! units = async.Return ([cito; clientServices] |> List.toSeq)
        return units
    }

    let getFakeUnit () = asyncTrial {
        let! profile = async.Return {
            Id=cito.Id
            Name=cito.Name
            Description=cito.Description
            Url=Some(cito.Url)
            Members= Some ([  
                {UnitMembership.Id=ronswanson.Id; Name=ronswanson.Name; Description=""; Role=Role.Leader; Title="Director"; Tools=None; PhotoUrl=ronswanson.PhotoUrl; Percentage=100}
                {UnitMembership.Id=brent.Id; Name=brent.Name; Description=""; Role=Role.Member; Title="Developer"; Tools=None; PhotoUrl=None; Percentage=100} 
                ] |> List.toSeq )
            SupportedDepartments= Some ([
                arsd; dema
                ] |> List.toSeq)
            Children= Some([
                {Unit.Id=2; Name="Fourth Floor"; Description="This is a child unit description"; Url="http://example.com"}
                {Unit.Id=3; Name="Other Child Unit"; Description="This is a child unit description"; Url="http://example.com"}
            ] |> List.toSeq)
            Parent= Some({Unit.Id=4; Name="City Council"; Description="The management, supervision, coordination, and implementation of an array of leisure service opportunities, including such organized activities as athletics, sports, arts, crafts, drama, physical fitness, music, and aquatics, utilizing recreation centers, athletic fields, swimming pools, open space, schools, and special facilities."; Url="http://example.com"})
        }
        return profile
    }

    let getFakeDepartments () = asyncTrial {
        let! departments = async.Return ([arsd; dema] |> List.toSeq)
        return departments
    }

    let getFakeDepartment () = asyncTrial {
        let! profile = async.Return {
            Department=arsd
            SupportingUnits=[cito]
            Units=[clientServices]
            Members= 
              [ {Member.Id=brent.Id; Name=brent.Name; Description=""}
                {Member.Id=ronswanson.Id; Name=ronswanson.Name; Description=""} ]
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
