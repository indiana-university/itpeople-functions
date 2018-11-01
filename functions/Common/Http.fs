namespace MyFunctions.Common

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
    let jsonSettings = JsonSerializerSettings(ContractResolver=CamelCasePropertyNamesContractResolver())
    jsonSettings.Converters.Add(Newtonsoft.Json.Converters.StringEnumConverter())

    /// Construct an HTTP response with JSON content
    let jsonResponse status model = 
        let content = 
            JsonConvert.SerializeObject(model, jsonSettings)
            |> (fun s -> new StringContent(s))
        let response = new HttpResponseMessage(status)
        response.Content <- content
        response.Content.Headers.ContentType <- "application/json" |> MediaTypeHeaderValue;
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
    let constructResponse (log:TraceWriter) trialResult : HttpResponseMessage =
        match trialResult with
        | Ok(result, _) -> 
            result |> jsonResponse Status.OK
        | Bad(msgs) -> 
            let (status, errors) = failure (msgs)
            sprintf "%A %O" status errors |> log.Error
            jsonResponse status errors
