// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause    

module Tasks.Functions

open Core.Types
open Core.Util

open System
open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Extensions.Http
open Microsoft.Extensions.Logging

/// This module defines the bindings and triggers for all functions in the project

[<FunctionName("PingGet")>]
let ping
    ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")>] req) =
    "pong!"

// [<FunctionName("CronTest")>]
// let cronTest
//     ([<TimerTrigger("0 */1 * * * *")>] timer: TimerInfo,
//      [<Queue("test-queue")>] queue: ICollector<string>,
//      log: ILogger) =
//     // Log invocation
//     let timestamp = DateTime.Now.ToLongTimeString()
//     sprintf "Timed function fired at %A. " timestamp |> log.LogInformation
//     // Queue a message 
//     let msg = sprintf "queue message @ %s" timestamp
//     sprintf "Enqueued msg: '%s'" msg |> log.LogInformation
//     queue.Add msg

// [<FunctionName("QueueTest")>]
// let queueTest
//     ([<QueueTrigger("test-queue")>] item: string,
//      log: ILogger) =
//     // Log the dequeued message
//     sprintf "Dequeued msg: '%s'" item |> log.LogInformation
