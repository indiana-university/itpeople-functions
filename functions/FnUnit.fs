namespace MyFunctions

open Chessie.ErrorHandling
open MyFunctions.Types
open MyFunctions.Common
open Microsoft.AspNetCore.Http
open Microsoft.Azure.WebJobs.Host
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


    
