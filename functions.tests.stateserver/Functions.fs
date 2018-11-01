namespace MyFunctions

open MyFunctions.Common.Types
open Microsoft.Azure.WebJobs
open Microsoft.AspNetCore.Http
open Microsoft.Azure.WebJobs.Host
open System.Net.Http
open Microsoft.Extensions.Logging
open Microsoft.Azure.WebJobs.Host
open Microsoft.Azure.WebJobs.Host
open Chessie.ErrorHandling
open Common.Http
open System.Net

///<summary>
/// This module defines the bindings and triggers for all functions in the project
///</summary
module Functions =

    type ProviderState = {
        Consumer: string
        State: string
    }

    let someFunction () = asyncTrial {
        return "yay"
    }

    let matcher (arg:string) = 
        match arg with 
        | "test" -> ok someFunction
        | _ ->  fail (HttpStatusCode.BadRequest, (sprintf "no state defined for %s" arg))
    
         
    let setState (req: HttpRequestMessage) = asyncTrial {
        let! state = deserializeBody<ProviderState> req
        let! fn = matcher state.State
        let! result = fn()
        return result
    }

    /// (Anonymous) A function that simply returns, "Pong!" 
    [<FunctionName("InitializeState")>]
    let ping
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "post", Route = "state")>]
        req: HttpRequestMessage, log: TraceWriter) =
        let fn () = setState req
        Api.Common.getResponse' log fn

    
    