namespace Functions.Common

module Logging =

    open System
    open System.Net.Http
    open Types

    open Serilog
    open Serilog.Core
    open Serilog.Context
    open Serilog.Sinks.PostgreSQL
    open NpgsqlTypes

    let private loggingColumns = 
      [ "raise_date", TimestampColumnWriter(NpgsqlDbType.Timestamp) :> ColumnWriterBase
        "level", LevelColumnWriter(true, NpgsqlDbType.Varchar) :> ColumnWriterBase
        "elapsed", SinglePropertyColumnWriter("Elapsed", PropertyWriteMethod.Raw, NpgsqlDbType.Integer) :> ColumnWriterBase 
        "status", SinglePropertyColumnWriter("Status", PropertyWriteMethod.Raw, NpgsqlDbType.Integer) :> ColumnWriterBase
        "method", SinglePropertyColumnWriter("Method", PropertyWriteMethod.Raw, NpgsqlDbType.Text) :> ColumnWriterBase
        "function", SinglePropertyColumnWriter("Function", PropertyWriteMethod.Raw, NpgsqlDbType.Text) :> ColumnWriterBase
        "parameters", SinglePropertyColumnWriter("Parameters", PropertyWriteMethod.Raw, NpgsqlDbType.Text) :> ColumnWriterBase
        "query", SinglePropertyColumnWriter("Query", PropertyWriteMethod.Raw, NpgsqlDbType.Text) :> ColumnWriterBase
        "detail", SinglePropertyColumnWriter("Detail", PropertyWriteMethod.Raw, NpgsqlDbType.Text) :> ColumnWriterBase
        "exception", ExceptionColumnWriter(NpgsqlDbType.Text) :> ColumnWriterBase ]

    let private splitPath (req:HttpRequestMessage) =
        req.RequestUri.AbsolutePath.Split("/", StringSplitOptions.RemoveEmptyEntries)

    let private funcName (req:HttpRequestMessage) =
        req
        |> splitPath
        |> Seq.head
    
    let private funcParams (req:HttpRequestMessage) =
        req
        |> splitPath
        |> Seq.skip 1
        |> String.concat "/"
    
    let private query (req:HttpRequestMessage) =
        req.RequestUri.Query

    let createLogger (config:AppConfig) =
        Serilog.Debugging.SelfLog.Enable(Console.Out);
        Serilog.LoggerConfiguration()
            .Enrich.WithDemystifiedStackTraces()
            .WriteTo.Console()
            .WriteTo.PostgreSQL(config.DbConnectionString, "logs", (dict loggingColumns))
            .WriteTo.ApplicationInsightsTraces(System.Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY"))
            .CreateLogger()

    let logInfo (log:Logger) (req:HttpRequestMessage) (status:Status) elapsed =
        log.Information("{Method} {Function}/{Parameters}{Query} finished in {Elapsed} ms with status {Status}.", 
            req.Method, req |> funcName, req |> funcParams, req |> query, elapsed, int status)

    let logError (log:Logger) (req:HttpRequestMessage) (status:Status) elapsed errors =
        log.Error(
            "{Method} {Function}/{Parameters}{Query} errored in {Elapsed} ms with status {Status}. Errors: {Detail}", 
            req.Method, req |> funcName, req |> funcParams, req |> query, elapsed, int status, errors)

    let logFatal (log:Logger) (req:HttpRequestMessage) elapsed (exn:Exception) =
        log.Fatal(
            exn,
            "{Method} {Function}/{Parameters}{Query} threw an exception after {Elapsed} ms with status {Status}. {Detail}", 
            req.Method, req |> funcName, req |> funcParams, req |> query, elapsed, int Status.InternalServerError, "See exception for details")
