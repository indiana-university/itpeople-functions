// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Database

open Core.Types
open Dapper
open Npgsql

module OptionHandler =

    type OptionHandler<'T> () =
        inherit SqlMapper.TypeHandler<'T option> ()

        override __.SetValue (param, value) =
            let valueOrNull =
                match value with
                | Some x -> box x
                | None   -> null

            param.Value <- valueOrNull

        override __.Parse value =
            if 
                System.Object.ReferenceEquals(value, null) || 
                value = box System.DBNull.Value
            then None
            else Some (value :?> 'T)


    let RegisterTypes () =
        SqlMapper.AddTypeHandler (OptionHandler<Id>())
        SqlMapper.AddTypeHandler (OptionHandler<UnitId>())
        SqlMapper.AddTypeHandler (OptionHandler<PersonId>())
        SqlMapper.AddTypeHandler (OptionHandler<DepartmentId>())
        SqlMapper.AddTypeHandler (OptionHandler<bool>())
        SqlMapper.AddTypeHandler (OptionHandler<byte>())
        SqlMapper.AddTypeHandler (OptionHandler<sbyte>())
        SqlMapper.AddTypeHandler (OptionHandler<int16>())
        SqlMapper.AddTypeHandler (OptionHandler<uint16>())
        SqlMapper.AddTypeHandler (OptionHandler<int32>())
        SqlMapper.AddTypeHandler (OptionHandler<uint32>())
        SqlMapper.AddTypeHandler (OptionHandler<int64>())
        SqlMapper.AddTypeHandler (OptionHandler<uint64>())
        SqlMapper.AddTypeHandler (OptionHandler<single>())
        SqlMapper.AddTypeHandler (OptionHandler<float>())
        SqlMapper.AddTypeHandler (OptionHandler<double>())
        SqlMapper.AddTypeHandler (OptionHandler<decimal>())
        SqlMapper.AddTypeHandler (OptionHandler<char>())
        SqlMapper.AddTypeHandler (OptionHandler<string>())
        SqlMapper.AddTypeHandler (OptionHandler<obj>())


module Command = 

    open System.Threading.Tasks
    
    type IdFilter = { Id: Id }
    type NetIdFilter = { NetId: NetId }
    type SearchFilter = { Query: string }

    type Cn = NpgsqlConnection
    type Sql = string
    type WhereClause = string

    type MapMany<'T> = Cn -> Task<seq<'T>>
    type MapOne<'T> = int -> MapMany<'T>

    type Filter =
        | Unfiltered
        | Param of obj
        | Where of WhereClause
        | WhereId of WhereClause * Id
        | WhereParam of WhereClause * obj

    let like = sprintf "%%%s%%"
    let where = sprintf "%s WHERE %s"

    let parseQueryAndParam sql filter = 
        match filter with
        | Unfiltered -> (sql, ():>obj)
        | Param param -> (sql, param)
        | Where clause -> ((where sql clause), ():>obj)
        | WhereId (clause,id)-> ((where sql clause+"=@Id"), {Id=id}:>obj)
        | WhereParam (clause,param)-> ((where sql clause), param)

    let (|Contains|_|) (substr:string) (str:string) = if str.Contains(substr) then Some str else None
    
    let handleDbExn name resource (exn:System.Exception) = 
        let (status, msg) = 
            match exn.Message with
            | Contains "violates foreign key constraint" _ ->
                let msg = sprintf "Could not %s %s because one or more related entities could not be found." name resource
                (Status.NotFound, msg)
            | Contains "violates unique constraint" _ ->
                let msg = sprintf "Could not %s %s because that %s already exists." name resource resource
                (Status.Conflict, msg)
            | _ ->
                let msg = sprintf "Database error on %s %s: %s" name resource exn.Message
                (Status.InternalServerError, msg)
        Error(status, msg)

    let fetchAll<'T> connStr (mapper:MapMany<'T>) = async {
        try
            use cn = new NpgsqlConnection(connStr)
            let! result = cn |> mapper |> Async.AwaitTask
            return Ok result
        with exn -> return handleDbExn "fetch+map all" (typedefof<'T>.Name) exn
    }

    let fetchOne<'T> connStr (mapper:MapOne<'T>) id  = async {
        try
            use cn = new NpgsqlConnection(connStr)
            let! result = mapper id cn |> Async.AwaitTask
            if Seq.isEmpty result 
            then return Error(Status.NotFound, sprintf "No %s was found with ID %d." (typedefof<'T>.Name) id)
            else return result |> Seq.head |> Ok
        with exn -> return handleDbExn "fetch+map one" (typedefof<'T>.Name) exn
    }

    let fetch<'T> connStr (queryFn:Cn -> Task<'T>)  = async {
        try
            use cn = new NpgsqlConnection(connStr)
            let! result = cn |> queryFn |> Async.AwaitTask
            return result |> Ok
        with exn -> return handleDbExn "fetch" (typedefof<'T>.Name) exn
    }

    let insertImpl<'T> connStr (obj:'T) = async {
        try
            use cn = new NpgsqlConnection(connStr)
            let! result = cn.InsertAsync<'T>(obj) |> Async.AwaitTask
            return Ok (result.GetValueOrDefault())
        with exn -> return handleDbExn "insert" (typedefof<'T>.Name) exn
    }

    let insert<'T> connStr writeParams =
        insertImpl<'T> connStr
        >=> fetchOne<'T> connStr writeParams

    let updateImpl<'T> connStr id (obj:^T) = async {
        try
            use cn = new NpgsqlConnection(connStr)
            let! _ = cn.UpdateAsync<'T>(obj) |> Async.AwaitTask
            return Ok id
        with exn -> return handleDbExn "update" (typedefof<'T>.Name) exn
    }

    let update<'T> connStr writeParams id  = 
        updateImpl<'T> connStr id
        >=> fetchOne<'T> connStr writeParams

    let delete<'T> connStr (id:int) = async {
        try
            use cn = new NpgsqlConnection(connStr)
            let! _ = cn.DeleteAsync<'T>(id) |> Async.AwaitTask
            return () |> Ok
        with exn -> return handleDbExn "update" (typedefof<'T>.Name) exn
    }

    let execute connStr sql parameters = async {
        try
            use cn = new NpgsqlConnection(connStr)
            let! _ = cn.ExecuteAsync(sql, parameters) |> Async.AwaitTask
            return () |> Ok
        with exn -> return handleDbExn "execute" "" exn
    }

    let executeRaw connStr (fn:Cn->unit) = async {
        try
            use cn = new NpgsqlConnection(connStr)
            fn cn
            return () |> Ok
        with exn -> return handleDbExn "executeRaw" "" exn
    }
    let takeExactlyOne<'T> (seq:seq<'T>) =
        match seq |> Seq.length with
        | 1 -> seq |> Seq.head |> ok
        | 0 -> error(Status.NotFound, sprintf "No %s was found with that identifier." (typedefof<'T>.Name))
        | _ -> error(Status.BadRequest, sprintf "More than one %s was found with that identifier." (typedefof<'T>.Name))


    let init() = 
        SimpleCRUD.SetDialect(SimpleCRUD.Dialect.PostgreSQL)
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores <- true
        OptionHandler.RegisterTypes()