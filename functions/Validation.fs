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

    let inline validateEntityExists lookup request = 
        match request with
        | Id id -> 
            query lookup id
        | Model model -> 
            let id = (^T : (member Id:Id) model)
            queryAndPassThrough lookup id model

    let validateMembershipUnitExists data (m:UnitMember) = 
        queryAndPassThrough data.Units.Get m.UnitId m 
    let validateMembershipPersonExists data (m:UnitMember) = 
        match m.PersonId with 
        | Some(id) -> queryAndPassThrough data.People.Get id m 
        | None -> ok m |> async.Return
    let validateMembershipIsUnique data (m:UnitMember) = async {
        let conflict mx = 
            m.Id <> mx.Id 
            && m.UnitId = mx.UnitId 
            && m.PersonId = mx.PersonId
        let assertUniqueness memberships = 
            if memberships |> Seq.exists conflict
            then fail (Status.Conflict, "This person is already a member of this unit.")
            else ok m
        return
            match m.PersonId with
            | None -> ok m
            | Some(id) -> 
                await data.People.GetMemberships id
                >>= assertUniqueness
    }

    let validateMembershipDetails data membership = async {
        return
            membership
            |> await (validateMembershipUnitExists data)
            >>= await (validateMembershipPersonExists data)
            >>= await (validateMembershipIsUnique data)
    }

    let membershipValidator data : Validator<UnitMember> = 
        let validForCreate = validateMembershipDetails data
        let validForUpdate model = async {
            return 
                await (validateEntityExists data.Memberships.Get) (Model(model))
                >>= await (validateMembershipDetails data) 
        }
        let validForDelete id = validateEntityExists data.Memberships.Get (Id(id))

        { ValidForCreate = validForUpdate
          ValidForUpdate = validForUpdate
          ValidForDelete = validForDelete }

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
                await (validateEntityExists data.SupportRelationships.Get) (Model(model))
                >>= await (validateRelationshipDetails data) 
        }
        let validForDelete id = validateEntityExists data.SupportRelationships.Get (Id(id))

        { ValidForCreate = validForUpdate
          ValidForUpdate = validForUpdate
          ValidForDelete = validForDelete }