namespace MyFunctions.Department

open Chessie.ErrorHandling
open MyFunctions.Types
open MyFunctions.Common
open Microsoft.AspNetCore.Http
open Microsoft.Azure.WebJobs.Host

module GetAll =

    let workflow (req: HttpRequest) config (queryDepartments:FetchAll<DepartmentList>) = asyncTrial {
        let! _ = requireMembership config req
        let! result = queryDepartments()
        return result |> jsonResponse Status.OK
    }

    let run (req: HttpRequest) (log: TraceWriter) (data: IDataRepository) config = async {
        let queryDepartments = data.GetDepartments
        let! result = workflow req config queryDepartments |> Async.ofAsyncResult
        return constructResponse log result
    }
        
module GetId =

    let workflow (req: HttpRequest) config id (queryDepartment:FetchById<DepartmentProfile>) = asyncTrial {
        let! _ = requireMembership config req
        let! result = queryDepartment id
        return result |> jsonResponse Status.OK
    }

    let run (req: HttpRequest) (log: TraceWriter) (data: IDataRepository) id config = async {
        let queryDepartment = data.GetDepartment
        let! result = workflow req config id queryDepartment |> Async.ofAsyncResult
        return constructResponse log result
    }
