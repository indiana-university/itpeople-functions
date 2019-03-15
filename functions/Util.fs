// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

module Util =

    open System

    /// An active pattern to identify empty sequences
    let (|EmptySeq|_|) a = if Seq.isEmpty a then Some () else None

    let invariantEqual (str:string) arg = 
        str.Equals(arg, System.StringComparison.InvariantCultureIgnoreCase)

    /// Checks whether the string is null or empty
    let isEmpty str = String.IsNullOrWhiteSpace str

    let now () = DateTime.UtcNow
