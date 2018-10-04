namespace MyFunctions.User

open Chessie.ErrorHandling
open System.Data.SqlClient
open MyFunctions.Types
open MyFunctions.Common
open MyFunctions.Database
open Microsoft.AspNetCore.Http
open Microsoft.Azure.WebJobs.Host
open System.Net
open System.Net.Http


///<summary>
/// This module provides a function to return "Pong!" to the calling client. 
/// It demonstrates a basic GET request and response.
///</summary>
module GetMe =
    let workflow (req: HttpRequest) (config:AppConfig) queryUser = asyncTrial {
        let! claims = requireMembership config req
        let! profile = bindAsyncResult (fun () -> queryUser claims.UserName)
        let response = profile |> jsonResponse Status.OK
        return response
    }

    /// <summary>
    /// Say hello to a person by name.
    /// </summary>
    let run (req: HttpRequest) (log: TraceWriter) (data:IDataRepository) config = async {
        let getProfileByNetId = data.GetProfileByNetId
        let! result = workflow req config getProfileByNetId |> Async.ofAsyncResult
        return constructResponse log result
    }

///<summary>
/// This module provides a function to return "Pong!" to the calling client. 
/// It demonstrates a basic GET request and response.
///</summary>
module GetId =
    let workflow (req: HttpRequest) (config:AppConfig) id queryProfileById = asyncTrial {
        let! _ = requireMembership config req
        let! user = bindAsyncResult (fun () -> queryProfileById id)
        let response = user |> jsonResponse Status.OK
        return response
    }

    /// <summary>
    /// Say hello to a person by name.
    /// </summary>
    let run (req: HttpRequest) (log: TraceWriter) (data:IDataRepository) id config = async {
        let getProfileById = data.GetProfileById
        let! result = workflow req config id getProfileById |> Async.ofAsyncResult
        return constructResponse log result
    }

module Put =
    let validatePostBody body = ok body

    let validateUserCanEditRecord claims record = ok record

    let workflow (req: HttpRequest) (config:AppConfig) getProfileRecord updateProfileRecord = asyncTrial {
        let! claims = requireMembership config req
        let! body = deserializeBody<User> req
        let! update = validatePostBody body
        let! record = bindAsyncResult (fun () -> getProfileRecord)
        let! _ = validateUserCanEditRecord claims record
        let! updatedRecord = bindAsyncResult (fun () -> updateProfileRecord record update)
        let response = updatedRecord |> jsonResponse Status.OK
        return response
    }

    let run req log id config = async {
        use cn = new SqlConnection(config.DbConnectionString);
        let getUser = queryUser cn id
        let updateUser = updateUser cn id
        let! result = workflow req config getUser updateUser |> Async.ofAsyncResult
        return constructResponse log result
    }
