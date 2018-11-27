namespace Functions.Common

open Types
open Util
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open Chessie.ErrorHandling
open Microsoft.AspNetCore.Http
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json
open Newtonsoft.Json.Serialization
open Microsoft.AspNetCore.WebUtilities
open Serilog.Core

module Json =

    open System
    open Microsoft.FSharp.Reflection
    open Newtonsoft.Json
    open Newtonsoft.Json.Converters

    type OptionConverter() =
        inherit JsonConverter()

        override x.CanConvert(t) = 
            t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

        override x.WriteJson(writer, value, serializer) =
            let value = 
                if value = null then null
                else 
                    let _,fields = FSharpValue.GetUnionFields(value, value.GetType())
                    fields.[0]  
            serializer.Serialize(writer, value)

        override x.ReadJson(reader, t, existingValue, serializer) =        
            let innerType = t.GetGenericArguments().[0]
            let innerType = 
                if innerType.IsValueType then (typedefof<Nullable<_>>).MakeGenericType([|innerType|])
                else innerType        
            let value = serializer.Deserialize(reader, innerType)
            let cases = FSharpType.GetUnionCases(t)
            if value = null then FSharpValue.MakeUnion(cases.[0], [||])
            else FSharpValue.MakeUnion(cases.[1], [|value|])

module Http =

    let client = new HttpClient()
    let tryDeserialize<'T> status str =
        tryf status (fun () -> str |> JsonConvert.DeserializeObject<'T>)

    /// Attempt to deserialize the request body as an object of the given type.
    let deserializeBody<'T> (req:HttpRequestMessage) = 
        let body = req.Content.ReadAsStringAsync().Result
        match body with
        | null -> fail (Status.BadRequest, "Expected a request body but received nothing")
        | ""   -> fail (Status.BadRequest, "Expected a request body but received nothing")
        | _    -> tryDeserialize<'T> Status.BadRequest body 

    /// Attempt to retrieve a parameter of the given name from the query string
    let getQueryParam paramName (req: HttpRequestMessage) =
        let query = req.RequestUri.Query |> QueryHelpers.ParseQuery
        if query.ContainsKey(paramName)
        then ok (query.[paramName].ToString())
        else fail (Status.BadRequest,  (sprintf "Query parameter '%s' is required." paramName))

    /// Attempt to post an HTTP request and deserialize the ressponse
    let postAsync<'T> (url:string) (content:HttpContent) : Async<Result<'T,(HttpStatusCode*string)>> = async {
        try
            let! response = client.PostAsync(url, content) |> Async.AwaitTask
            let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            if (response.IsSuccessStatusCode)
            then return tryDeserialize Status.InternalServerError content
            else return fail (response.StatusCode, content)
        with 
        | exn -> return fail (Status.InternalServerError, exn.Message)
    }

    /// JSON Serialization Defaults:
    /// 1. Format property names in 'camelCase'.
    /// 2. Convert all enum values to/from their string equivalents.
    /// 3. Format all options as null or the unwrapped type
    let jsonSettings = JsonSerializerSettings(ContractResolver=CamelCasePropertyNamesContractResolver())
    jsonSettings.Converters.Add(Newtonsoft.Json.Converters.StringEnumConverter())
    jsonSettings.Converters.Add(Json.OptionConverter())

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

    /// Construct an HTTP response with JSON content
    let jsonResponse reqHost corsHosts status model = 
        let content = 
            JsonConvert.SerializeObject(model, jsonSettings)
            |> (fun s -> new StringContent(s))
        let response = new HttpResponseMessage(status)
        response.Content <- content
        response.Content.Headers.ContentType <- "application/json" |> MediaTypeHeaderValue;
        addCORSHeader response reqHost corsHosts
        response

    let origin (req:HttpRequestMessage) =
        if req.Headers.Contains("origin")
        then req.Headers.GetValues("origin") |> Seq.head
        else ""

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
    let constructResponse (req:HttpRequestMessage) (log:Logger) (corsHosts:string) trialResult : HttpResponseMessage =
        let referrer = origin req
        let url = sprintf ("%A %s") req.Method (req.RequestUri.ToString())
        try
            match trialResult with
            | Ok(result, _) -> 
                sprintf "%s %O" url Status.OK |> log.Information
                result |> jsonResponse referrer corsHosts Status.OK
            | Bad(msgs) -> 
                let (status, errors) = failure (msgs)
                sprintf "%s %A %O" url status errors |> log.Error
                jsonResponse referrer corsHosts status errors
        with
        | exn -> 
            let msg = exn.ToString() |> sprintf "Unhandled exception when constructing response: %s"
            jsonResponse referrer corsHosts Status.InternalServerError msg
