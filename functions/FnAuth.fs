namespace MyFunctions.Auth

open Chessie.ErrorHandling
open MyFunctions.Types
open MyFunctions.Common
open MyFunctions.Database
open Microsoft.AspNetCore.Http
open Microsoft.Azure.WebJobs.Host
open System.Net
open System.Net.Http
open System.Collections.Generic
open System.Data.SqlClient

///<summary>
/// This module provides a function to return "Pong!" to the calling client. 
/// It demonstrates a basic GET request and response.
///</summary>
module Get =
    
    type ResponseModel = {
        access_token: string
    }

    let createTokenRequest clientId clientSecret redirectUrl code =
        let fn () = 
            dict[
                "grant_type", "authorization_code"
                "code", code
                "client_id", clientId
                "client_secret", clientSecret
                "redirect_uri", redirectUrl
            ]
            |> Dictionary
            |> (fun d-> new FormUrlEncodedContent(d))
        tryf Status.InternalServerError fn

    let getAppRole queryUserByName username = async {
        let! result = queryUserByName username
        match result with
        | Ok(_:User,_) -> return ok ROLE_USER
        | Bad([(Status.NotFound, _)]) -> return fail (HttpStatusCode.Forbidden, "Only registered IT Pros are allowed to view this informaiton.")
        | Bad(msgs) -> return msgs |> List.head |> fail
    }

    let returnToken token = 
        let fn() = 
            { access_token = token } 
            |> jsonResponse Status.OK
        tryf Status.InternalServerError fn
    
    let workflow (req: HttpRequest) config (queryUserByName:string -> AsyncResult<User,Error>) = asyncTrial {
        let getUaaJwt request = bindAsyncResult (fun () -> postAsync<ResponseModel> config.OAuth2TokenUrl request)

        let! oauthCode = getQueryParam "code" req
        let! uaaRequest = createTokenRequest config.OAuth2ClientId config.OAuth2ClientSecret config.OAuth2RedirectUrl oauthCode
        let! uaaJwt = getUaaJwt uaaRequest
        let! uaaClaims = decodeUaaJwt uaaJwt.access_token
        let! user = queryUserByName uaaClaims.UserName
        let! appJwt = encodeJwt config.JwtSecret uaaClaims.Expiration user.Id user.NetId
        let! response = returnToken appJwt         
        return response
    }

    let run (req: HttpRequest) (log: TraceWriter) (data:IDataRepository) config = async {
        let queryUserByNetId = data.GetUserByNetId
        let! result = workflow req config queryUserByNetId |> Async.ofAsyncResult
        return constructResponse log result
    }
