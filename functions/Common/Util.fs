namespace Functions.Common


///<summary>
/// This module contains common types and functions to facilitate request 
/// handling and response creation. 
///</summary>
module Util =
    open Types 
    open Json
    open Newtonsoft.Json
    open System
    open Chessie.ErrorHandling

    /// An active pattern to identify empty sequences
    let (|EmptySeq|_|) a = if Seq.isEmpty a then Some () else None

    /// An active pattern that tries to map a string to an int.
    let (|Int|_|) str =
        try
            let parsed = System.Int32.Parse str
            Some(parsed)
        with
        | exn -> None

    /// Checks whether the string is null or empty
    let isEmpty str = String.IsNullOrWhiteSpace str

    let optional str = 
        if isEmpty str 
        then None
        else Some(str)

    /// Get an empty sequence of type 'a
    let emptySeq<'a> () : AsyncResult<'a seq,Error> = asyncTrial {
        return Seq.empty<'a>
    }

    /// Given a list of tuples, check whether the first item
    /// of any element matches the provided predicate
    let any pred s = s |> Seq.exists (fun li -> fst li = pred)

    /// Get the first item in a sequence, or an error if none exists.
    let tryGetFirst seq msg = 
        match seq |> Seq.tryHead with
            | None -> fail (Status.NotFound, msg)
            | Some (x) -> ok x


    /// Given an async computation expression that returns a Result<TSuccess,TFailure>,
    /// bind and return the TSuccess.
    let bindAsyncResult<'T> (asyncFn: unit -> Async<Result<'T,(Status*string)>>) = asyncTrial {
        let! result = asyncFn()
        let! bound = result
        return bound
    }

    /// Given an async computation expression that returns a Result<TSuccess,TFailure>,
    /// bind and return the TSuccess.
    let bindAsync<'T> (asyncResult: Async<Result<'T,(Status*string)>>) = asyncTrial {
        let! result = asyncResult
        let! bound = result
        return bound
    }

    /// ROP: Attempt to execute a function.
    /// If it succeeds, pass along the result. 
    /// If it throws, wrap the exception message in a failure with the provided status.
    let tryf status fn = 
        try
            fn() |> ok
        with
        | exn -> fail (status, exn.Message)

    /// ROP: Attempt to execute a function.
    /// If it succeeds, pass along the result. 
    /// If it throws, wrap the exception message in a failure with the provided status.
    let tryf' status msg fn = 
        try
            fn() |> ok
        with
        | exn -> fail (status, sprintf "%s: %s" msg (exn.Message))

    /// ROP: Attempt to execute a function.
    /// If it succeeds, pass along the result. 
    /// If it throws, wrap the exception message in a failure with the provided status.
    let tryfAsync<'T> status msg (fn:unit -> Async<Result<'T,Error>>) = asyncTrial {
        let doAsync status msg fn = async {
          try
                let! result = fn()
                return result
            with
            | exn -> return fail (status, sprintf "%s: %s" msg (exn.Message))
        }
        
        let! result = doAsync status msg fn |> bindAsync
        return result
    }

    let mapFlagsToSeq<'T when 'T :> System.Enum> (value: 'T) = 
        JsonConvert.SerializeObject(value, JsonSettings).Trim('"')
        |> fun s -> s.Split([|','|])
        |> Seq.map (fun s -> s.Trim())
        |> Seq.map (fun s -> System.Enum.Parse(typeof<'T>,s) :?> 'T)
        |> Seq.filter (fun e -> e.ToString() <> "None")
