open Database
open OrgGroupHelpers
open System.IO
open Newtonsoft.Json

let pagesToGroups (pages : PageRecord list) =
    pages
    |> List.map pageToGroup
    |> List.choose id

let saveToFile (o) =
    let path = "output/groups.json"
    let json = JsonConvert.SerializeObject o
    FileInfo(path).Directory.Create() |> ignore
    File.WriteAllText(path, json)

let GetConnectionString (args:string[]) =
    // TODO: get from args
    @"server=localhost;database=orgchart_wcms;user=root;pwd=secret;"

[<EntryPoint>]
let main args =
    ConnectionString <- GetConnectionString args

    GetPages
    |> pagesToGroups
    |> saveToFile
    
    0
