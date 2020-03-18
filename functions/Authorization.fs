module Functions.Authorization

open Core.Types

let canCreateDeleteUnit auth netid  =
    auth.IsServiceAdmin netid

let canModifyUnit unitId auth netid =
    auth.IsUnitManager netid unitId 

let canModifyUnitMemberTools unitId auth netid = 
    auth.IsUnitToolManager netid unitId 

let canModifyPerson personId auth netid =
    auth.CanModifyPerson netid personId

