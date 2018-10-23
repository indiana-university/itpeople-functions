namespace MyFunctions.Api.Search

open Chessie.ErrorHandling
open MyFunctions.Common.Types
open MyFunctions.Common.Jwt
open MyFunctions.Common.Http
open Microsoft.Azure.WebJobs.Host
open System.Net.Http

module GetSimple =

    let workflow (req: HttpRequestMessage) config (getSearchResults: string -> AsyncResult<SimpleSearch,Error>) = asyncTrial {
        let! _ = requireMembership config req
        let! term = getQueryParam "term" req
        let! result = getSearchResults term
        return result |> jsonResponse Status.OK
    }

    let run (req: HttpRequestMessage) (log: TraceWriter) (data: IDataRepository) config = async {
        let! result = workflow req config (data.GetSimpleSearchByTerm) |> Async.ofAsyncResult
        return constructResponse log result
    }

