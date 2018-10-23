module SqlServerContainer

open System
open Docker.DotNet
open Docker.DotNet.Models
open System.Data.SqlClient
open System.Collections.Generic
open Dapper
open MyFunctions.Common
open MyFunctions.Common.Types

let image = "microsoft/mssql-server-linux"
let connStr = "Server=127.0.0.1,1434;User Id=sa;Password=P@55w0rd;Timeout=5"
let client = (new DockerClientConfiguration(new Uri("http://localhost:2376"))).CreateClient()

let result b = if b then "[OK]" else "[ERROR]"

let private ensureImage image = async {
    let listParams = new ImagesListParameters(MatchName=image)
    let! images = client.Images.ListImagesAsync(listParams) |> Async.AwaitTask
    if images |> Seq.exists (fun i -> i.ID = image)
    then
        image |> sprintf "Using existing Docker image: %s." |> Console.Out.WriteLine
    else
        image |> sprintf "Fetching Docker image: %s... " |> Console.Out.Write
        let imageParams = new ImagesCreateParameters()
        imageParams.FromImage <- image
        imageParams.Tag <- "latest"
        do! client.Images.CreateImageAsync(imageParams, null, Progress()) |> Async.AwaitTask
        true |> result |> Console.Out.WriteLine
}

let private sqlHostConfig = 
    let hostConfig = HostConfig()
    let binding = PortBinding()
    binding.HostPort <- "1434"
    binding.HostIP <- "127.0.0.1"
    let bindingList = ResizeArray<PortBinding> [binding]
    hostConfig.PortBindings <- Dictionary<string, IList<PortBinding>>()
    hostConfig.PortBindings.Add("1433/tcp", bindingList)
    hostConfig

let private sqlConfig =
    let env= ["ACCEPT_EULA=Y"; "SA_PASSWORD=P@55w0rd"; "MSSQL_PID=Developer"]
    let config = new Config()
    config.Env <- ResizeArray<string> env
    config

let private createContainer image container = async {
    sprintf "Creating container %s from image %s... " container image |> Console.Write
    let createParams = new CreateContainerParameters(sqlConfig)
    createParams.Image <- image
    createParams.Name <- container
    createParams.Tty <- true
    createParams.HostConfig <- sqlHostConfig
    let! response = client.Containers.CreateContainerAsync(createParams) |> Async.AwaitTask
    if response.Warnings <> null 
    then
        false |> result |> Console.WriteLine
        String.Join(';', response.Warnings) |> sprintf "   Warnings: '%s'" |> Console.WriteLine
        raise(Exception("Couldn't create container."))
    else
        true |> result |> Console.WriteLine
}


let private start' container = async {
    container |> sprintf "Starting container %s ..." |> Console.Write
    let! started = client.Containers.StartContainerAsync(container, new ContainerStartParameters()) |> Async.AwaitTask
    started |> result |> Console.WriteLine
    if (started = false) then raise(Exception("Couldn't start container."))
}

let private ready () = async {
    try
        use conn = new SqlConnection(connStr)
        do! conn.OpenAsync() |> Async.AwaitTask
        return true
    with 
    | exn -> 
        // sprintf "Failed to connect to server: %s" exn.Message |> Console.WriteLine
        return false
}
let private ensureReady () = async {
    let mutable count = 0
    let mutable isReady = false
    while isReady = false && count < 20 do
        "Checking if server is ready..." |> Console.Write
        let! isReady' = ready()
        isReady <- isReady'
        if isReady = false
        then
            "[NOPE]" |> Console.WriteLine
            count <- count + 1
            do! Async.Sleep(2000)
        else
            "[YEP]" |> Console.WriteLine
    
    if count = 20 
    then raise(Exception("SQL Server never became ready. :("))
}

let start container = async {
    do! ensureImage image
    do! createContainer image container
    do! start' container
    do! ensureReady ()
    return true
}

let stop container = async {
    container |> sprintf "Stopping container %s... " |> Console.Write
    let! stopped = client.Containers.StopContainerAsync(container, new ContainerStopParameters()) |> Async.AwaitTask
    stopped |> sprintf "[%b]" |> Console.WriteLine
    return stopped
}

let delete container = async {
    do! client.Containers.RemoveContainerAsync(container, new ContainerRemoveParameters()) |> Async.AwaitTask
}

let migrate () = 
    Program.migrate connStr ["up"]

let populate () = async {
    use cn = new SqlConnection(connStr)
    let! inserted = cn.InsertAsync<Unit>(Fakes.cito) |> Async.AwaitTask
    return if inserted.HasValue then inserted.Value else 0
}
