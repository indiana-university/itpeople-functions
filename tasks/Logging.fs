// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Tasks

module Logging =

    open Core.Json
    open Core.Types

    open System

    open Serilog
    open Serilog.Sinks.PostgreSQL
    open Serilog.Exceptions
    open NpgsqlTypes

    let private loggingColumns = 
      [ "timestamp", TimestampColumnWriter(NpgsqlDbType.Timestamp) :> ColumnWriterBase
        "level", LevelColumnWriter(true, NpgsqlDbType.Varchar) :> ColumnWriterBase
        "invocation_id", SinglePropertyColumnWriter("InvocationId", PropertyWriteMethod.Raw, NpgsqlDbType.Uuid) :> ColumnWriterBase
        "function_name", SinglePropertyColumnWriter("FunctionName", PropertyWriteMethod.Raw, NpgsqlDbType.Text) :> ColumnWriterBase
        "message", SinglePropertyColumnWriter("Message", PropertyWriteMethod.Raw, NpgsqlDbType.Text) :> ColumnWriterBase
        "properties", SinglePropertyColumnWriter("Properties", PropertyWriteMethod.Raw, NpgsqlDbType.Json) :> ColumnWriterBase ]

    let createLogger dbConnectionString =
        Serilog.Debugging.SelfLog.Enable(Console.Out);
        LoggerConfiguration()
            .Enrich.WithExceptionDetails()
            .Enrich.FromLogContext()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.PostgreSQL(dbConnectionString, "logs_automation", (dict loggingColumns))
            .WriteTo.ApplicationInsightsTraces(System.Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY"))
            .CreateLogger()

    let inline toJson properties = 
        match properties with 
        | Some(m) -> m |> serialize
        | None -> ""
    
    let logDebug (message:string) properties (log:ILogger) =
        match properties with
        | None -> log.Debug("{Message}",message)
        | Some(p) -> log.ForContext("Properties", p|>serialize).Debug("{Message}",message)

    let logInfo (message:string) properties (log:ILogger) =
        match properties with
        | None -> log.Information("{Message}",message)
        | Some(p) -> log.ForContext("Properties", p|>serialize).Information("{Message}",message)

    let logWarn (message:string) properties (log:ILogger) =
        match properties with
        | None -> log.Warning("{Message}",message)
        | Some(p) -> log.ForContext("Properties", p|>serialize).Warning("{Message}",message)

    let logError (status:Status) (message:string) (log:ILogger) =
        let msg = "Pipeline failed with error result:"
        log.Error("{Message} {Properties}", msg, (status, message) |> serialize)

    let logFatal (exn:Exception) (log:ILogger) =
        let msg = sprintf "Pipeline failed with exception: '%s' See properties for details." exn.Message
        log.Fatal(exn, "{Message}", msg)