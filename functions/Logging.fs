// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

module Logging =

    open Core.Types

    open System
    open System.Net.Http

    open Serilog
    open Serilog.Core
    open Serilog.Sinks.PostgreSQL
    open NpgsqlTypes

    let private loggingColumns = 
      [ "timestamp", TimestampColumnWriter(NpgsqlDbType.Timestamp) :> ColumnWriterBase
        "level", LevelColumnWriter(true, NpgsqlDbType.Varchar) :> ColumnWriterBase
        "elapsed", SinglePropertyColumnWriter("Elapsed", PropertyWriteMethod.Raw, NpgsqlDbType.Integer) :> ColumnWriterBase 
        "status", SinglePropertyColumnWriter("Status", PropertyWriteMethod.Raw, NpgsqlDbType.Integer) :> ColumnWriterBase
        "method", SinglePropertyColumnWriter("Method", PropertyWriteMethod.Raw, NpgsqlDbType.Text) :> ColumnWriterBase
        "function", SinglePropertyColumnWriter("Function", PropertyWriteMethod.Raw, NpgsqlDbType.Text) :> ColumnWriterBase
        "parameters", SinglePropertyColumnWriter("Parameters", PropertyWriteMethod.Raw, NpgsqlDbType.Text) :> ColumnWriterBase
        "query", SinglePropertyColumnWriter("Query", PropertyWriteMethod.Raw, NpgsqlDbType.Text) :> ColumnWriterBase
        "detail", SinglePropertyColumnWriter("Detail", PropertyWriteMethod.Raw, NpgsqlDbType.Text) :> ColumnWriterBase
        "content", SinglePropertyColumnWriter("Content", PropertyWriteMethod.Raw, NpgsqlDbType.Text) :> ColumnWriterBase
        "ip_address", SinglePropertyColumnWriter("IPAddress", PropertyWriteMethod.Raw, NpgsqlDbType.Text) :> ColumnWriterBase
        "netid", SinglePropertyColumnWriter("NetId", PropertyWriteMethod.Raw, NpgsqlDbType.Text) :> ColumnWriterBase
        "exception", ExceptionColumnWriter(NpgsqlDbType.Text) :> ColumnWriterBase ]

    let private splitPath (req:HttpRequestMessage) =
        req.RequestUri.AbsolutePath.Split("/", StringSplitOptions.RemoveEmptyEntries)

    let private funcName req =
        req
        |> splitPath
        |> Seq.head
    
    let private funcParams req =
        req
        |> splitPath
        |> Seq.skip 1
        |> String.concat "/"
    
    let private query (req:HttpRequestMessage) =
        req.RequestUri.Query

    let tryGetHeaderValue (req:HttpRequestMessage) name = 
        if req.Headers.Contains(name) 
        then req.Headers.GetValues(name) |> String.concat "; " |> Some
        else None

    let tryGetIPAddress (req:HttpRequestMessage) = 
        match tryGetHeaderValue req "X-Cluster-Client-Ip" with
        | Some v -> v
        | None -> 
            match tryGetHeaderValue req "X-Forwarded-For" with
            | Some v -> v
            | None -> 
                match tryGetHeaderValue req "REMOTE_ADDR" with
                | Some v -> v
                | None -> ""         

    let tryGetElapsedTime (req:HttpRequestMessage) = 
        if req.Properties.ContainsKey(WorkflowTimestamp)
        then 
            let started = req.Properties.[WorkflowTimestamp] :?> DateTime
            (DateTime.UtcNow - started).TotalMilliseconds |> int
        else -1

    let tryGetAuthenticatedUser (req:HttpRequestMessage) =
        if req.Properties.ContainsKey(WorkflowUser)
        then req.Properties.[WorkflowUser] :?> string
        else ""

    let tryGetContent (req:HttpRequestMessage) = async {
        let! content = req.Content.ReadAsStringAsync() |> Async.AwaitTask
        return if content |> String.IsNullOrWhiteSpace  then "(none)" else content
    }

    let createLogger dbConnectionString =
        Serilog.Debugging.SelfLog.Enable(Console.Out);
        Serilog.LoggerConfiguration()
            .Enrich.WithDemystifiedStackTraces()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.PostgreSQL(dbConnectionString, "logs", (dict loggingColumns))
            .WriteTo.ApplicationInsightsTraces(System.Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY"))
            .CreateLogger()

    let logSuccess (log:Logger) req (status:Status) = async {
        let! content = req |> tryGetContent
        log.Information(
            "{IPAddress} {NetId} {Method} {Function}/{Parameters}{Query} finished in {Elapsed} ms with status {Status}. Content: {Content}", 
            req |> tryGetIPAddress, 
            req |> tryGetAuthenticatedUser,
            req.Method, 
            req |> funcName, 
            req |> funcParams, 
            req |> query, 
            req |> tryGetElapsedTime, 
            int status,
            content)
    }

    let logError (log:Logger) req (status:Status) errors = async {
        let! content = req |> tryGetContent
        log.Error(
            "{IPAddress} {NetId} {Method} {Function}/{Parameters}{Query} errored in {Elapsed} ms with status {Status}. Errors: {Detail}. Content: {Content}", 
            req |> tryGetIPAddress, 
            req |> tryGetAuthenticatedUser,
            req.Method, 
            req |> funcName, 
            req |> funcParams, 
            req |> query, 
            req |> tryGetElapsedTime, 
            int status, 
            errors,
            content)
    }

    let logFatal (log:Logger) (req:HttpRequestMessage) (exn:Exception) = async {
        let! content = req |> tryGetContent
        log.Fatal(
            exn,
            "{IPAddress} {NetId} {Method} {Function}/{Parameters}{Query} threw an exception after {Elapsed} ms with status {Status}. {Detail}. Content: {Content}", 
            req |> tryGetIPAddress, 
            req |> tryGetAuthenticatedUser,
            req.Method, 
            req |> funcName, 
            req |> funcParams, 
            req |> query, 
            req |> tryGetElapsedTime, 
            int Status.InternalServerError, 
            "See exception for details",
            content)
    }
