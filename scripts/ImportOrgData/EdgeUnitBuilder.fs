// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace ImportOrgData

open System
open FSharp.Data
open Types

module EdgeUnitBuilder = 
   
    let projectMember (mem:EdgeMember.Row) =
        { Name=mem.Username.ToLowerInvariant()
          Title=""
          Role=mem.Role
          Percentage=100 }

    let buildTree (units:EdgeUnit.Row seq) (members: EdgeMember.Row seq) =
        
        let rec buildTree' (unit:EdgeUnit.Row) =
            // find all the members of this unit
            let unitMembers =
                members
                |> Seq.filter (fun m -> m.Unit = unit.Unit)
                |> Seq.map projectMember
            // recurse to find all the nested children of this unit
            let children =
                units
                |> Seq.filter (fun r -> r.Parent = unit.Unit)
                |> Seq.map buildTree'
            // project the data to a Unit record.
            { Name=unit.Unit
              Url=""
              Members=unitMembers
              Children=children }

        // build a sequence of top-level units (those with no parents)
        units 
        |> Seq.filter (fun r -> String.IsNullOrWhiteSpace(r.Parent))
        |> Seq.map buildTree'


    /// From a JSON file at 'path', generate a UITS org heirarchy.
    let build (unitCsv:string, memberCsv:string) = 
        let units = EdgeUnit.Load(unitCsv)
        let members = EdgeMember.Load(memberCsv)
        buildTree units.Rows members.Rows
