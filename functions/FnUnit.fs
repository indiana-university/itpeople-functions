namespace MyFunctions.Unit

open Chessie.ErrorHandling
open MyFunctions.Types
open MyFunctions.Common
open Microsoft.AspNetCore.Http
open Microsoft.Azure.WebJobs.Host
open System.Net.Http

module GetAll =

    let workflow (req: HttpRequestMessage) config (queryUnits:FetchAll<UnitList>) = asyncTrial {
        let! _ = requireMembership config req
        let! result = queryUnits()
        return result |> jsonResponse Status.OK
    }

    let run (req: HttpRequestMessage) (log: TraceWriter) (data: IDataRepository) config = async {
        let queryUnits = data.GetUnits
        let! result = workflow req config queryUnits |> Async.ofAsyncResult
        return constructResponse log result
    }
        
module GetId =

    let workflow (req: HttpRequestMessage) config id (queryUnit:FetchById<UnitProfile>)  = asyncTrial {
        let! _ = requireMembership config req
        let! result = queryUnit id
        return result |> jsonResponse Status.OK
    }

    let run (req: HttpRequestMessage) (log: TraceWriter) (data: IDataRepository) id config = async {
        let queryUnit = data.GetUnit
        let! result = workflow req config id queryUnit |> Async.ofAsyncResult
        return constructResponse log result
    }
