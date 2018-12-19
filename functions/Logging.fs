// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

module Logging =

    open Types

    open System
    open System.Net.Http

    open Serilog
    open Serilog.Core
    open Serilog.Context
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

    let createLogger (config:AppConfig) =
        Serilog.Debugging.SelfLog.Enable(Console.Out);
        Serilog.LoggerConfiguration()
            .Enrich.WithDemystifiedStackTraces()
            .WriteTo.Console()
            .WriteTo.PostgreSQL(config.DbConnectionString, "logs", (dict loggingColumns))
            .WriteTo.ApplicationInsightsTraces(System.Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY"))
            .CreateLogger()

    let logInfo (log:Logger) req msg =
        log.Information(
            "{IPAddress} {NetId} {Method} {Function}/{Parameters}{Query}: {Detail}.", 
            req |> tryGetIPAddress, 
            req |> tryGetAuthenticatedUser,
            req.Method, 
            req |> funcName, 
            req |> funcParams, 
            req |> query, 
            msg)

    let logSuccess (log:Logger) req (status:Status) =
        log.Information(
            "{IPAddress} {NetId} {Method} {Function}/{Parameters}{Query} finished in {Elapsed} ms with status {Status}.", 
            req |> tryGetIPAddress, 
            req |> tryGetAuthenticatedUser,
            req.Method, 
            req |> funcName, 
            req |> funcParams, 
            req |> query, 
            req |> tryGetElapsedTime, 
            int status)

    let logError (log:Logger) req (status:Status) errors =
        log.Error(
            "{IPAddress} {NetId} {Method} {Function}/{Parameters}{Query} errored in {Elapsed} ms with status {Status}. Errors: {Detail}", 
            req |> tryGetIPAddress, 
            req |> tryGetAuthenticatedUser,
            req.Method, 
            req |> funcName, 
            req |> funcParams, 
            req |> query, 
            req |> tryGetElapsedTime, 
            int status, 
            errors)

    let logFatal (log:Logger) req (exn:Exception) =
        log.Fatal(
            exn,
            "{IPAddress} {NetId} {Method} {Function}/{Parameters}{Query} threw an exception after {Elapsed} ms with status {Status}. {Detail}", 
            req |> tryGetIPAddress, 
            req |> tryGetAuthenticatedUser,
            req.Method, 
            req |> funcName, 
            req |> funcParams, 
            req |> query, 
            req |> tryGetElapsedTime, 
            int Status.InternalServerError, 
            "See exception for details")
