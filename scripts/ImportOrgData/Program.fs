namespace ImportOrgData

open System
open Types
open Take2
open Chessie.ErrorHandling
open FSharp.Data
open Database;
open System.IO
open System.Collections.Generic


module Program = 

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

            // printfn "Starting Import"
        
            getJsonPath argv |> go |> ignore

            // //let uitsId = addUnitToDatabase "UITS"
            // //printfn "  UITS id is %i" uitsId
                      
            // let data = OrgData.Load path

            // printfn "Importing units"
            // let units = flattenUnits data

            // printfn "Mapping children"
            // let childIds = getChildIds units

            // printfn "Mapping top-level units"
            // let topLevelUnits = getTopLevelUnits units childIds
            // for unit in topLevelUnits do
            //     evalUnit units unit
            
            
            
            //let duplicateUnitSets = getDuplicateNames units
            //printfn "  Duplicate names"
            
            //for (key,set) in duplicateUnitSets do
            //    printfn "\n      %s (%i nodes)" key set.Length
            //    for unit in set do
            //        printfn "         %s (%s)" unit.Name unit.Id
            //        printfn "         Members:"
            //        for person in unit.Members do
            //            let name = getMemberName person
            //            printfn "           %s (%s, %s)" name person.Title person.Role
                
                        
            //let childNamesAndIds = extractChildNamesAndIds units
            //let unitHashes = addUnitsToDatabase units childNamesAndIds
            
            //printfn "---> Assigning child relationships"
            //let children = assignUnitRelationships units unitHashes

            //printfn "---> Assigning top-level units to UITS" 
            //let topLevelUnits = assignTopLevelUnitsToUITS uitsId (getTopLevelUnits units children) unitHashes

            //assignUnitsMembers units unitHashes

            //printfn ""
            //printfn "---------------------------------"
            //printfn "Imported units: %i" units.Length
            //printfn "Assigned units: %i" (children.Length + topLevelUnits.Length)

            0        
         with 
         | exn -> 
            printfn "Error occurred: %s" exn.Message
            -1

