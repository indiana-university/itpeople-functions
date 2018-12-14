// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

module Http =

    open Types
    open Util
    open System.Net
    open System.Net.Http
    open Chessie.ErrorHandling
    open Newtonsoft.Json
    open Microsoft.AspNetCore.WebUtilities

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
    let postAsync<'T> (url:string) (content:HttpContent) = async {
        try
            let! response = client.PostAsync(url, content) |> Async.AwaitTask
            let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            if (response.IsSuccessStatusCode)
            then return tryDeserialize<'T> Status.InternalServerError content
            else return fail (response.StatusCode, content)
        with 
        | exn -> return fail (Status.InternalServerError, exn.Message)
    }


