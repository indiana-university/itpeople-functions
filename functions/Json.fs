// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Functions

module Json =

    open Types
    open Util
    open System
    open System.Net.Http
    open Microsoft.FSharp.Reflection
    open Newtonsoft.Json
    open Newtonsoft.Json.Serialization

    type OptionConverter() =
        inherit JsonConverter()

        override x.CanConvert(t) = 
            t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

        override x.WriteJson(writer, value, serializer) =
            let value = 
                if isNull value then null
                else 
                    let _,fields = FSharpValue.GetUnionFields(value, value.GetType())
                    fields.[0]  
            serializer.Serialize(writer, value)

        override x.ReadJson(reader, t, existingValue, serializer) =        
            let innerType = t.GetGenericArguments().[0]
            let innerType = 
                if innerType.IsValueType then (typedefof<Nullable<_>>).MakeGenericType([|innerType|])
                else innerType        
            let value = serializer.Deserialize(reader, innerType)
            let cases = FSharpType.GetUnionCases(t)
            if isNull value then FSharpValue.MakeUnion(cases.[0], [||])
            else FSharpValue.MakeUnion(cases.[1], [|value|])

    /// JSON Serialization Defaults:
    /// 1. Format property names in 'camelCase'.
    /// 2. Convert all enum values to/from their string equivalents.
    /// 3. Format all options as null or the unwrapped type
    let JsonSettings = 
        JsonSerializerSettings(
            ContractResolver=CamelCasePropertyNamesContractResolver(),
            DefaultValueHandling=DefaultValueHandling.Populate)
    JsonSettings.Converters.Add(Newtonsoft.Json.Converters.StringEnumConverter())
    JsonSettings.Converters.Add(OptionConverter())

    let mapFlagsToSeq<'T when 'T :> System.Enum> (value: 'T) = 
        JsonConvert.SerializeObject(value, JsonSettings).Trim('"')
        |> fun s -> s.Split([|','|])
        |> Seq.map (fun s -> System.Enum.Parse(typeof<'T>,s.Trim()) :?> 'T)
        |> Seq.filter (fun e -> e.ToString() <> "None")

    let tryDeserialize<'T> status str =
        try JsonConvert.DeserializeObject<'T>(str, JsonSettings) |> Ok
        with exn -> Error (status, exn.Message)

    /// Attempt to deserialize the request body as an object of the given type.
    let deserializeBody<'T> (req:HttpRequestMessage) = async { 
        let! body = req.Content.ReadAsStringAsync() |> Async.AwaitTask 
        return 
            if isEmpty body
            then Error (Status.BadRequest, "Expected a request body but received nothing")
            else tryDeserialize<'T> Status.BadRequest body 
    }