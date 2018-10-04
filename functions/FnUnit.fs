namespace MyFunctions.Unit

open Chessie.ErrorHandling
open MyFunctions.Types
open MyFunctions.Common
open Microsoft.AspNetCore.Http
open Microsoft.Azure.WebJobs.Host

module GetAll =

    let workflow (req: HttpRequest) config queryUnits = asyncTrial {
        let! _ = requireMembership config req
        let! result = bindAsyncResult (fun () -> queryUnits())
        return result |> jsonResponse Status.OK
    }

    let run (req: HttpRequest) (log: TraceWriter) (data: IDataRepository) config = async {
        let queryUnits = data.GetUnits
        let! result = workflow req config queryUnits |> Async.ofAsyncResult
        return constructResponse log result
    }
        
module GetId =

    let workflow (req: HttpRequest) config id queryUnit  = asyncTrial {
        let! _ = requireMembership config req
        let! result = bindAsyncResult (fun () -> queryUnit id)
        return result |> jsonResponse Status.OK
    }

    let run (req: HttpRequest) (log: TraceWriter) (data: IDataRepository) id config = async {
        let queryUnit = data.GetUnitById
        let! result = workflow req config id queryUnit |> Async.ofAsyncResult
        return constructResponse log result
    }
