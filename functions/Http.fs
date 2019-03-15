// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

module Http =

    open Types
    open Util
    open System.Net.Http
    open Newtonsoft.Json
    open Microsoft.AspNetCore.WebUtilities

    let client = new HttpClient()

    let tryDeserialize<'T> status str =
        try JsonConvert.DeserializeObject<'T>(str, Functions.Json.JsonSettings) |> Ok
        with exn -> Error (status, exn.Message)

    /// Attempt to deserialize the request body as an object of the given type.
    let deserializeBody<'T> (req:HttpRequestMessage) = async { 
        let! body = req.Content.ReadAsStringAsync() |> Async.AwaitTask 
        return 
            if isEmpty body
            then Error (Status.BadRequest, "Expected a request body but received nothing")
            else tryDeserialize<'T> Status.BadRequest body 
    }

    /// Attempt to retrieve a query parameter of the given name
    let tryQueryParam paramName (req: HttpRequestMessage) =
        let query = req.RequestUri.Query |> QueryHelpers.ParseQuery
        let param =
            if query.ContainsKey(paramName)
            then query.[paramName].ToString() |> Some
            else None
        param |> Ok |> async.Return

    /// Require a query parameter of the given name
    let queryParam paramName (req: HttpRequestMessage) =
        let query = req.RequestUri.Query |> QueryHelpers.ParseQuery
        if query.ContainsKey(paramName)
        then query.[paramName].ToString() |> Ok |> async.Return
        else Error (Status.BadRequest,  (sprintf "Query parameter '%s' is required." paramName)) |> async.Return
        
    /// Attempt to post an HTTP request and deserialize the ressponse
    let postAsync<'T> (url:string) (content:HttpContent) = async {
        try
            let! response = client.PostAsync(url, content) |> Async.AwaitTask
            let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            if (response.IsSuccessStatusCode)
            then return tryDeserialize<'T> Status.InternalServerError content
            else return Error (response.StatusCode, content)
        with 
        | exn -> return Error (Status.InternalServerError, exn.Message)
    }


