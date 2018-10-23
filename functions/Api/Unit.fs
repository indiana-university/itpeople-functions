namespace MyFunctions.Api

open Chessie.ErrorHandling
open MyFunctions.Common.Types
open MyFunctions.Common.Jwt
open MyFunctions.Common.Http
open System.Net.Http

module Unit =

    let getById (req: HttpRequestMessage) config id (queryUnit:FetchById<UnitProfile>)  = asyncTrial {
        let! _ = requireMembership config req
        let! result = queryUnit id
        return result |> jsonResponse Status.OK
    }
    
    let getAll (req: HttpRequestMessage) config (queryUnits:FetchAll<UnitList>) = asyncTrial {
        let! _ = requireMembership config req
        let! result = queryUnits()
        return result |> jsonResponse Status.OK
    }


    
