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

///<summary>
/// This module defines the bindings and triggers for all functions in the project
///</summary
module Functions =

    let setState (reg:HttpRequestMessage) (log:TraceWriter) = 
        log.Info "hello world" 
        

    /// (Anonymous) A function that simply returns, "Pong!" 
    [<FunctionName("InitializeState")>]
    let ping
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "post", Route = "state")>]
        req: HttpRequestMessage, log: TraceWriter) =
        
        Api.Common.getResponse' log setState

    