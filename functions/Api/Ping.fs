namespace Functions.Api

open Chessie.ErrorHandling
open Functions.Common.Http
open Microsoft.Azure.WebJobs.Host
open System.Net.Http
open Functions.Common.Types

///<summary>
/// This module provides a function to return "Pong!" to the calling client. 
/// It demonstrates a basic GET request and response.
///</summary>
module Ping =
    
    type PingResult = {
        Message: string
    }

    let private sayPong () = trial {
        return { Message="pong!" }
    }

    /// <summary>
    /// Get 'pong!.
    /// </summary>
    let get (req: HttpRequestMessage) = asyncTrial {
        let! result = sayPong ()
        return result
    }
