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
    let inline assertUnique lookup conflictPredicate msg model = async { 
        let assertUniqueness models = 
            if models |> Seq.exists conflictPredicate
            then fail (Status.Conflict, msg)
            else ok model
        return 
            await' lookup 
            >>= assertUniqueness
    }

    let inline validateEntityExists getOne request = 
        match request with
        | Id id -> query getOne id
        | Model model -> queryAndPassThrough getOne (identity model) model

    let inline createValidator data getOne detailsValidationFn = 
        let validForCreate = detailsValidationFn data
        let validForUpdate model = async {
            return 
                await (validateEntityExists getOne) (Model(model))
                >>= await (detailsValidationFn data) 
        }
        let validForDelete id = validateEntityExists getOne (Id(id))

        { ValidForCreate = validForCreate
          ValidForUpdate = validForUpdate
          ValidForDelete = validForDelete }


    // Unit Membership Validation

    let validateMembershipUnitExists data (m:UnitMember) = 
        queryAndPassThrough data.Units.Get m.UnitId m 
    let validateMembershipPersonExists data (m:UnitMember) = 
        match m.PersonId with 
        | Some(id) -> queryAndPassThrough data.People.Get id m 
        | None -> ok m |> async.Return
    let validateMembershipIsUnique data (m:UnitMember) = 
        let entities = data.People.GetMemberships
        let conflictPredicate (mx:UnitMember) = 
            m.Id <> mx.UnitId 
            && m.UnitId = mx.UnitId 
            && m.PersonId = mx.PersonId
        match m.PersonId with
        | None -> ok m |> async.Return
        | Some(id) -> assertUnique (entities id) conflictPredicate "This person already belongs to this unit." m
    let validateMembershipDetails data membership = async {
        return
            await (validateMembershipUnitExists data) membership
            >>= await (validateMembershipPersonExists data)
            >>= await (validateMembershipIsUnique data)
    }
    let membershipValidator data = 
        createValidator data data.Memberships.Get validateMembershipDetails


    // Support Relationship Validation

    let validateRelationshipUnitExists data (m:SupportRelationship) = 
        queryAndPassThrough data.Units.Get m.UnitId m 
    let validateRelationshipDepartmentExists data (m:SupportRelationship) = 
        queryAndPassThrough data.Departments.Get m.DepartmentId m 
    let validateRelationshipIsUnique data (m:SupportRelationship) =
        let entities = data.SupportRelationships.GetAll
        let conflictPredicate (mx:SupportRelationship) = 
            m.Id <> mx.Id 
            && m.UnitId = mx.UnitId 
            && m.DepartmentId = mx.DepartmentId
        m |> assertUnique (entities ()) conflictPredicate "This unit already has a support relationship with this department."

    let validateRelationshipDetails data relationship = async {
        return
            await (validateRelationshipUnitExists data) relationship
            >>= await (validateRelationshipDepartmentExists data)
            >>= await (validateRelationshipIsUnique data)
    }

    let supportRelationshipValidator data = 
        createValidator data data.SupportRelationships.Get validateRelationshipDetails
