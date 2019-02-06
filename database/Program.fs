// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Database
    
module Program =

    open Migration

    let usage () =             
        printf """Usage : dotnet database.dll '<conn>' <args>

  <conn>: the Postgres database connection string
  <args>: SimpleMigration args (try 'help')"""

    [<EntryPoint>]
    let main argv =
        match argv |> List.ofSeq with
        | connection :: args->
            migrate connection args
        | _ ->
            usage()
            1