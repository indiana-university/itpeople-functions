namespace StateServer

module Functions =

    open System.Net.Http
    open Microsoft.Azure.WebJobs
    open Newtonsoft.Json

    open Database.Fakes

    /// The request body of the Pact client
    type ProviderState = {
        consumer: string
        state: string
    }

    [<FunctionName("Ping")>]
    let ping
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "get", Route = "ping")>] 
        req: HttpRequestMessage) =
        req.CreateResponse(System.Net.HttpStatusCode.OK)

    [<FunctionName("InitializeState")>]
    let initializeState
        ([<HttpTrigger(Extensions.Http.AuthorizationLevel.Anonymous, "post", Route = "state")>]
        req: HttpRequestMessage) =
        if System.Environment.GetEnvironmentVariable("UseFakeData") <> "true" 
        then 
            let state = 
                req.Content.ReadAsAsync<ProviderState>() 
                |> Async.AwaitTask 
                |> Async.RunSynchronously
            // System.Console.Write "Resetting database with test fakes... "
            resetDatabaseWithTestFakes()
            // System.Console.WriteLine(" [OK]")
        else System.Console.WriteLine "Serving test takes from memory."
        req.CreateResponse(System.Net.HttpStatusCode.OK)