// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open Types
open Util
open Chessie.ErrorHandling

module Validation = 

    type Request<'T> = 
        | Id of Id
        | Model of 'T

    type Validator<'T> =
      { ValidForCreate: 'T -> Async<Result<'T,Error>>
        ValidForUpdate: 'T -> Async<Result<'T,Error>>
        ValidForDelete: Id -> Async<Result<'T,Error>> }

    let query lookup param  = async {
        let! lookupResult = lookup param
        return 
            match lookupResult with
            | Ok(value,_) -> ok value
            | Bad(msgs) -> Bad msgs
    }
    let queryAndPassThrough lookup param model  = async {
        return 
            await (query lookup) param
            >>= fun _ -> ok model
    }
    let validateRelationshipExists data (request:Request<SupportRelationship>) =
        match request with
        | Id id -> query data.SupportRelationships.Get id
        | Model model -> queryAndPassThrough data.SupportRelationships.Get model.Id model
    
    let validateRelationshipDetails data relationship = async {
        let validateRelationshipUnitExists (m:SupportRelationship) = 
            queryAndPassThrough data.Units.Get m.UnitId m 
        let validateRelationshipDepartmentExists (m:SupportRelationship) = 
            queryAndPassThrough data.Departments.Get m.DepartmentId m 
        let validateRelationshipIsUnique (m:SupportRelationship) = async {
            let conflict (mx:SupportRelationship) = 
                m.Id <> mx.Id 
                && m.UnitId = mx.UnitId 
                && m.DepartmentId = mx.DepartmentId
            let assertUniqueness relationships = 
                if relationships |> Seq.exists conflict
                then fail (Status.Conflict, "This unit already has a support relationship with this department.")
                else ok m
            return
                await data.SupportRelationships.GetAll ()
                    >>= assertUniqueness
        }
        return
            relationship
            |> await validateRelationshipUnitExists
            >>= await validateRelationshipDepartmentExists
            >>= await validateRelationshipIsUnique
    }

    let supportRelationshipValidator data : Validator<SupportRelationship> = 
        let validForCreate = validateRelationshipDetails data
        let validForUpdate model = async {
            return 
                await (validateRelationshipExists data) (Model(model))
                >>= await (validateRelationshipDetails data) 
        }
        let validForDelete id = validateRelationshipExists data (Id(id))

        { ValidForCreate = validForUpdate
          ValidForUpdate = validForUpdate
          ValidForDelete = validForDelete }