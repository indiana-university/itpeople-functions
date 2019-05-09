// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause    

namespace Tasks 

module Types=
    open Core.Types

    type IDataRepository =
      { GetAllNetIds: unit -> Async<Result<seq<NetId>, Error>>
        FetchLatestHRPerson: NetId -> Async<Result<Person, Error>>
        UpdatePerson: Person -> Async<Result<Person, Error>> }

module FakeRepository=

    open Types
    open Core.Types
    open Core.Fakes

    let Respository : IDataRepository = 
     { GetAllNetIds = fun () -> ["rswanso"] |> List.toSeq |> ok
       FetchLatestHRPerson = fun netid -> swanson |> ok
       UpdatePerson = fun person -> person |> ok }

module DataRepository=
    ()

module Functions=

    open Core.Types

    open System.Net
    open System.Net.Http
    open Microsoft.Azure.WebJobs
    open Microsoft.Azure.WebJobs.Extensions.Http
    open Microsoft.Extensions.Logging

    let execute (workflow:'a -> Async<Result<'b,Error>>) (arg:'a)= 
        async {
            let! result = workflow arg
            match result with
            | Ok(_) -> return ()
            | Error(msg) -> 
                msg
                |> sprintf "Workflow failed with error: %A"
                |> System.Exception
                |> raise
        } |> Async.StartAsTask

    let tap f x =
        f x // invoke f with the argument x
        ok x // pass x unchanged to the next step in the workflow

    let data = FakeRepository.Respository    

    /// This module defines the bindings and triggers for all functions in the project
    [<FunctionName("PingGet")>]
    let ping
        ([<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ping")>] req:HttpRequestMessage) =
        req.CreateResponse(HttpStatusCode.OK, "pong!")

    // Enqueue the netids of all the people for whom we need to update
    // canonical HR data.
    [<FunctionName("PeopleUpdateBatcher")>]
    let peopleUpdateBatcher
        ([<TimerTrigger("0 */1 * * * *")>] timer: TimerInfo,
         [<Queue("people-update")>] queue: ICollector<string>,
         log: ILogger) = 
        
        let enqueueAllNetIds netids =
            netids |> Seq.iter queue.Add

        let logEnqueuedNumber netids = 
            netids
            |> Seq.length
            |> sprintf "Enqueued %d netids for update."
            |> log.LogInformation

        let workflow = 
            data.GetAllNetIds
            >=> tap enqueueAllNetIds
            >=> tap logEnqueuedNumber

        execute workflow ()

    // Pluck a netid from the queue, fetch that person's HR data from the API, 
    // and update it in the DB.
    [<FunctionName("PeopleUpdateWorker")>]
    let peopleUpdateWorker
        ([<QueueTrigger("people-update")>] netid: string,
         log: ILogger) =

        let logUpdatedPerson (person:Person) = 
            person.NetId
            |> sprintf "Updated HR data for %s."
            |> log.LogInformation

        let workflow = 
            data.FetchLatestHRPerson
            >=> data.UpdatePerson
            >=> tap logUpdatedPerson

        execute workflow netid
