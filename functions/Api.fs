// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

module Functions.Api

open Core.Types
open Core.Json
open Logging
open Examples

open System.Net.Http
open System.Net.Http.Headers
open System.Reflection
open Microsoft.AspNetCore.WebUtilities
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Newtonsoft.Json

open Swashbuckle.AspNetCore.Swagger
open Swashbuckle.AspNetCore.Filters
open Swashbuckle.AspNetCore.AzureFunctions.Extensions


/// CONFIGURATION 

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
        SharedSecret = getRequiredValue<string> config "SharedSecret"
    }

/// HTTP REQUEST

/// Attempt to retrieve a query parameter of the given name
let tryQueryParam' (req: HttpRequestMessage) key =
    let query = req.RequestUri.Query |> QueryHelpers.ParseQuery
    if query.ContainsKey(key)
    then query.[key].ToString() |> Some
    else None

let tryQueryParam (req: HttpRequestMessage) key =
    key
    |> (tryQueryParam' req)
    |> ok

/// Require a query parameter of the given name
let queryParam paramName (req: HttpRequestMessage) =
    let query = req.RequestUri.Query |> QueryHelpers.ParseQuery
    if query.ContainsKey(paramName)
    then query.[paramName].ToString() |> Ok |> async.Return
    else Error (Status.BadRequest,  (sprintf "Query parameter '%s' is required." paramName)) |> async.Return
    

/// CORS

let addCORSHeader (res:HttpResponseMessage) (origin) (corsHosts) =
    match corsHosts with
    | null -> ()
    | "" -> ()
    | _ ->
        if corsHosts = "*" || corsHosts.Split(',') |> Seq.exists (fun c -> c = origin)
        then 
            res.Headers.Add("Access-Control-Allow-Origin", value=origin)
            res.Headers.Add("Access-Control-Allow-Headers", "origin, content-type, accept, authorization")
            res.Headers.Add("Access-Control-Allow-Credentials", "true")
            res.Headers.Add("Access-Control-Allow-Methods", "GET, PUT, POST, DELETE, HEAD")
        else ()

let addPermissionsHeader (req:HttpRequestMessage) (res:HttpResponseMessage) =
    if req.Properties.ContainsKey(WorkflowPermissions)
    then 
        let values = 
            req.Properties.[WorkflowPermissions] :?> List<UserPermissions>
            |> List.map (fun a -> a.ToString())
            |> String.concat ", "
        res.Headers.Add("Access-Control-Expose-Headers", "X-User-Permissions")
        res.Headers.Add("X-User-Permissions", values)
    
let origin (req:HttpRequestMessage) =
    if req.Headers.Contains("origin")
    then req.Headers.GetValues("origin") |> Seq.head
    else ""

/// HTTP RESPONSE

/// Given an API function, get a response.  
let optionsResponse req config  = 
    let origin = origin req
    let response = new HttpResponseMessage(Status.OK)
    addCORSHeader response origin config.CorsHosts
    response

let contentResponse req corsHosts status content = 
    let response = new HttpResponseMessage(status)
    response.Content <- content
    response.Content.Headers.ContentType <- MediaTypeHeaderValue "application/json"
    response.Content.Headers.ContentType.CharSet <- "utf-8"
    addCORSHeader response (origin req) corsHosts
    addPermissionsHeader req response
    response

/// Construct an HTTP response with JSON content
let jsonResponse req corsHosts status model = 
    JsonConvert.SerializeObject(model, JsonSettings)
    |> (fun s -> new StringContent(s))
    |> contentResponse req corsHosts status

/// Convert an ROP trial into an HTTP response. 
/// The result of a successful trial will be passed to the provided success function.
/// The result(s) of a failed trial will be aggregated, logged, and returned as a 
/// JSON error message with an appropriate status code.
let createResponse req config log status result = 
    match result with
    | Ok body ->
        logSuccess log req status
        jsonResponse req config.CorsHosts status body
    | Error (status,msg) -> 
        logError log req status msg
        jsonResponse req config.CorsHosts status msg

/// OpenAPI SPEC
let apiInfo = 
    Info(
        Title="IT People API",
        Version="v1",
        Description="IT People is the canonical source of information about the organization of IT units and people at Indiana University",
        Contact = Contact (Name="UITS DCD", Email="dcdreq@iu.edu"))

let generateOpenAPISpec () = 
    let services = ServiceCollection()
    let assembly = Assembly.GetExecutingAssembly()
    services.AddAzureFunctionsApiProvider(functionAssembly=assembly, routePrefix="")
    services
        .AddSwaggerGen((fun (options:Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions) -> 
            options.SwaggerDoc(name="v1", info=apiInfo)
            options.DescribeAllEnumsAsStrings()
            options.EnableAnnotations()
            options.ExampleFilters()
            options.TryIncludeFunctionXmlComments(assembly)
        ))
        .AddSwaggerExamplesFromAssemblyOf<UnitExample>(JsonSettings)
        .AddSwaggerExamples(JsonSettings)
        .BuildServiceProvider(true)
        .GetSwagger("v1")
