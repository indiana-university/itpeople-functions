// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open Core.Types
open Core.Util
open System
open System.Collections.Generic
open System.Net.Http
open Jose
open Org.BouncyCastle.Crypto.Parameters;
open Org.BouncyCastle.OpenSsl;
open System.IO
open System.Security.Cryptography;

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

    let epoch = DateTime(1970,1,1,0,0,0,0,System.DateTimeKind.Utc)

    type UaaJwt = {
        exp: int64
        user_name: NetId
    }

    let ConvertRSAParametersField (n:Org.BouncyCastle.Math.BigInteger) (size:int) =
        let bs = n.ToByteArrayUnsigned()
        if (bs.Length = size)
        then bs
        else
            if (bs.Length > size)
            then ArgumentException("Specified size too small", "size") |> raise
            else         
                let padded = Array.create size (byte 0)
                Array.Copy(bs, 0, padded, size - bs.Length, bs.Length);
                padded

    let ToRSAParameters (rsaKey:RsaKeyParameters) =
        let mutable rp = RSAParameters()
        rp.Modulus <- rsaKey.Modulus.ToByteArrayUnsigned()
        if (rsaKey.IsPrivate)
        then rp.D <- ConvertRSAParametersField rsaKey.Exponent rp.Modulus.Length
        else rp.Exponent <- rsaKey.Exponent.ToByteArrayUnsigned()
        rp

    let importPublicKey pem =
        let pr =  PemReader(new StringReader(pem));
        let publicKey = pr.ReadObject() :?> Org.BouncyCastle.Crypto.AsymmetricKeyParameter
        let rsaParams = publicKey :?> RsaKeyParameters |> ToRSAParameters
        let csp = new RSACryptoServiceProvider();// cspParams);
        csp.ImportParameters(rsaParams);
        csp


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

    let validateSignature uaaPublicKey jwt =
        try 
            let rsa = importPublicKey uaaPublicKey
            Jose.JWT.Decode<UaaJwt>(jwt, rsa, JwsAlgorithm.RS256) |> ok
        with _ -> 
            error (Status.Unauthorized, "Access token is not valid.")

    let ensureJwtNotExpired uaa =
        let expiration = uaa.exp |> float |> epoch.AddSeconds
        if (expiration < DateTime.UtcNow)
        then error(Status.Unauthorized, "Access token has expired.")
        else ok uaa.user_name

    /// Decode a JWT issued by the Api.Auth.get function.
    let decodeJwt uaaPublicKey jwt = pipeline {
        let! validatedJwt = validateSignature uaaPublicKey jwt
        return! ensureJwtNotExpired validatedJwt
    }
               
    /// Attempt to decode the app JWT claims
    let authenticateRequest uaaPublicKey req = pipeline { 
        let! authHeader = extractAuthHeader req
        let! jwt = extractJwt authHeader
        return! decodeJwt uaaPublicKey jwt
    }