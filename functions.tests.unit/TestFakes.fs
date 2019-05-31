// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Tests

open Core.Types
open System.Net.Http

module TestFakes =

    /// NOTE: You can view the contents of these tokens at jwt.io.
   
    // This token payload is: { "user_name":"johndoe", "user_id":1, "exp":1915544643 }
    let validJwt = "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJleHAiOiIxOTE1NTQ0NjQzIiwidXNlcl9uYW1lIjoiam9obmRvZSIsInVzZXJfaWQiOjF9.bCMuAfRby19tJHCOggz7KESMRxtPl_h7pLTQTx3ui4E"

    let jwtSingingSecret = "jwt signing secret"

    let requestWithValidJwt = 
        let req = new HttpRequestMessage()
        req.Headers.Add("Authorization", (sprintf "Bearer %s" validJwt))
        req

    let requestWithNoJwt = 
        let req = new HttpRequestMessage()
        req

    let appConfig = 
      { OAuth2ClientId="client id"
        OAuth2ClientSecret="client secret"
        OAuth2TokenUrl="token url"
        OAuth2RedirectUrl="redirect url"
        DbConnectionString="db connectionString"
        UseFakes=false
        CorsHosts="*" }