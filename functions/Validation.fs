// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

open Types
open Util

module Validation = 

    type Request<'T> = 
        | Id of Id
        | Model of 'T

    type Validator<'T> =
      { ValidForCreate: 'T -> Async<Result<'T,Error>>
        ValidForUpdate: 'T -> Async<Result<'T,Error>>
        ValidForDelete:Id -> Async<Result<'T,Error>>
        ValidEntity: Id -> Async<Result<'T,Error>> }

    let assertRelationExists lookup param model  = async {
        let! result = lookup param
        return 
            match result with 
            | Ok(_) -> Ok model        
            | Error((Status.NotFound, msg)) -> Error (Status.BadRequest, msg)
            | Error(msg) -> Error(msg)
    }

    let inline assertUnique lookup conflictPredicate msg model = async { 
        let! models = lookup ()
        return 
            match models with 
            | Ok models ->
                if models |> Seq.exists conflictPredicate
                then Error (Status.Conflict, msg)
                else Ok model
            | Error msg -> Error msg
    }

    let inline createValidator data getOne writeValidationPipeline deleteValidationPipeline = 
        let validForUpdate model = async {
            return 
                await getOne (identity model)
                >>= fun _ -> await (writeValidationPipeline data) model
        }
        let validForDelete id = async {
            return 
                await getOne id
                >>= await (deleteValidationPipeline data)
        }

        { ValidForCreate = writeValidationPipeline data
          ValidForUpdate = validForUpdate
          ValidForDelete = validForDelete
          ValidEntity = getOne }


    // Unit Membership Validation

    let membershipUnitExists data m = 
        assertRelationExists data.Units.Get (unitId m) m 
    let membershipPersonExists data (m:UnitMember) = 
        match m.PersonId with 
        | Some(id) -> assertRelationExists data.People.Get id m 
        | None -> Ok m |> async.Return
    let membershipIsUnique data (m:UnitMember) = 
        let entities id = fun () -> data.People.GetMemberships id
        let conflictPredicate mx = 
            (m.Id = 0 || m.Id <> mx.Id)
            && m.UnitId = mx.UnitId 
            && m.PersonId = mx.PersonId
        let msg = "This person already belongs to this unit."
        match m.PersonId with
        | None -> Ok m |> async.Return
        | Some(id) -> 
            m |> assertUnique (entities id) conflictPredicate msg
    let membershipWriteValidationPipeline data membership = async {
        return
            await (membershipUnitExists data) membership
            >>= await (membershipPersonExists data)
            >>= await (membershipIsUnique data)
    }
    let membershipDeleteValidationPipeline _ membership = async {
        return Ok membership
    }
    let membershipValidator data = 
        createValidator data data.Memberships.Get membershipWriteValidationPipeline membershipDeleteValidationPipeline


    // Support Relationship Validation

    let inline relationshipUnitExists data m = 
        assertRelationExists data.Units.Get (unitId m) m 
    let inline relationshipDepartmentExists data m = 
        assertRelationExists data.Departments.Get (departmentId m) m 
    let inline relationshipIsUnique data m =
        let entities = data.SupportRelationships.GetAll
        let conflictPredicate mx = 
            ((identity m) = 0 || (identity m) <> (identity mx))
            && (unitId m) = (unitId mx)
            && (departmentId m) = (departmentId mx)
        let msg = "This unit already has a support relationship with this department."
        assertUnique entities conflictPredicate msg m

    let relationshipWriteValidationPipeline repository = 
        relationshipUnitExists repository
        >=> relationshipDepartmentExists repository
        >=> relationshipIsUnique repository

    let relationshipDeleteValidationPipeline _ relationship = async {
        return Ok relationship
    }

    let inline supportRelationshipValidator data = 
        createValidator data data.SupportRelationships.Get relationshipWriteValidationPipeline relationshipDeleteValidationPipeline


    // Unit Validation

    let unitParentExists data (u:Unit) = 
        match u.ParentId with 
        | Some(id) -> assertRelationExists data.Units.Get id u 
        | None -> Ok u |> async.Return

    let unitNameIsUnique data id (model:Unit) =
        let entities () = data.Units.GetAll (Some(model.Name))
        let conflictPredicate (u:Unit) = 
            (id = 0 || id <> u.Id) 
            && (invariantEqual model.Name u.Name)            
        let msg = "Another unit already has that name."
        model |> assertUnique entities conflictPredicate msg
 
    let unitParentRelationshipIsNotCircular data (u:Unit) = async {
        let assertLinearDependency (child:Unit option) =    
            match (child) with
            | Some(c) -> 
                let error = sprintf "Whoops! %s is a parent of %s in the unit hierarcy. Adding it as a child would result in a circular relationship. 🙃" u.Name c.Name
                Error (Status.Conflict, error)
            | None -> Ok u

        match u.ParentId with
        | None -> return (Ok u)
        | Some(parentId) ->    
            return 
                parentId
                |> await (data.Units.GetDescendantOfParent u.Id) 
                >>= assertLinearDependency
    }

    let unitHasNoChildren data (u:Unit) = async {
        let! result = data.Units.GetChildren u
        return 
            match result with
            | Ok(children, _) -> 
                match children with
                | EmptySeq -> Ok u
                | _ -> Error (Status.Conflict, "This unit has children. They must be reassigned before this unit can be deleted.")
            | Error(msgs) -> Error msgs
    }

    let unitWriteValidationPipeline data model = async {
        return 
            model
            |> await (unitParentExists data)
            >>= await (unitNameIsUnique data model.Id)
            >>= await (unitParentRelationshipIsNotCircular data)
    }

    let unitDeleteValidationPipeline data model = async {
        return
            model
            |> await (unitHasNoChildren data)
    }

    let unitValidator data =
        createValidator data data.Units.Get unitWriteValidationPipeline unitDeleteValidationPipeline