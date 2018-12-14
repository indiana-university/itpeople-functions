// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

﻿namespace ImportOrgData

open Types
open OrgTree
open Database
open System
open System.IO
open System.Collections.Generic

module Program = 

    // let connectionString = "User ID=root;Host=localhost;Port=5432;Database=circle_test;Pooling=true;"
    
    let getJsonPath (args:string[]) =
        if args.Length < 1 then
            raise (Exception "Invalid arguments. Excepted input path as first argument")
        if not(File.Exists(args.[0])) then
            raise (Exception (sprintf "File does not exist: %s" args.[0]))
        args.[0]

    let getConnectionString (args:string[]) =
        if args.Length < 2 then
            raise (Exception "Invalid arguments. Excepted PSQL conneciton string as second argument")
        args.[1]
    
    [<EntryPoint>]
    let main argv =
        try
            let path = argv |> getJsonPath
            let connectionString = argv |> getConnectionString

            path
            |> buildOrgTree
            |> updateDatabase connectionString
            |> ignore
            
            0        
         with 
         | exn -> 
            printfn "Error occurred: %s" exn.Message
            -1

