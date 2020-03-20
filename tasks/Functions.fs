// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause    

namespace Tasks 

module Functions=

    open Core.Types

    open System.Net
    open System.Net.Http
    open Microsoft.Azure.WebJobs
    open Microsoft.Azure.WebJobs.Extensions.Http
    open Microsoft.Extensions.Logging
    
    open Core.Util

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

    /// This module defines the bindings and triggers for all functions in the project
    [<FunctionName("PingGet")>]
    let ping
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")>] req:HttpRequestMessage) =
        req.CreateResponse(HttpStatusCode.OK, "pong!")

    // Update our buildings table from a canonical data source.
    // [<Disable>]
    [<FunctionName("BuildingsUpdate")>]
    let buildingsUpdate
        ([<TimerTrigger("0 */15 * * * *")>] timer: TimerInfo,
         log: ILogger) =
        Buildings.updateBuildings log connStr buildingUrl buildingUser buildingPassword |> execute

    // Enqueue the netids of all the people for whom we need to update
    // canonical HR data.
    // [<Disable>]
    [<FunctionName("PeopleUpdateHrTable")>]
    let peopleUpdateHrTable
        ([<TimerTrigger("0 */15 * * * *")>] timer: TimerInfo,
         [<Queue("people-update")>] queue: ICollector<string>,
         log: ILogger) = 
        People.updateHrTable log queue connStr hrDataUrl uaaUrl uaaUser uaaPassword |> execute

    // Pluck a netid from the queue, fetch that person's HR data from the API, 
    // and update it in the DB.
    // [<Disable>]
    [<FunctionName("PeopleUpdateWorker")>]
    let peopleUpdateWorker
        ([<QueueTrigger("people-update")>] netid: string,
         log: ILogger) =
        People.updatePerson log netid connStr |> execute

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