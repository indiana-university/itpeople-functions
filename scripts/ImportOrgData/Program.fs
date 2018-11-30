namespace ImportOrgData

open System
open Types
open Util
open Chessie.ErrorHandling
open FSharp.Data
open Database;
open System.IO

module Program = 

    // this doesn't need to be the complete json file
    // just a sample that includes the structure and
    // all of the possible fields of the json that will
    // eventually be parsed.
    type OrgData = JsonProvider<"OrgDataSample.json">

    let connectionString = "User ID=root;Host=localhost;Port=5432;Database=circle_test;Pooling=true;"
    
    let getJsonPath (args:string[]) =
        if args.Length = 0 then
            raise (Exception "Invalid arguments. Excepted input path as first argument")

        if not(File.Exists(args.[0])) then
            raise (Exception (sprintf "File does not exist: %s" args.[0]))

        args.[0]

    [<EntryPoint>]
    let main argv =
        try
            printfn "Starting Import"
        
            let path = getJsonPath argv
            printfn "---> Json path is %s" path

            let uitsId = Database.getUitsId connectionString
            printfn "---> UITS id is %i" uitsId

            printfn "---> Loading %s" (Path.GetFileName path)
            let data = OrgData.Load path

            for units in data do
                for unit in units do
                    let unitId = Database.addUnitToDb connectionString unit.Name 
                    printfn "---> Imported unit %s (Id=%i)" unit.Name unitId
                
            0        
         with 
         | exn -> 
            printfn "Error occurred: %s" exn.Message
            -1

