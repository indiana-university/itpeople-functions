namespace MyFunctions.Common

open Types
open Util
open System
open System.Collections.Generic
open System.Net.Http
open Chessie.ErrorHandling
open JWT
open JWT.Algorithms
open JWT.Builder

module Jwt =

    let ExpClaim = "exp"
    let UserIdClaim = "user_id"
    let UserNameClaim = "user_name"
    let UserRoleClaim = "user_role"
    let epoch = DateTime(1970,1,1,0,0,0,0,System.DateTimeKind.Utc)

    // Create and sign a JWT
    let encodeJwt secret exp id netId = 
        let fn() =
            JwtBuilder()
                .WithAlgorithm(new HMACSHA256Algorithm())
                .WithSecret(secret)
                .ExpirationTime(exp)
                .AddClaim(UserIdClaim, (id.ToString()))
                .AddClaim(UserNameClaim, netId)
                // .AddClaim(UserRoleClaim, (role.ToString()))
                .Build();
        tryf' Status.InternalServerError "Failed to create access token" fn

    /// Convert the "exp" unix timestamp into a Datetime
    let decodeExp (exp:obj) = 
        exp 
        |> string 
        |> System.Double.Parse 
        |> (fun unixTicks -> epoch.AddSeconds(unixTicks))

    /// <summary>
    /// Decode a JWT from the UAA service
    /// </summary>
    let decodeUaaJwt (jwt:string) = 
        try
            let decoded = 
                JwtBuilder()
                    .Decode<IDictionary<string, obj>>(jwt)
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

    /// <summary>
    /// Decode a JWT issued by the /api/auth function.
    /// </summary>
    let decodeAppJwt (secret:string) (jwt:string) =
        try
            let decoded = 
                JwtBuilder()
                    .WithSecret(secret)
                    .MustVerifySignature()
                    .Decode<IDictionary<string, string>>(jwt)
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

    let extractAuthHeader (req: HttpRequestMessage) =
        let authHeader = 
            if req.Headers.Contains("Authorization")
            then string req.Headers.Authorization
            else String.Empty
        if (isEmpty authHeader || authHeader.StartsWith("Bearer ") = false)
        then fail (Status.Unauthorized, MissingAuthHeader)
        else authHeader |> ok

    let extractJwt (authHeader: string) =
        let parts = authHeader.Split([|' '|])
        if parts.Length <> 2 
        then fail (Status.Unauthorized, MissingAuthHeader)
        else parts.[1] |> ok

    let validateAuth (secret:string) (req: HttpRequestMessage) = trial {
        let! authHeader = extractAuthHeader req
        let! jwt = extractJwt authHeader
        let! claims = decodeAppJwt secret jwt
        return claims
    }
    
    let requireMembership (config:AppConfig) (req: HttpRequestMessage) = trial {
        let! claims = validateAuth config.JwtSecret req
        return claims
    }
