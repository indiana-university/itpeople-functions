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
        ValidEntity: Id -> Async<Result<'T,Error>> }

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
            await lookup () 
            >>= assertUniqueness
    }

    let inline validateEntityExists getOne request = 
        match request with
        | Id id -> query getOne id
        | Model model -> queryAndPassThrough getOne (identity model) model

    let inline createValidator data getOne validationPipeline = 
        let validForCreate = validationPipeline data
        let validForUpdate model = async {
            return 
                await (validateEntityExists getOne) (Model(model))
                >>= await (validationPipeline data) 
        }
        let validEntity id = validateEntityExists getOne (Id(id))

        { ValidForCreate = validForCreate
          ValidForUpdate = validForUpdate
          ValidEntity = validEntity }


    // Unit Membership Validation

    let membershipUnitExists data (m:UnitMember) = 
        queryAndPassThrough data.Units.Get m.UnitId m 
    let membershipPersonExists data (m:UnitMember) = 
        match m.PersonId with 
        | Some(id) -> queryAndPassThrough data.People.Get id m 
        | None -> ok m |> async.Return
    let membershipIsUnique data (m:UnitMember) = 
        let entities id = fun () -> data.People.GetMemberships id
        let conflictPredicate (mx:UnitMember) = 
            m.Id <> mx.UnitId 
            && m.UnitId = mx.UnitId 
            && m.PersonId = mx.PersonId
        let msg = "This person already belongs to this unit."
        match m.PersonId with
        | None -> ok m |> async.Return
        | Some(id) -> m |> assertUnique (entities id) conflictPredicate msg
    let membershipValidationPipeline data membership = async {
        return
            await (membershipUnitExists data) membership
            >>= await (membershipPersonExists data)
            >>= await (membershipIsUnique data)
    }
    let membershipValidator data = 
        createValidator data data.Memberships.Get membershipValidationPipeline


    // Support Relationship Validation

    let relationshipUnitExists data (m:SupportRelationship) = 
        queryAndPassThrough data.Units.Get m.UnitId m 
    let relationshipDepartmentExists data (m:SupportRelationship) = 
        queryAndPassThrough data.Departments.Get m.DepartmentId m 
    let relationshipIsUnique data (m:SupportRelationship) =
        let entities = data.SupportRelationships.GetAll
        let conflictPredicate (mx:SupportRelationship) = 
            m.Id <> mx.Id 
            && m.UnitId = mx.UnitId 
            && m.DepartmentId = mx.DepartmentId
        let msg = "This unit already has a support relationship with this department."
        m |> assertUnique entities conflictPredicate msg

    let relationshipValidationPipeline data relationship = async {
        return
            await (relationshipUnitExists data) relationship
            >>= await (relationshipDepartmentExists data)
            >>= await (relationshipIsUnique data)
    }

    let supportRelationshipValidator data = 
        createValidator data data.SupportRelationships.Get relationshipValidationPipeline


    // Unit Validation

    let unitParentExists data (u:Unit) = 
        match u.ParentId with 
        | Some(id) -> queryAndPassThrough data.Units.Get id u 
        | None -> ok u |> async.Return

    let unitNameIsUnique data (model:Unit) =
        let entities () = data.Units.GetAll (Some(model.Name))
        let conflictPredicate (u:Unit) = 
            (model.Id <> u.Id) 
            && (invariantEqual model.Name u.Name)            
        let msg = "Another unit already has that name."
        model |> assertUnique entities conflictPredicate msg
 
    let unitParentRelationshipIsNotCircular data (u:Unit) = async {
        let assertLinearDependency (child:Unit option) =    
            match (child) with
            | Some(c) -> 
                let error = sprintf "Whoops! %s is a parent of %s in the unit hierarcy. Adding it as a child would result in a circular relationship. 🙃" u.Name c.Name
                fail(Status.Conflict, error)
            | None -> ok u

        match u.ParentId with
        | None -> return (ok u)
        | Some(parentId) ->    
            return 
                parentId
                |> await (data.Units.GetDescendantOfParent u) 
                >>= assertLinearDependency
    }

    let unitValidationPipeline data (model:Unit) = async {
        return 
            model
            |> await (unitParentExists data)
            >>= await (unitNameIsUnique data)
            >>= await (unitParentRelationshipIsNotCircular data)
    }

    let unitValidator data =
        createValidator data data.Units.Get unitValidationPipeline