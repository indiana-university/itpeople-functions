// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace ImportOrgData

open Types
open UitsUnitBuilder
open Database
open System
open System.IO
open System.Collections.Generic
open Argu

module Program = 



    // let connectionString = "User ID=root;Host=localhost;Port=5432;Database=circle_test;Pooling=true;"

    let tryImportPeople (args:ParseResults<CLIArguments>) connection =
        match args.TryGetResult(People) with
        | Some (hrCsv) ->
            Person.Load(hrCsv).Rows |> addOrUpdatePeople connection
        | None -> ()   

    let tryImportUITSUnits (args:ParseResults<CLIArguments>) connection =
        match args.TryGetResult(Uits) with
        | Some (uitsJson) -> 
            uitsJson 
            |> UitsUnitBuilder.build 
            |> addOrUpdateUnits connection
        | None -> ()

    let tryImportEdgeUnits (args:ParseResults<CLIArguments>) connection =
        match args.TryGetResult(Edge) with
        | Some (unitCsv, memberCsv) -> 
            (unitCsv, memberCsv) 
            |> EdgeUnitBuilder.build 
            |> addOrUpdateUnits connection
        | None -> ()

    let tryImportDepartments (args:ParseResults<CLIArguments>) connection =
        match args.TryGetResult(Dept) with
        | Some (deptCsv) -> 
            Dept.Load(deptCsv).Rows |> addOrUpdateDepartments connection
        | None -> ()

    let tryImportUnitDepartments (args:ParseResults<CLIArguments>) connection =
        match args.TryGetResult(UnitDept) with
        | Some (deptCsv) -> 
            UnitDept.Load(deptCsv).Rows |> addOrUpdateUnitDepts connection
        | None -> ()
    

    [<EntryPoint>]
    let main argv =
        try
            let parser = ArgumentParser.Create<CLIArguments>(programName = "program.exe")
            let args = parser.Parse argv
            let connection = args.GetResult(Connection)

            tryImportDepartments args connection
            tryImportPeople args connection
            tryImportUITSUnits args connection
            tryImportEdgeUnits args connection
            tryImportUnitDepartments args connection

            0
         with 
         | exn -> 
            printfn "Error occurred: %s" exn.Message
            -1

