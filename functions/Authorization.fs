module Functions.Authorization

open System
open Core.Types

/// Temporary: a list of IT people admins.
let isAdmin (user:JwtClaims) =
    let admins = [ "jhoerr"; "kendjone"; "jerussel"; "brrund"; "mattzink"; "johndoe" ]
    admins |> List.contains user.UserName

let parseAuthResult model result =
    if result
    then Ok model |> ar
    else Error (Status.Forbidden, "You are not authorized to modify this resource.") |> ar

let canCreateDeleteUnit auth model user  =
    auth.IsServiceAdmin user.UserName
    >>= parseAuthResult model

let canModifyUnit auth model user =
    auth.IsUnitManager user.UserName 0
    >>= parseAuthResult model

let canModifyUnitMemberTools auth model user = 
    auth.IsUnitToolManager user.UserName 0
    >>= parseAuthResult model
