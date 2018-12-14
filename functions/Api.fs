// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open Types
open Jwt
open Database
open Fakes
open Logging
open Util
open Http
open System.Collections.Generic
open System.Diagnostics
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs
open Microsoft.Extensions.Configuration
open Serilog.Core
open Newtonsoft.Json


module Api =

    ///
    /// CONFIGURATION 
    ///
    
    let getRequiredValue<'T> (config:IConfigurationRoot) key =
        if config.GetChildren() |> Seq.exists (fun c -> c.Key = key)
        then config.GetValue<'T>(key)
        else 
            let msg = sprintf "Configuration is missing required value: %s" key
            System.Console.WriteLine(sprintf "[FATAL] %s" msg)
            msg |> System.Exception |> raise

    let getValueOrDefault<'T> (config:IConfigurationRoot) key defaultValue =
        config.GetValue<'T>(key, defaultValue)

    let getConfiguration () =
        let config = 
            ConfigurationBuilder()
                .AddJsonFile("local.settings.json", optional=true)
                .AddKeyPerFile("/run/secrets", optional=true)
                .AddEnvironmentVariables()
                .Build();

        {
            OAuth2ClientId = getRequiredValue<string> config "OAuthClientId"
            OAuth2ClientSecret = getRequiredValue<string> config "OAuthClientSecret"
            OAuth2TokenUrl = getRequiredValue<string> config "OAuthTokenUrl"
            OAuth2RedirectUrl = getRequiredValue<string> config "OAuthRedirectUrl"
            JwtSecret = getRequiredValue<string> config "JwtSecret"
            DbConnectionString = getRequiredValue<string> config "DbConnectionString"
            UseFakes = getValueOrDefault<bool> config "UseFakeData" false
            CorsHosts = getValueOrDefault<string> config "CorsHosts" "*"
        }

    let getData config =
        if config.UseFakes
        then FakesRepository() :> IDataRepository
        else DatabaseRepository(config.DbConnectionString) :> IDataRepository

    ///
    /// CORS
    ///

    let addCORSHeader (res:HttpResponseMessage) (origin:string) (corsHosts:string) =
        match corsHosts with
        | null -> ()
        | "" -> ()
        | _ ->
            if corsHosts = "*" || corsHosts.Split(',') |> Seq.exists (fun c -> c = origin)
            then 
                res.Headers.Add("Access-Control-Allow-Origin", value=origin)
                res.Headers.Add("Access-Control-Allow-Headers", "origin, content-type, accept, authorization")
                res.Headers.Add("Access-Control-Allow-Credentials", "true")
            else ()

    let origin (req:HttpRequestMessage) =
        if req.Headers.Contains("origin")
        then req.Headers.GetValues("origin") |> Seq.head
        else ""

    ///
    /// HTTP RESPONSE
    ///

    /// Given an API function, get a response.  
    let optionsResponse (req: HttpRequestMessage) (config: AppConfig)  = 
            let origin = origin req
            let response = new HttpResponseMessage(Status.OK)
            addCORSHeader response origin config.CorsHosts
            response

    /// Construct an HTTP response with JSON content
    let jsonResponse req corsHosts status model = 
        let content = 
            JsonConvert.SerializeObject(model, Json.JsonSettings)
            |> (fun s -> new StringContent(s))
        let response = new HttpResponseMessage(status)
        response.Content <- content
        response.Content.Headers.ContentType <- "application/json" |> MediaTypeHeaderValue;
        addCORSHeader response (origin req) corsHosts
        response

    /// Organize the errors into a status code and a collection of error messages. 
    /// If multiple errors are found, the aggregate status will be that of the 
    /// most severe error (500, then 404, then 400, etc.)
    let failure msgs =
        let l = msgs |> Seq.toList
        // Determine the aggregate status code based on the most severe error.
        let statusCode = 
            if l |> any Status.InternalServerError then Status.InternalServerError
            elif l |> any Status.NotFound then Status.NotFound
            elif l |> any Status.BadRequest then Status.BadRequest
            else l.Head |> fst
        // Flatten all error messages into a single array.
        let errors = 
            l 
            |> Seq.map snd 
            |> Seq.toArray 
            |> (fun es -> { errors = es } )
        
        ( statusCode, errors )

    /// Convert an ROP trial into an HTTP response. 
    /// The result of a successful trial will be passed to the provided success function.
    /// The result(s) of a failed trial will be aggregated, logged, and returned as a 
    /// JSON error message with an appropriate status code.
    let createResponse (req:HttpRequestMessage) (config:AppConfig) (log:Logger) (timer:Stopwatch) result : HttpResponseMessage =
        match result with
        | Ok(result, _) -> 
            logInfo log req Status.OK timer.ElapsedMilliseconds
            jsonResponse req config.CorsHosts Status.OK result
        | Bad(msgs) -> 
            let (status, errors) = failure (msgs)
            logError log req status timer.ElapsedMilliseconds errors
            jsonResponse req config.CorsHosts status errors


