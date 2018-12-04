namespace ImportOrgData

open System
open Types
open Util
open Chessie.ErrorHandling
open FSharp.Data
open Database;
open System.IO
open System.Collections.Generic

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


    let addUnitToDatabase unitName = 
        Database.addUnitToDb connectionString unitName

    let createChildRelation unitId childId =
        match childId with
        | 0 -> 0
        | v -> Database.addUnitRelation connectionString unitId childId

    let getUnitIdFromDictionary unitId (dictionary: Dictionary<string,int>) =
        match dictionary.TryGetValue(unitId) with
        | true, v -> v
        | _ -> 0

    let flattenUnits (data:OrgData.Root[][]) =
        let flattened = new List<OrgData.Root>();
        for units in data do
            for unit in units do
                flattened.Add(unit)
        
        flattened.ToArray()

    type UnitNameId = (string*string)

    let findUnitWithId (units: OrgData.Root[]) id = 
        units |> Array.find (fun unit -> unit.Id = id)

    let findUnitWithName (units: OrgData.Root[]) name = 
        units |> Array.tryFind (fun unit -> unit.Name.Trim() = name)

    let resolveLinkChild (units: OrgData.Root[]) (child: OrgData.Child) =
        let name = child.Name.Trim()
        let unit = findUnitWithName units (name)
        match unit with
        | Some unit -> (child.Name, unit.Id)
        | _ -> (name, "")

    let extractChildNamesAndIds (units:OrgData.Root[]) =
        let children = new List<UnitNameId>()
        for unit in units do
            for child in unit.Children do
                if not(child.Type="link") then
                    children.Add ((child.Name, child.CmsId))
                else
                    children.Add(resolveLinkChild units child)

        children.ToArray()

    let getValueForRole role = 
        match role with
        | "leadership" -> 4
        | "sublead" -> 3
        | "member" -> 2
        | _ -> 1

    let assignMembersToUnit unitId (members: OrgData.Member[]) = 
        for person in members do
            let result = Database.addMemberRelation connectionString unitId person.Username person.Title (getValueForRole person.Role)
            match result with
            | true -> printfn  "-------> User %s added to unit" person.Username
            | false -> printfn "-------> Could not add user %s to unit" person.Username

    let assignUnitsMembers (units:OrgData.Root[]) (hashes:Dictionary<string,int>) =
        for unit in units do
            printfn "---> Assigning members to %s" unit.Name
            let unitId = getUnitIdFromDictionary unit.Id hashes
            assignMembersToUnit unitId unit.Members
            
    let resolveUnitName (unit:OrgData.Root) (childNamesAndIds: UnitNameId[]) = 
        let childName = childNamesAndIds |> Array.tryFind(fun (_, id)-> id = unit.Id)
        match childName with
        | Some (name,_) -> printfn "found childname %s for %s" name unit.Name; name
        | _ -> printfn "could not find child name for %s" unit.Name; unit.Name

    let rec getUniqueName name (usedNames:string[]) count =
        let found = usedNames |> Array.tryFind(fun n -> n = name)
        match found with
        | Some n -> getUniqueName (sprintf "%s %i" n count) usedNames (count+1)
        | _ -> name

    let addUnitsToDatabase (units: OrgData.Root[]) (childNamesAndIds: UnitNameId[]) = 
        let usedNames = new List<string>()
        let unitHashes = new Dictionary<string, int>()
                
        for unit in units do
            let nameToUse = getUniqueName (resolveUnitName unit childNamesAndIds) (usedNames.ToArray()) 1
            let unitId = addUnitToDatabase nameToUse
            unitHashes.Add(unit.Id, unitId) 
            usedNames.Add(nameToUse)
            //assignMembersToUnit unitId unit.Members |> ignore
            printfn "---> Imported unit %s (Id=%i)" nameToUse unitId
        unitHashes

    
    let addUnitsToDatabase' (units: OrgData.Root[]) = 
        let usedNames = new List<string>()
        let unitHashes = new Dictionary<string, int>()
                
        for unit in units do
            let nameToUse = sprintf "%s (%s)" unit.Name unit.Id
            let unitId = addUnitToDatabase nameToUse
            unitHashes.Add(unit.Id, unitId) 
            //assignMembersToUnit unitId unit.Members |> ignore
            printfn "---> Imported unit %s (Id=%i)" nameToUse unitId
        unitHashes

    let assignUnitRelationships (units: OrgData.Root[]) (hashes: Dictionary<string, int>) =
        let assigned = new List<string>()
        for unit in units do
            let unitId = getUnitIdFromDictionary unit.Id hashes
            for child in unit.Children do
                let childId = getUnitIdFromDictionary child.CmsId hashes
                match createChildRelation unitId childId with
                | 0 -> printfn "--> unit %s has invalid child %s" unit.Name child.Name
                | v -> 
                    assigned.Add(child.CmsId)
                    printfn "--> assigned %s as child of unit %s" child.Name unit.Name
        
        assigned.ToArray()
    
    let getTopLevelUnits (units: OrgData.Root[]) (children: string[]) =
        units |> Array.filter (fun unit -> children |> (Array.exists (fun id -> unit.Id =id )
            >> not ))

    let extractParents (units: OrgData.Root[]) (children: UnitNameId[]) =
        units |> Array.filter (fun unit -> children |> (Array.exists (fun (_,id) -> unit.Id = id)
            >> not ))

    let assignTopLevelUnitsToUits uitsId (topLevelUnits: OrgData.Root[]) (hashes: Dictionary<string, int>) =
        let assigned = new List<string>()
        for unit in topLevelUnits do
            let childId = getUnitIdFromDictionary unit.Id hashes
            match createChildRelation uitsId childId with
                | 0 -> printfn "--> could not assign unit %s to UITS" unit.Name
                | v -> printfn "--> assigned %s as child of UITS" unit.Name
        
        assigned.ToArray()            

    let getDuplicateNames (units: OrgData.Root[]) = units |> Array.groupBy (fun unit -> unit.Name)
                                                          |> Array.choose (fun (key,set) -> 
                                                             if set.Length > 1 then Some key else None)

    [<EntryPoint>]
    let main argv =
        try
            printfn "Starting Import"
        
            let path = getJsonPath argv
            printfn "---> Json path is %s" path

            let uitsId = addUnitToDatabase "UITS"
            printfn "---> UITS id is %i" uitsId

            printfn "---> Loading %s" (Path.GetFileName path)
            let data = OrgData.Load path

            printfn "---> Importing units"
            let units = flattenUnits data
                        
            let childNamesAndIds = extractChildNamesAndIds units
            let unitHashes = addUnitsToDatabase units childNamesAndIds
            
            printfn "---> Assigning child relationships"
            let children = assignUnitRelationships units unitHashes

            printfn "---> Assigning top-level units to UITS" 
            let topLevelUnits = assignTopLevelUnitsToUits uitsId (getTopLevelUnits units children) unitHashes

            assignUnitsMembers units unitHashes

            printfn ""
            printfn "---------------------------------"
            printfn "Imported units: %i" units.Length
            printfn "Assigned units: %i" (children.Length + topLevelUnits.Length)

            0        
         with 
         | exn -> 
            printfn "Error occurred: %s" exn.Message
            -1

