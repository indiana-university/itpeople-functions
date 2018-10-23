namespace MyFunctions.Common

open System
open Chessie.ErrorHandling

///<summary>
/// This module contains common types and functions to facilitate request 
/// handling and response creation. 
///</summary>
module Util =
    open Types 

    // STATIC 

    // UTILITY FUNCTIONS

    /// <summary>
    /// An active pattern to identify empty sequences
    /// </summary>
    let (|EmptySeq|_|) a = if Seq.isEmpty a then Some () else None

    let (|Int|_|) str =
       match System.Int32.TryParse(str) with
       | (true,int) -> Some(int)
       | _ -> None

    /// <summary>
    /// Checks whether the string is null or empty
    /// </summary>
    let isEmpty str = String.IsNullOrWhiteSpace str

    ///<summary>
    /// Given a list of tuples, check whether the first item
    /// of any element matches the provided predicate
    let any pred s = s |> Seq.exists (fun li -> fst li = pred)

   /// <summary>
    /// Given an async computation expression that returns a Result<TSuccess,TFailure>,
    /// bind and return the TSuccess.
    /// </summary>
    let bindAsyncResult<'T> (asyncFn: unit -> Async<Result<'T,(Status*string)>>) = asyncTrial {
        let! result = asyncFn()
        let! bound = result
        return bound
    }

    /// <summary>
    /// Given an async computation expression that returns a Result<TSuccess,TFailure>,
    /// bind and return the TSuccess.
    /// </summary>
    let bindAsync<'T> (asyncResult: Async<Result<'T,(Status*string)>>) = asyncTrial {
        let! result = asyncResult
        let! bound = result
        return bound
    }

    /// <summary>
    /// ROP: Attempt to execute a function.
    /// If it succeeds, pass along the result. 
    /// If it throws, wrap the exception message in a failure with the provided status.
    /// </summary>
    let tryf status fn = 
        try
            fn() |> ok
        with
        | exn -> fail (status, exn.Message)

    /// <summary>
    /// ROP: Attempt to execute a function.
    /// If it succeeds, pass along the result. 
    /// If it throws, wrap the exception message in a failure with the provided status.
    /// </summary>
    let tryf' status msg fn = 
        try
            fn() |> ok
        with
        | exn -> fail (status, sprintf "%s: %s" msg (exn.Message))

    /// <summary>
    /// ROP: Attempt to execute a function.
    /// If it succeeds, pass along the result. 
    /// If it throws, wrap the exception message in a failure with the provided status.
    /// </summary>
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



