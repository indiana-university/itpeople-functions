// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

module Functions.Api

open Core.Types
open Core.Json
open Logging
open Examples

open System.Net.Http
open System.Net.Http.Headers
open System.Xml
open System.Xml.Serialization
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
        DbConnectionString = getRequiredValue<string> config "DbConnectionString"
        UseFakes = getValueOrDefault<bool> config "UseFakeData" false
        CorsHosts = getValueOrDefault<string> config "CorsHosts" "*"
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

let formatJson = "application/json"
let formatXml = "application/xml"

let contentResponse req corsHosts status content  = 
    let response = new HttpResponseMessage(status)
    response.Content <- content
    addCORSHeader response (origin req) corsHosts
    addPermissionsHeader req response
    response

let serializeJson model = 
    JsonConvert.SerializeObject(model, JsonSettings)

let stringContent format str  = 
    new StringContent(str, System.Text.Encoding.UTF8, format)

/// Construct an HTTP response with JSON content
let inline jsonResponse model = 
    model |> serializeJson |> stringContent formatJson

type Utf8StringWriter()=
    inherit System.IO.StringWriter()
    override __.Encoding = System.Text.Encoding.UTF8

let serializeXml<'a> (model: 'a) = 
    let serializer = XmlSerializer(typeof<'a>)
    use writer = new Utf8StringWriter()
    serializer.Serialize(writer, model)
    writer.ToString()

/// Construct an HTTP response with JSON content
let inline xmlResponse<'a> (model:'a) = 
    model |> serializeXml<'a> |> stringContent formatXml

let description = """## Description
IT People is the canonical source of information about people doing IT work at Indiana University, their responsibilities and interests, and the IT units to which they belong.

## Need Help?
If you need help using this API, please contact the [UITS DCD](mailto:dcdreq@iu.edu) team. 
The source code for this [API](https://github.com/indiana-university/itpeople-functions) and the [web front end](https://github.com/indiana-university/itpeople-app) are available on GitHub. We welcome pull requests! 
If you find a bug or would like to request a feature, please [file an issue in GitHub](https://github.com/indiana-university/itpeople-functions/issues).

## Request/Response Formats
All HTTP request and response bodies will be JSON formatted.

## Authentication
All requests to this API require an HTTP authentication header in the form `Authorization: Bearer TOKEN`, where the `TOKEN` is any valid JWT issued by the <a href="https://github.iu.edu/iu-uits-es/uaa">UITS UAA</a> service. 
This API will infer the identity of the requestor from the *user_name* property of the UAA JWT.  

All data query endpoints (i.e. `GET` endpoints) are publicly accessible with valid authentication.

All data modification endpoints (i.e. `POST`, `PUT`, `DELETE`) are authorized against the identity of caller, as identified in the JWT *user_name* property. See individual endpoints for authorization details.
"""

/// OpenAPI SPEC
let apiInfo = Info(Title="IT People API", Version="v1", Description=description)

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
