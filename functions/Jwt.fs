// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open Types
open Util
open Logging
open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open Chessie.ErrorHandling
open JWT
open JWT.Algorithms
open JWT.Builder

module Jwt =

    /// Generate a form url-encoded request to exchange the code for a JWT.
    let createUaaTokenRequest (appConfig:AppConfig) code =
        let fn () = 
            dict[
                "grant_type", "authorization_code"
                "code", code
                "client_id", appConfig.OAuth2ClientId
                "client_secret", appConfig.OAuth2ClientSecret
                "redirect_uri", appConfig.OAuth2RedirectUrl
            ]
            |> Dictionary
            |> (fun d-> new FormUrlEncodedContent(d))
        tryf Status.InternalServerError fn

    let ExpClaim = "exp"
    let UserIdClaim = "user_id"
    let UserNameClaim = "user_name"
    let UserRoleClaim = "user_role"
    let epoch = DateTime(1970,1,1,0,0,0,0,System.DateTimeKind.Utc)

    /// Create and sign a JWT
    let encodeAppJwt secret expiration (netId, id) = 
        let fn() =
            let jwt = 
                JwtBuilder()
                    .WithAlgorithm(new HMACSHA256Algorithm())
                    .WithSecret(secret)
                    .ExpirationTime(expiration)
                    .AddClaim(UserIdClaim, (id.ToString()))
                    .AddClaim(UserNameClaim, netId)
                    // .AddClaim(UserRoleClaim, (role.ToString()))
                    .Build();
            { access_token = jwt }
        tryf' Status.InternalServerError "Failed to create access token" fn

    /// Convert the "exp" unix timestamp into a Datetime
    let decodeExp exp = 
        exp 
        |> string 
        |> System.Double.Parse 
        |> (fun unixTicks -> epoch.AddSeconds(unixTicks))

    /// Decode a JWT from the UAA service
    let decodeUaaJwt (jwt:UaaResponse) = 
        try
            // decode the UAA JWT
            let decoded = 
                JwtBuilder()
                    .Decode<IDictionary<string, obj>>(jwt.access_token)
            // map the claims to a domain object
            let claims = {
                UserId = 0
                UserName = decoded.[UserNameClaim] |> string
                Expiration = decoded.[ExpClaim] |> decodeExp
            }
            ok claims
        with 
        | :? TokenExpiredException as ex -> 
            fail (Status.Unauthorized, "Access token has expired")
        | exn ->
            fail (Status.Unauthorized, sprintf "Failed to decode access token: %s" (exn.Message))

    /// Decode a JWT issued by the Api.Auth.get function.
    let decodeAppJwt secret jwt =
        try
            // decode and validate the app JWT
            let decoded = 
                JwtBuilder()
                    .WithSecret(secret)
                    .MustVerifySignature()
                    .Decode<IDictionary<string, string>>(jwt)
            // map the claims to a domain object
            let claims = {
                UserId = decoded.[UserIdClaim] |> Int32.Parse
                UserName = decoded.[UserNameClaim] |> string
                Expiration = decoded.[ExpClaim] |> decodeExp
            }
            ok claims
        with 
        | :? TokenExpiredException as ex -> 
            fail (Status.Unauthorized, "Access token has expired")
        | :? SignatureVerificationException as ex -> 
            fail (Status.Unauthorized, "Access token has invalid signature")
        | exn ->
            fail (Status.Unauthorized, sprintf "Failed to decode access token: %s" (exn.Message))       

    let MissingAuthHeader = "Authorization header is required in the form of 'Bearer <token>'."

    /// Attempt to parse the Authorization header from the request
    let extractAuthHeader (req: HttpRequestMessage) =
        let authHeader = 
            if req.Headers.Contains("Authorization")
            then string req.Headers.Authorization
            else String.Empty
        if (isEmpty authHeader || authHeader.StartsWith("Bearer ") = false)
        then fail (Status.Unauthorized, MissingAuthHeader)
        else authHeader |> ok

    /// Attempt to parse the JWT from the Authorization header. 
    let extractJwt (authHeader: string) =
        let parts = authHeader.Split([|' '|])
        if parts.Length <> 2 
        then fail (Status.Unauthorized, MissingAuthHeader)
        else parts.[1] |> ok

    /// Attempt to decode the app JWT claims
    let validateAuth secret req = 
        extractAuthHeader req
        >>= extractJwt
        >>= decodeAppJwt secret
    

    let authenticateRequest (config:AppConfig) req = 
        validateAuth config.JwtSecret req
    
