module Core.Util

open System
open System.Net.Http

open Types
open Json

/// An active pattern to identify empty sequences
let (|EmptySeq|_|) a = if Seq.isEmpty a then Some () else None

let invariantEqual (str:string) arg = 
    str.Equals(arg, System.StringComparison.InvariantCultureIgnoreCase)

/// Checks whether the string is null or empty
let isEmpty str = String.IsNullOrWhiteSpace str

let trim (str:string) = str.Trim()
let now () = DateTime.UtcNow

let client = new HttpClient()

/// Attempt to post an HTTP request and deserialize the ressponse
let getResponse<'T> (responseFn:System.Threading.Tasks.Task<HttpResponseMessage>) = async {
    try
        let! response = responseFn |> Async.AwaitTask
        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        if (response.IsSuccessStatusCode)
        then return tryDeserialize<'T> Status.InternalServerError content
        else return Error (response.StatusCode, content)
    with exn -> return Error (Status.InternalServerError, exn.Message)
}

let postAsync<'T> (url:string) (content:HttpContent) =
    client.PostAsync(url, content) |> getResponse<'T> 

let getAsync<'T> (url:string) = 
    client.GetAsync(url) |> getResponse<'T> 

let sendAsync<'T> (msg:HttpRequestMessage) = 
    client.SendAsync(msg) |> getResponse<'T> 