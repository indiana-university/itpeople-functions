module Functions.Authorization

open System
open Core.Types

let canCreateDeleteUnit auth netid  =
    auth.IsServiceAdmin netid

let canModifyUnit unitId auth netid =
    auth.IsUnitManager netid unitId 

let canModifyUnitMemberTools unitId auth netid = 
    auth.IsUnitToolManager netid unitId 

let canModifyPerson personId auth netid =
    auth.CanModifyPerson netid personId

let parsePermissionResult canModify = 
    if canModify
    then ok [GET; POST; PUT; DELETE]
    else ok [GET;]

let determineAuthenticatedUserPermissions (authRepo:AuthorizationRepository) authFn (netid:NetId) =
    authFn authRepo netid
    >>= parsePermissionResult

let parseAuthorizationResult model canModify = 
    if canModify
    then ok model
    else error (Status.Forbidden, "You are not authorized to modify this resource.")

let authorizeRequest<'T> (authRepo:AuthorizationRepository) (model:'T) authFn (netid:NetId) =
    authFn authRepo netid
    >>= parseAuthorizationResult model
