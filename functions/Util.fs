// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

module Util =

    open System

    /// An active pattern to identify empty sequences
    let (|EmptySeq|_|) a = if Seq.isEmpty a then Some () else None

    /// An active pattern that tries to map a string to an int.
    let (|Int|_|) str =
        try
            let parsed = System.Int32.Parse str
            Some(parsed)
        with
        | exn -> None

    let invariantEqual (str:string) arg = 
        str.Equals(arg, System.StringComparison.InvariantCultureIgnoreCase)

    /// Checks whether the string is null or empty
    let isEmpty str = String.IsNullOrWhiteSpace str

    /// Given a list of tuples, check whether the first item
    /// of any element matches the provided predicate
    let any pred s = s |> Seq.exists (fun li -> fst li = pred)


    let now () = DateTime.UtcNow
