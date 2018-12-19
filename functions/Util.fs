// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

module Util =

    open Types 
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

    /// Given a list of tuples, check whether the first item
    /// of any element matches the provided predicate
    let any pred s = s |> Seq.exists (fun li -> fst li = pred)

    // An awaiter for async computation expressions
    let await f x = 
        f x |> Async.RunSynchronously

    // An awaiter for async Tasks
    let awaitTask f  = 
        f |> Async.AwaitTask

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

    let now () = DateTime.UtcNow

    /// Apply a function f to the provided argument x,
    /// then return x unchanged.
    let inline tap f x =
        f x
        x

    /// ROP: Apply a function f to the provided argument x,
    /// then return x unchanged as a Success result.
    let inline tap' f x =
        f x
        ok x