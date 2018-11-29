namespace Functions.Common

open Types
open Util
open Chessie.ErrorHandling
open System

module Fakes =

    // Units
    let city:Unit = {Id=0; Name="City of Pawnee"; Description="City of Pawnee, Indiana"; Url=""}
    let parksAndRec:Unit = {Id=0; Name="Parks and Rec"; Description="Parks and Recreation"; Url=""}
    let fourthFloor:Unit = {Id=0; Name="Fourth Floor"; Description="It's spooky up there!"; Url=""}

    // Departments
    let parksDept:Department = {Id=0; Name="PA-PARKS"; Description="Parks and Recreation Department"; DisplayUnits=true}
    
    // People
    let swanson:Person = {
        Id=0
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
        HrDepartmentId=parksDept.Id
    }

    let knope:Person = {
        Id=0
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
        HrDepartmentId=parksDept.Id
    }

    let sebastian:Person = {
        Id=0
        Hash="hash"
        NetId="lsebastian"
        Name="Sebastian, L'il"
        Position="Mascot and Guiding Light"
        Location=""
        Campus=""
        CampusPhone=""
        CampusEmail="lknope@pawnee.in.us"
        Expertise="Hay; Being Small"
        Notes=""
        PhotoUrl="https://sasquatchbrewery.com/wp-content/uploads/2018/06/lil.jpg"
        Tools = Tools.ItProMail ||| Tools.ItProWeb
        Responsibilities = Responsibilities.UserExperience
        HrDepartmentId=parksDept.Id
    }

    // People DTOs
    let swansonDto:PersonDto = {
        Id=swanson.Id
        NetId=swanson.NetId
        Name=swanson.Name
        Position=swanson.Position
        Location=swanson.Location
        Campus=swanson.Campus
        CampusPhone=swanson.CampusPhone
        CampusEmail=swanson.CampusEmail
        Expertise=swanson.Expertise.Split(";")
        Notes=swanson.Notes
        PhotoUrl=swanson.PhotoUrl
        Tools = swanson.Tools |> mapFlagsToSeq
        Responsibilities = swanson.Responsibilities |> mapFlagsToSeq
        Department=parksDept
        UnitMemberships=
          [ {Id=parksAndRec.Id; Name=parksAndRec.Name; Description=""; Role=Role.Leader; Title="Director"; Tools=[ Tools.AccountMgt ]; PhotoUrl=swanson.PhotoUrl; Percentage=100} ]
    }

    let knopeDto:PersonDto = {
        Id=knope.Id
        NetId=knope.NetId
        Name=knope.Name
        Position=knope.Position
        Location=knope.Location
        Campus=knope.Campus
        CampusPhone=knope.CampusPhone
        CampusEmail=knope.CampusEmail
        Expertise=knope.Expertise.Split(";")
        Notes=knope.Notes
        PhotoUrl=knope.PhotoUrl
        Tools = knope.Tools |> mapFlagsToSeq
        Responsibilities = knope.Responsibilities |> mapFlagsToSeq
        Department=parksDept
        UnitMemberships=
          [ {Id=parksAndRec.Id; Name=parksAndRec.Name; Description=""; Role=Role.Sublead; Title="Deputy Director"; Tools=[ ]; PhotoUrl=knope.PhotoUrl; Percentage=100} ]
    }

    let sebastianDto:PersonDto = {
        Id=sebastian.Id
        NetId=sebastian.NetId
        Name=sebastian.Name
        Position=sebastian.Position
        Location=sebastian.Location
        Campus=sebastian.Campus
        CampusPhone=sebastian.CampusPhone
        CampusEmail=sebastian.CampusEmail
        Expertise=sebastian.Expertise.Split(";")
        Notes=sebastian.Notes
        PhotoUrl=sebastian.PhotoUrl
        Tools = sebastian.Tools |> mapFlagsToSeq
        Responsibilities = sebastian.Responsibilities |> mapFlagsToSeq
        Department=parksDept
        UnitMemberships=
          [ {Id=parksAndRec.Id; Name=parksAndRec.Name; Description=""; Role=Role.Member; Title="Mascot"; Tools=[ ]; PhotoUrl=sebastian.PhotoUrl; Percentage=100} ]
        
    }

    let iuware = {Id=1; Name="IUware Tools"; Description=""}
    let itproMail = {Id=2; Name="IT Pro Mailing List"; Description=""}

    let getFakeUser () = asyncTrial {
        let! user = async.Return swanson
        return user
    }

    let getFakeProfile () : AsyncResult<PersonDto,Error> = asyncTrial {
        let! profile = async.Return swansonDto
        return profile
    }

    let getFakeSimpleSearchByTerm () : AsyncResult<SimpleSearch,Error> = asyncTrial {
        let result = {
                Users=
                  [ {Id=swanson.Id; Name=swanson.Name; Description=""}
                    {Id=knope.Id; Name=knope.Name; Description=""}
                    {Id=sebastian.Id; Name=sebastian.Name; Description=""} ]
                Departments=
                  [ {Id=parksDept.Id; Name=parksDept.Name; Description=""} ]
                Units=
                  [ {Id=city.Id; Name=city.Name; Description=""}
                    {Id=parksAndRec.Id; Name=parksAndRec.Name; Description=""}
                    {Id=fourthFloor.Id; Name=fourthFloor.Name; Description=""} ]
            }
        return result
    }

    let getFakeUnits () = asyncTrial {
        let! units = async.Return ([parksAndRec; city] |> List.toSeq)
        return units
    }

    let getFakeUnit () = asyncTrial {
        let! profile = async.Return {
            Id=parksAndRec.Id
            Name=parksAndRec.Name
            Description=parksAndRec.Description
            Url=parksAndRec.Url
            Members= 
              [ {Id=swanson.Id; Name=swanson.Name; Description=""; Role=Role.Leader; Title="Director"; Tools=[ Tools.AccountMgt ]; PhotoUrl=swanson.PhotoUrl; Percentage=100}
                {Id=knope.Id; Name=knope.Name; Description=""; Role=Role.Sublead; Title="Deputy Director"; Tools=[ ]; PhotoUrl=knope.PhotoUrl; Percentage=100}
                {Id=sebastian.Id; Name=sebastian.Name; Description=""; Role=Role.Member; Title="Mascot"; Tools=[ ]; PhotoUrl=sebastian.PhotoUrl; Percentage=100} ]
            SupportedDepartments= [ parksDept ]
            Children= [ fourthFloor ]
            Parent= Some(city)
        }
        return profile
    }

    let getFakeDepartments () = asyncTrial {
        let! departments = async.Return ([ parksDept ] |> List.toSeq)
        return departments
    }

    let getFakeDepartment () = asyncTrial {
        let! profile = async.Return {
            Id=parksDept.Id
            Name=parksDept.Name
            Description=parksDept.Description
            SupportingUnits=[parksAndRec]
            Units=[parksAndRec]
            Members= 
              [ {Member.Id=swanson.Id; Name=swanson.Name; Description=""}
                {Member.Id=knope.Id; Name=knope.Name; Description=""}
                {Member.Id=sebastian.Id; Name=sebastian.Name; Description=""} ]
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
