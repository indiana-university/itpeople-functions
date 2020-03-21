// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause    

namespace Tasks 

module Functions=

    open Logging

    open Core.Types
    open Core.Util

    open Serilog

    open System.Net
    open System.Net.Http
    open Microsoft.Azure.WebJobs
    open Microsoft.Azure.WebJobs.Extensions.Http
    open Microsoft.Extensions.Logging

    let connStr = env "DbConnectionString"
    let hrDataUrl = env "HrDataUrl"
    let uaaUrl = env "UaaUrl"
    let uaaUser = env "UaaUser"
    let uaaPassword = env "UaaPassword"
    let adUser = env "AdUser"
    let adPassword = env "AdPassword"
    let buildingUrl = env "BuildingUrl"
    let buildingUser = env "BuildingUser"
    let buildingPassword = env "BuildingPassword"

    let logger = createLogger connStr

    let execute (workflow:Async<Result<'b,Error>>)= 
        async {
            let! result = workflow
            match result with
            | Ok(_) -> ()
            | Error(msg) -> 
                msg
                |> sprintf "Workflow failed with error: %A"
                |> System.Exception
                |> raise
        } |> Async.RunSynchronously

    let execute' (ctx:ExecutionContext) (workflow:Serilog.ILogger -> Async<Result<unit,Error>>)= 
        async {
            let log =
                logger
                    .ForContext("InvocationId", ctx.InvocationId)
                    .ForContext("FunctionName", ctx.FunctionName)
            try
                log |> logInfo "Pipeline started." None
                let! result = workflow log
                match result with
                | Ok(_) -> 
                    log |> logInfo "Pipeline succeeded." None
                    ()
                | Error(msg) -> 
                    let err = sprintf "Pipeline failed with error: %A" msg
                    log |> logError err None
                    failwith err // we must throw so the functions runtime knows to retry.
            with                
            | exn -> 
                log |> logFatal exn
                raise exn // we must throw so the functions runtime knows to retry.
        } |> Async.RunSynchronously


    /// This module defines the bindings and triggers for all functions in the project
    [<FunctionName("PingGet")>]
    let ping
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")>] req:HttpRequestMessage) =
        req.CreateResponse(HttpStatusCode.OK, "pong!")

    // Update our buildings table from a canonical data source.
    // [<Disable>]
    [<FunctionName("BuildingsUpdate")>]
    let buildingsUpdate
        ([<TimerTrigger("0 */15 * * * *", RunOnStartup=true)>] timer: TimerInfo,
         ctx: ExecutionContext) =
        Buildings.updateBuildings connStr buildingUrl buildingUser buildingPassword |> execute' ctx

    // Enqueue the netids of all the people for whom we need to update
    // canonical HR data.
    // [<Disable>]
    [<FunctionName("PeopleUpdateHrTable")>]
    let peopleUpdateHrTable
        ([<TimerTrigger("0 */15 * * * *")>] timer: TimerInfo,
         [<Queue("people-update")>] queue: ICollector<string>,
         ctx: ExecutionContext) = 
        People.updateHrTable queue connStr hrDataUrl uaaUrl uaaUser uaaPassword |> execute' ctx

    // Pluck a netid from the queue, fetch that person's HR data from the API, 
    // and update it in the DB.
    // [<Disable>]
    [<FunctionName("PeopleUpdateWorker")>]
    let peopleUpdateWorker
        ([<QueueTrigger("people-update")>] netid: string,
         ctx: ExecutionContext) =
        People.updatePerson netid connStr |> execute' ctx

        // Enqueue the tools for which permissions need to be updated.
    // [<Disable>]
    [<FunctionName("ToolUpdateBatcher")>]
    let toolUpdateBatcher
        ([<TimerTrigger("0 */5 * * * *")>] timer: TimerInfo,
         [<Queue("tool-update")>] queue: ICollector<string>,
         log: ILogger) =
         Tools.enqueueTools log queue connStr |> execute         

    // Pluck a tool from the queue. 
    // Fetch all the people that should have access to this tool, then fetch 
    // all the people currently in the AD group associated with this tool. 
    // Determine which people should be added/removed from that AD group
    // and enqueue and add/remove message for each.
    // [<Disable>]
    [<FunctionName("ToolUpdateWorker")>]
    let toolUpdateWorker
        ([<QueueTrigger("tool-update")>] item: string,
         [<Queue("tool-update-person")>] queue: ICollector<string>,
         log: ILogger) = 
         Tools.enqueueAccessUpdates log queue item connStr adUser adPassword |> execute

    // Pluck a tool-person from the queue. 
    // Add/remove the person to/from the specified AD group.
    // [<Disable>]
    [<FunctionName("ToolUpdatePersonWorker")>]
    let toolUpdatePersonWorker
        ([<QueueTrigger("tool-update-person")>] item: string,
         log: ILogger) = 
         Tools.updatePersonAccess log item connStr adUser adPassword |> execute