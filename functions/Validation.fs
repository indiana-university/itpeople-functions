// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open Core.Types
open Core.Util

module Validation = 

    // Unit Validation

    let assertUnitParentRelationshipIsNotCircular data unitId parentId = async {
        match parentId with
        | None -> return Ok ()
        | Some(pid) ->
            let! result = data.Units.GetDescendantOfParent (unitId, pid)
            match result with
            | Ok (Some(_)) -> return Error (Status.Conflict, "Whoops! Adding this unit as a child would result in a circular relationship. 🙃")
            | Ok (None) -> return Ok ()
            | Error msgs -> return Error msgs
    }

    let assertUnitHasNoChildren data unitId = async {
        let! result = data.Units.GetChildren unitId
        match result with
        | Ok (EmptySeq) -> return Ok () 
        | Ok (_) -> return Error (Status.Conflict, "This unit has children. They must be reassigned before this unit can be deleted.")
        | Error(msgs) -> return Error msgs
    }
