namespace Tests

open Chessie.ErrorHandling
open Functions.Common.Util
open Functions.Common.Types
open System
open Xunit
open System.Net.Http

module TestFakes =

    /// NOTE: You can view the contents of these tokens at jwt.io.
   
    // This token payload is: { "user_name":"johndoe", "user_id":1, "exp":1915544643 }
    let validJwt = "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJleHAiOiIxOTE1NTQ0NjQzIiwidXNlcl9pZCI6IjEiLCJ1c2VyX25hbWUiOiJqb2huZG9lIn0.9uerDlhPKrtBrMMHuRoxbJ5x0QA7KOulDEHx9DKXpnQ"

    let jwtSingingSecret = "jwt signing secret"

    let requestWithValidJwt = 
        let req = new HttpRequestMessage()
        req.Headers.Add("Authorization", (sprintf "Bearer %s" validJwt))
        req

    let requestWithNoJwt = 
        let req = new HttpRequestMessage()
        req

    let appConfig = {
        OAuth2ClientId="client id"
        OAuth2ClientSecret="client secret"
        OAuth2TokenUrl="token url"
        OAuth2RedirectUrl="redirect url"
        JwtSecret=jwtSingingSecret
        DbConnectionString="db connectionString"
        UseFakes=false
    }
