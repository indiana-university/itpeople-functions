// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace ImportOrgData

module Types =
    
    open Argu
    open FSharp.Data

    type Member = {
        Name: string
        Title: string
        Role: string
        Percentage: int
    }

    type Unit = {
        Name: string
        Url: string
        Members: seq<Member>
        Children: seq<Unit>
    }

    type Uits = JsonProvider<"samples/UitsDataSample.json">
    type EdgeUnit = CsvProvider<"samples/EdgeUnitsSample.csv">
    type EdgeMember = CsvProvider<"samples/EdgeMembersSample.csv">
    type Person = CsvProvider<"samples/PeopleSample.csv">
    type UnitDept = CsvProvider<"samples/UnitDeptSample.csv">
    type Dept = CsvProvider<"samples/DepartmentSample.csv">

    type CLIArguments =
        | [<Mandatory>]Connection of connection:string
        | [<Mandatory>]Uits of uitsJson:string
        | [<Mandatory>]Edge of unitCsv:string * memberCsv:string
        | [<Mandatory>]UnitDept of unitDeptCsv:string
        | [<Mandatory>]Dept of deptCsv:string
        | [<Mandatory>]People of hrCsv:string
    with
        interface IArgParserTemplate with
            member s.Usage =
                match s with
                | Uits _ -> "Import UITS unit data. Requires a path to a json file."
                | Edge _ -> "Import Edge unit data. Requires a path to a unit CSV file and a member CSV file."
                | People _ -> "Import People HR data. Requires a path to an HR CSV file."
                | UnitDept _ -> "Import unit/department relationships. Requires a path to an unit-dept CSV file."
                | Dept _ -> "Import department names. Requires a path to an dept CSV file."
                | Connection _ -> "(required) PostgresQL connection string"