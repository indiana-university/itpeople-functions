open Database
open OrgGroupHelpers
open System.IO
open Newtonsoft.Json

let saveToFile (o) =
    let path = "output/groups.json"
    let json = JsonConvert.SerializeObject o
    FileInfo(path).Directory.Create() |> ignore
    File.WriteAllText(path, json)

let GetConnectionString (args:string[]) =
    if args.Length > 0 
    then args.[0]
    else  @"server=localhost;database=orgchart_wcms;user=root;pwd=secret;"

[<EntryPoint>]
let main args =
    ConnectionString <-  args |> GetConnectionString 
    
    GetPages
    |> pagesToGroups
    |> saveToFile
    
    0
