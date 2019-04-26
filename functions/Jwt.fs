// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open Core.Types
open Core.Util
open System
open System.Collections.Generic
open System.Net.Http
open JWT
open JWT.Algorithms
open JWT.Builder

module Jwt =

    /// Generate a form url-encoded request to exchange the code for a JWT.
    let createUaaTokenRequest (appConfig:AppConfig) code =
        dict[
            "grant_type", "authorization_code"
            "code", code
            "client_id", appConfig.OAuth2ClientId
            "client_secret", appConfig.OAuth2ClientSecret
            "redirect_uri", appConfig.OAuth2RedirectUrl
        ]
        |> Dictionary
        |> (fun d-> new FormUrlEncodedContent(d))
        |> Ok
        |> async.Return

    let ExpClaim = "exp"
    let UserIdClaim = "user_id"
    let UserNameClaim = "user_name"
    let epoch = DateTime(1970,1,1,0,0,0,0,System.DateTimeKind.Utc)

    /// Create and sign a JWT
    let encodeAppJwt secret expiration (netId: string, userId: int option) = 
        try
            let builder = 
                JwtBuilder()
                    .WithAlgorithm(HMACSHA256Algorithm())
                    .WithSecret(secret)
                    .ExpirationTime(expiration)
                    .AddClaim(UserNameClaim, netId)
            if (userId.IsSome)
            then builder.AddClaim(UserIdClaim, userId.Value) |> ignore
            { access_token = builder.Build() } |> Ok |> async.Return
        with _ -> Error (Status.InternalServerError, "Failed to create access token.") |> async.Return

    /// Convert the "exp" unix timestamp into a Datetime
    let decodeExp exp = 
        exp 
        |> string 
        |> System.Double.Parse 
        |> (fun unixTicks -> epoch.AddSeconds(unixTicks))

    /// Decode a JWT from the UAA service
    let decodeUaaJwt (jwt:JwtResponse) = 
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
            Ok claims |> async.Return
        with 
        | :? TokenExpiredException as ex -> 
            Error (Status.Unauthorized, "Access token has expired") |> async.Return
        | exn ->
            Error (Status.Unauthorized, sprintf "Failed to decode access token: %s" (exn.Message)) |> async.Return

    /// Decode a JWT issued by the Api.Auth.get function.
    let decodeAppJwt secret jwt =
        let result = 
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
                Ok claims
            with 
            | :? TokenExpiredException as ex -> 
                Error (Status.Unauthorized, "Access token has expired")
            | :? SignatureVerificationException as ex -> 
                Error (Status.Unauthorized, "Access token has invalid signature")
            | exn ->
                Error (Status.Unauthorized, sprintf "Failed to decode access token: %s" (exn.Message))       
        async.Return result

    let MissingAuthHeader = "Authorization header is required in the form of 'Bearer <token>'."

    /// Attempt to parse the Authorization header from the request
    let extractAuthHeader (req: HttpRequestMessage) =
        let authHeader = 
            if req.Headers.Contains("Authorization")
            then string req.Headers.Authorization
            else String.Empty
        let result = 
            if (isEmpty authHeader || authHeader.StartsWith("Bearer ") = false)
            then Error (Status.Unauthorized, MissingAuthHeader)
            else authHeader |> Ok
        async.Return result        

    /// Attempt to parse the JWT from the Authorization header. 
    let extractJwt (authHeader: string) =
        let parts = authHeader.Split([|' '|])
        let result = 
            if parts.Length <> 2 
            then Error (Status.Unauthorized, MissingAuthHeader)
            else parts.[1] |> Ok
        async.Return result 
               
    /// Attempt to decode the app JWT claims
    let authenticateRequest (config:AppConfig) = 
        extractAuthHeader 
        >=> extractJwt
        >=> decodeAppJwt config.JwtSecret