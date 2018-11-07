namespace Functions.Api

open Functions.Common.Types
open Functions.Common.Http
open Chessie.ErrorHandling
open System.Net.Http
open Functions.Common.Jwt


/// This module provides functions to fetch and update user profiles.
module User =

    /// <summary>
    /// Get the profile of the user associated with the JWT passed in the Authentication header.
    /// </summary>
    /// <param name="req">The HTTP request that triggered this function</param>
    /// <param name="config">The application configuration</param>
    /// <param name="queryUser">A function to fetch a given user profile by its Id</param>
    /// <returns>
    /// A user profile, or error information.
    /// </returns>
    let getMe (req: HttpRequestMessage) (config:AppConfig) (queryUser: FetchById<UserProfile>) = asyncTrial {
        let! claims = requireMembership config req
        let! profile = queryUser claims.UserId
        return profile
    }

// module Put =
//     let validatePostBody body = ok body

//     let validateUserCanEditRecord claims record = ok record

//     let workflow (req: HttpRequest) (config:AppConfig) getProfileRecord updateProfileRecord = asyncTrial {
//         let! claims = requireMembership config req
//         let! body = deserializeBody<User> req
//         let! update = validatePostBody body
//         let! record = bindAsyncResult (fun () -> getProfileRecord)
//         let! _ = validateUserCanEditRecord claims record
//         let! updatedRecord = bindAsyncResult (fun () -> updateProfileRecord record update)
//         let response = updatedRecord |> jsonResponse Status.OK
//         return response
//     }

//     let run req log id config = async {
//         use cn = new SqlConnection(config.DbConnectionString);
//         let getUser = queryUser cn id
//         let updateUser = updateUser cn id
//         let! result = workflow req config getUser updateUser |> Async.ofAsyncResult
//         return constructResponse log result
//     }
