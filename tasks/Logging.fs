// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Tasks

module Logging =

    open Core.Json

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
            .WriteTo.Console()
            .WriteTo.PostgreSQL(dbConnectionString, "logs_automation", (dict loggingColumns))
            .WriteTo.ApplicationInsightsEvents(System.Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY"))
            .CreateLogger()

    let toJson properties = 
        match properties with 
        | Some(m) -> 
            match box m with 
            | :? string -> m
            | :? int -> m
            | _ -> m |> serialize
        | None -> null

    let logInfo message properties (log:ILogger) =
        log.Information("{Message} {Properties}",message, properties |> toJson)

    let logError message properties (log:ILogger) =
        log.Error("{Message} {Properties}",message, properties |> toJson)

    let logFatal (exn:Exception) (log:ILogger) =
        log.Fatal("{Message} {Properties}", exn.Message, exn |> serialize)
