// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open Core.Types
open Core.Util

module Validation = 

    // Unit Validation

    let assertUnitParentRelationshipIsNotCircular data (u:Unit) = async {
        let assertLinearDependency (child:Unit option) =    
            match (child) with
            | Some(c) -> 
                let error = sprintf "Whoops! %s is a parent of %s in the unit hierarcy. Adding it as a child would result in a circular relationship. 🙃" u.Name c.Name
                Error (Status.Conflict, error)
            | None -> Ok u
        match u.ParentId with
        | None -> return (Ok u)
        | Some(parentId) ->
            let! result = data.Units.GetDescendantOfParent (u.Id, parentId)
            match result with
            | Ok child -> return assertLinearDependency child
            | Error msgs -> return Error msgs
    }

    let assertUnitHasNoChildren data (u:Unit) = async {
        let! result = data.Units.GetChildren u
        return 
            match result with
            | Ok children -> 
                match children with
                | EmptySeq -> Ok u
                | _ -> Error (Status.Conflict, "This unit has children. They must be reassigned before this unit can be deleted.")
            | Error(msgs) -> Error msgs
    }
