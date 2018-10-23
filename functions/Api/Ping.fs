namespace MyFunctions.Api.Ping

open Chessie.ErrorHandling
open MyFunctions.Common.Types
open MyFunctions.Common.Http
open Microsoft.Azure.WebJobs.Host
open System.Net.Http

///<summary>
/// This module provides a function to return "Pong!" to the calling client. 
/// It demonstrates a basic GET request and response.
///</summary>
module Get =
    
    let sayPong = trial {
        return "pong!" |> jsonResponse Status.OK 
    }

    let workflow (req: HttpRequestMessage) = asyncTrial {
        let! result = sayPong
        return result
    }

    /// <summary>
    /// Say hello to a person by name.
    /// </summary>
    let run (req: HttpRequestMessage) (log: TraceWriter) = async {
        let! result = (workflow req) |> Async.ofAsyncResult
        return constructResponse log result
    }
