module Core.Fakes

open Types

// UaaResponse 
let uaaJwt = { access_token = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VyX25hbWUiOiJqb2huZG9lIiwiZXhwIjoyOTE2MjM5MDIyfQ.ELo8I2IImgRRT75cOcUcSllbkWVWAIQA2WQr27WSpWwF2c7Wh9hjqkPriZ4PxSD4OR9IgGWt5HWpPQFDOwlv1O7tl2gLcZ5LayuRzQX2AEn-UsEBECStEwABUtwhg92q9Ov-GRbYqmP_5UpntbCr8aZfMEuMfLTIWePcORq_FrJhjyRUoKhUo8007W6RO58n03erVlslSB1f-JTYtBdhYOlgmDTOCp_rc-gPvKFePMb4c05IOD-x4ce2QGkZlL_pE1_OLKdn5A07k7B8x53v38WvWuisFGIPXUcuP3j9hdJHIzYLSfL5t1OABT1-57C91yaMAgVsATMRgT9qtzYQgg" }
let fakePublicKey = "-----BEGIN PUBLIC KEY-----\nMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAnzyis1ZjfNB0bBgKFMSv\nvkTtwlvBsaJq7S5wA+kzeVOVpVWwkWdVha4s38XM/pa/yr47av7+z3VTmvDRyAHc\naT92whREFpLv9cj5lTeJSibyr/Mrm/YtjCZVWgaOYIhwrXwKLqPr/11inWsAkfIy\ntvHWTxZYEcXLgAXFuUuaS3uF9gEiNQwzGTU1v0FqkqTBr4B8nW3HCN47XUu0t8Y0\ne+lf4s4OxQawWD79J9/5d3Ry0vbV3Am1FtGJiJvOwRsIfVChDpYStTcHTCMqtvWb\nV6L11BWkpzGXSW4Hv43qa+GSYOD2QU68Mb59oSk2OB+BtOLpJofmbGEGgvmwyCI9\nMwIDAQAB\n-----END PUBLIC KEY-----"
// Units
let cityOfPawnee:Unit = {Id=1; Name="City of Pawnee"; Description="City of Pawnee, Indiana"; Url="http://pawneeindiana.com/"; ParentId=None; Parent=None}
let parksAndRec:Unit = {Id=2; Name="Parks and Rec"; Description="Parks and Recreation"; Url="http://pawneeindiana.com/parks-and-recreation/"; ParentId=Some(cityOfPawnee.Id); Parent=Some(cityOfPawnee)}
let fourthFloor:Unit = {Id=3; Name="Fourth Floor"; Description="City Hall's Fourth Floor"; Url="http://pawneeindiana.com/fourth-floor/"; ParentId=Some(cityOfPawnee.Id); Parent=Some(cityOfPawnee)}
let parksAndRecUnitRequest:UnitRequest = { Name="Parks and Rec"; Description="Parks and Recreation"; Url="http://pawneeindiana.com/parks-and-recreation/"; ParentId=Some(cityOfPawnee.Id) }

// Departments
let parksDept:Department = {Id=1; Name="PA-PARKS"; Description="Parks and Recreation Department" }

// People
let swanson:Person = {
    Id=1
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
    Responsibilities = Responsibilities.ItLeadership
    DepartmentId=parksDept.Id
    Department=parksDept
    IsServiceAdmin=false
}

let knope:Person = {
    Id=2
    NetId="lknope"
    Name="Knope, Leslie Park"
    Position="Parks and Rec Deputy Director "
    Location=""
    Campus=""
    CampusPhone=""
    CampusEmail="lknope@pawnee.in.us"
    Expertise="Canvasing; Waffles"
    Notes=""
    PhotoUrl="https://en.wikipedia.org/wiki/Leslie_Knope#/media/File:Leslie_Knope_(played_by_Amy_Poehler).png"
    Responsibilities = Responsibilities.ItLeadership ||| Responsibilities.ItProjectMgt
    DepartmentId=parksDept.Id
    Department=parksDept
    IsServiceAdmin=false
}

let wyatt:Person = {
    Id=3
    NetId="bwyatt"
    Name="Wyatt, Ben"
    Position="Auditor"
    Location=""
    Campus=""
    CampusPhone=""
    CampusEmail="bwyatt@pawnee.in.us"
    Expertise="Board Games; Comic Books"
    Notes=""
    PhotoUrl="https://sasquatchbrewery.com/wp-content/uploads/2018/06/lil.jpg"
    Responsibilities = Responsibilities.ItProjectMgt
    DepartmentId=parksDept.Id
    Department=parksDept
    IsServiceAdmin=false
}

let admin = {
    Id=3
    NetId="johndoe"
    Name="Doe, John"
    Position="Admin"
    Location=""
    Campus=""
    CampusPhone=""
    CampusEmail="johndoe@pawnee.in.us"
    Expertise="Services; Administration"
    Notes=""
    PhotoUrl=""
    Responsibilities = Responsibilities.None
    DepartmentId=parksDept.Id
    Department=parksDept
    IsServiceAdmin=true
}

let tool: Tool = 
  { Id=1
    Name="Hammer"
    Description=""
    ADPath=""
    DepartmentScoped=true }

let memberTool:MemberTool = 
  { Id=1
    MembershipId=1
    ToolId=1 }

let knopeMembershipRequest:UnitMemberRequest = {
    UnitId=parksAndRec.Id
    PersonId=Some(knope.Id)
    Role=Role.Sublead
    Permissions=UnitPermissions.Viewer
    Title="Deputy Director"
    Percentage=100
    Notes=""
}

let swansonMembership:UnitMember = {
    Id=1
    UnitId=parksAndRec.Id
    PersonId=Some(swanson.Id)
    NetId=Some(swanson.NetId)
    Unit=parksAndRec
    Person=Some(swanson)
    Title="Director"
    Role=Role.Leader
    Permissions=UnitPermissions.Owner
    Percentage=100
    MemberTools=[ memberTool ]
    Notes=""
}

let knopeMembership = {
    Id=2
    UnitId=parksAndRec.Id
    PersonId=Some(knope.Id)
    NetId=Some(knope.NetId)
    Role=Role.Sublead
    Permissions=UnitPermissions.Viewer
    Title="Deputy Director"
    Percentage=100
    Person=Some(knope)
    Unit=parksAndRec
    MemberTools=Seq.empty
    Notes="Owner of server PA-Parks-Web"
}

let parksAndRecVacancy = {
    Id=3
    UnitId=parksAndRec.Id
    PersonId=None
    NetId=None
    Role=Role.Member
    Permissions=UnitPermissions.Viewer
    Title="Assistant to the Manager"
    Percentage=100
    Person=None
    Unit=parksAndRec
    MemberTools=Seq.empty
    Notes=""
}

let wyattMembership:UnitMember = {
    Id=4
    UnitId=cityOfPawnee.Id
    PersonId=Some(wyatt.Id)
    NetId=Some(wyatt.NetId)
    Unit=cityOfPawnee
    Person=Some(wyatt)
    Title="Auditor"
    Role=Role.Leader
    Permissions=UnitPermissions.Owner
    Percentage=100
    MemberTools=Seq.empty
    Notes=""
}

let supportRelationshipRequest:SupportRelationshipRequest = {
    UnitId=cityOfPawnee.Id
    DepartmentId=parksDept.Id
}

let supportRelationship:SupportRelationship = {
    Id=1
    UnitId=cityOfPawnee.Id
    DepartmentId=parksDept.Id
    Unit=cityOfPawnee
    Department=parksDept
}

let toolPermission:ToolPermission = {
    NetId=wyatt.NetId
    ToolName=tool.Name
    DepartmentName=parksDept.Name
}
