namespace Functions.Api

open Chessie.ErrorHandling
open Functions.Common.Types
open Functions.Common.Util
open Functions.Common.Http
open Functions.Common.Jwt
open Microsoft.Azure.WebJobs.Host
open System.Net
open System.Net.Http
open System.Collections.Generic

///<summary>
/// Provides functions to authenticate a user with UAA and generate an app-level JWT.
///</summary>
module Auth =
    
    type ResponseModel = {
        access_token: string
    }

    /// Generate a form url-encoded request to exchange the code for a JWT.
    let private createTokenRequest clientId clientSecret redirectUrl code =
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

    /// Determine the application role associated with the authenticated user.
    let private getAppRole queryUserByName username = async {
        let! result = queryUserByName username
        match result with
        | Ok(_:User,_) -> return ok ROLE_USER
        | Bad([(Status.NotFound, _)]) -> return fail (HttpStatusCode.Forbidden, "Only registered IT Pros are allowed to view this informaiton.")
        | Bad(msgs) -> return msgs |> List.head |> fail
    }

    /// Exchange an OAuth code for a UAA JWT. Fetch the user associated with the JWT and roll a new JWT
    /// containing the original JWT, the user ID, and user Role.  
    let get (req: HttpRequestMessage) config (queryUserByName:string -> AsyncResult<User,Error>) = asyncTrial {
        let getUaaJwt request = bindAsyncResult (fun () -> postAsync<ResponseModel> config.OAuth2TokenUrl request)
        let! oauthCode = getQueryParam "code" req
        let! uaaRequest = createTokenRequest config.OAuth2ClientId config.OAuth2ClientSecret config.OAuth2RedirectUrl oauthCode
        let! uaaJwt = getUaaJwt uaaRequest
        let! uaaClaims = decodeUaaJwt uaaJwt.access_token
        // let! user = queryUserByName uaaClaims.UserName
        let! appJwt = encodeJwt config.JwtSecret uaaClaims.Expiration uaaClaims.UserId uaaClaims.UserName
        let result = { access_token = appJwt }          
        return result
    }
