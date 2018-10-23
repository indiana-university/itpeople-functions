namespace MyFunctions.Api.Department

open Chessie.ErrorHandling
open MyFunctions.Common.Types
open MyFunctions.Common.Jwt
open MyFunctions.Common.Http
open Microsoft.Azure.WebJobs.Host
open System.Net.Http

module GetAll =

    let workflow (req: HttpRequestMessage) config (queryDepartments:FetchAll<DepartmentList>) = asyncTrial {
        let! _ = requireMembership config req
        let! result = queryDepartments()
        return result |> jsonResponse Status.OK
    }

    let run (req: HttpRequestMessage) (log: TraceWriter) (data: IDataRepository) config = async {
        let queryDepartments = data.GetDepartments
        let! result = workflow req config queryDepartments |> Async.ofAsyncResult
        return constructResponse log result
    }
        
module GetId =

    let workflow (req: HttpRequestMessage) config id (queryDepartment:FetchById<DepartmentProfile>) = asyncTrial {
        let! _ = requireMembership config req
        let! result = queryDepartment id
        return result |> jsonResponse Status.OK
    }

    let run (req: HttpRequestMessage) (log: TraceWriter) (data: IDataRepository) id config = async {
        let queryDepartment = data.GetDepartment
        let! result = workflow req config id queryDepartment |> Async.ofAsyncResult
        return constructResponse log result
    }
