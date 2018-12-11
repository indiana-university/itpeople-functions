namespace ImportOrgData

open System
open Chessie.ErrorHandling
open FSharp.Data
open Database;
open System.IO
open System.Collections.Generic
open Npgsql
open Dapper
open Types

module OrgTree = 

    /// WCMS IDs of units that are cruft or unusable.
    let private ignoreUnits =
      [ "301d63b3814f4e1006f2bd60005e1461" // Orphans
        "301e6869814f4e1006f2bd60f7a84c6d"
        "301e64d2814f4e1006f2bd603137f37a"
        "301e6be1814f4e1006f2bd60f259e977"
        "301e6ef3814f4e1006f2bd60ce7be7c7"
        "301e726d814f4e1006f2bd6027d65020"
        "301e75dd814f4e1006f2bd60fe49f1ec"
        "304a00d1814f4e1006f2bd608dbc80ea"
        "304a023a814f4e1006f2bd6030b97ebb"
        "304f5dea814f4e1006f2bd606dca3494"
        "304f5b32814f4e1006f2bd603e299e1e"
        "301ff22b814f4e1006f2bd60b52145bb"
        "304da21e814f4e1006f2bd60fb2a0da2"
        "304da655814f4e1006f2bd60df2c51e8"
        "304d8c3a814f4e1006f2bd600ba1c5ad"
        "304ae8d2814f4e1006f2bd6091c00ec5"
        "30205531814f4e1006f2bd6033cd4394"
        "301e7d6a814f4e1006f2bd60c928c6f0"
        "3048e047814f4e1006f2bd607afe3bdf"
        "304daf6a814f4e1006f2bd6068801f8f"
        "304ddcbf814f4e1006f2bd60295eccc4"
        "304de3fe814f4e1006f2bd60d4f19219"
        "304de5fe814f4e1006f2bd606e2aa9de"
        "30224885814f4e1006f2bd60f97d709e"
        "301fe05a814f4e1006f2bd60bdf99279" // Duplicate Chief of Staff
        "301c1367814f4e1006f2bd606c8ec1ee" ] // PTI

    /// The names of known UITS divisions
    let private divisions = 
      [ "Learning Technologies"
        "Client Services and Support"
        "Networks"
        "Enterprise Systems"
        "Clinical Affairs Information Technology Services"
        "Research Technologies" ]

    /// The names of known Regional Campus units
    let private regionals =
      [ "IU Northwest"
        "IU East"
        "IUPUC"
        "IU Southeast"
        "IU South Bend"
        "IU Kokomo" ]
    
    /// The names of known OVPIT units
    let private ovpit = 
      [ "Chief of Staff"
        "Communications Office"
        "Media Digitization and Preservation Institute"
        "Financial Planning, Budget Administration and Accounting"
        "Human Resources Office"
        "Pervasive Technology Institute"
        "Institutional Assurance"
        "User Experience Office" ]

    let private emptyUnit = {Id=""; Name=""; Url=""; Members=[]; ChildrenRaw=[]; Children=[]}

    /// Map a JsonProvider 'Member' to a domain Member
    let private projectMember (m:OrgData.Member) = 
        let username = 
            match m.Vacant with
            | Some(v) -> if v then "vacant" else m.Username
            | None -> m.Username
        { Name = username
          Title = m.Title
          Role = m.Role 
          Percentage = 0 }

    /// Map a JsonProvider 'Root' to a domain Unit
    let private projectUnit (u:OrgData.Root) =
        { Id = u.Id 
          Name = u.Name
          Members = u.Members |> Seq.map projectMember 
          ChildrenRaw = u.Children
          Children = Seq.empty;
          Url="" }

    /// Flatten a 2D sequence into a 1D sequence
    let private flattenUnits data =
        data |> Seq.collect (fun u -> u)

    let private icic = StringComparison.InvariantCultureIgnoreCase

    /// Any extra units that aren't properly accounted for in the WCMS data, or
    ///  are simply to gnarly to parse. 
    let private extraUnits =
      [ { Id = "301c1367814f4e1006f2bd606c8ec1ee"
          Name="Pervasive Technology Institute"
          Url="https://pti.iu.edu/index.html"
          ChildrenRaw=[]
          Members=
            [ { Name="stewart"; Title="Executive Director"; Role="leadership"; Percentage=100 }
              { Name="moshann"; Title="Administrative Assistant"; Role="other"; Percentage=100 } ] 
          Children=
            [ { emptyUnit with Name="Science Gateway Research Center SGRC"; Url="https://sgrc.iu.edu/" }
              { emptyUnit with Name="Digital Science Center DSC"; Url="https://www.dsc.soic.indiana.edu/" }  
              { emptyUnit with Name="Data to Insight Center D2I"; Url="https://pti.iu.edu/centers/d2i/index.html" }  
              { emptyUnit with Name="Center for Applied Cybersecurity Research CACR"; Url="https://cacr.iu.edu/" }  
              { emptyUnit with Name="Hathi Trust Research Center HTRC"; Url="https://www.hathitrust.org/htrc" }  
              { emptyUnit with Name="Research Technologies"; Url="https://pti.iu.edu/centers/rt/index.html" }  
              { emptyUnit with Name="National Center for Genome Analysis Support NCGAS"; Url="https://ncgas.org/" } ] } ]

    /// Identity the top-level units and build out the unit tree for each.
    let private buildTree (units:seq<Unit>) = 

        /// Recursively identify and gather up all the child units of this unit
        let rec buildTree' (unit:Unit) =
            let children = List<Unit>()
            for cid in unit.ChildrenRaw |> Seq.map (fun c -> c.CmsId) do
                let child = units |> Seq.tryFind (fun u -> u.Id = cid)
                let resolvedChild = 
                    match child with
                    | Some c -> buildTree' c
                    | None -> { Unit.Id=cid; Name="Unknown Unit"; Members=Seq.empty; ChildrenRaw=Seq.empty; Children=Seq.empty; Url="" }
                children.Add(resolvedChild)
            
            /// Some child units are actually just the member lists of the parent unit.
            /// This was done for display purposes in WCMS, to make everything "look right".
            /// These parent/child units need to be collapsed so the data structures are consistent and correct. 
            if (children.Count = 1 && (children.[0].Name.Equals(unit.Name,icic) || children.[0].Name = children.[0].Name.ToLowerInvariant()))
            then {unit with Children = Seq.empty; ChildrenRaw = Seq.empty; Members = unit.Members |> Seq.append children.[0].Members}
            else {unit with Children = children}
        
        /// Identity all the units that are known to be children of others.
        let childIds = 
            units 
            |> Seq.collect (fun u -> u.ChildrenRaw |> Seq.map (fun c -> c.CmsId))

        /// Identify all the top-level (non-child) units, filtering out those that 
        /// are known to be garbage data. Build out a unit tree for each.
        units
        |> Seq.filter (fun u -> childIds |> Seq.exists (fun cid -> u.Id = cid) |> not)
        |> Seq.filter (fun u -> ignoreUnits |> Seq.exists(fun uid -> u.Id = uid) |> not)
        |> Seq.map buildTree'
    
    /// Partition the units into three buckets: Divisions, OVPIT, and Regional Campuses.
    /// Place the three buckets under a single UITS unit.
    let private partition (units:seq<Unit>) =
        let divisions_units = 
          { emptyUnit with 
                Name="Divisions";
                Children = units |> Seq.filter (fun u -> divisions |> Seq.contains u.Name ) }
        let ovpit_units = 
          { emptyUnit with 
                Name="Office for the Vice President of Information Technology (OVPIT)";
                Children = units |> Seq.filter (fun u -> ovpit |> Seq.contains u.Name ) }
        let regional_units = 
          { emptyUnit with 
                Name="Regional Campuses";
                Children = units |> Seq.filter (fun u -> regionals |> Seq.contains u.Name ) }
        /// Collect divisions, OVPIT, and regionals under UITS.
        { emptyUnit with 
            Name="University Information Technology Services (UITS)"
            Url="https://uits.iu.edu"
            Children = [ divisions_units; ovpit_units; regional_units ] }
        

    /// Pretty-print the org tree.
    let private printTree (uits:Unit) =
        let whiteSpace level = (String.replicate (2*level) " ")

        let rec printTree' level (unit:Unit) =
            if level < 3 then printfn ""
            printfn "%s%s" (whiteSpace level) unit.Name
            if (unit.Members |> Seq.isEmpty |> not)
            then printfn "%sMembers: %s" (whiteSpace (level+1)) (unit.Members |> Seq.map (fun m -> m.Name) |> String.concat ", ")
            if (unit.Children |> Seq.isEmpty |> not)
            then unit.Children |> Seq.iter (printTree' (level+1))

        printTree' 0 uits
        uits
    
    /// From a JSON file at 'path', generate a UITS org heirarchy.
    let buildOrgTree (path:string) = 
        path
        |> OrgData.Load
        |> flattenUnits
        |> Seq.map projectUnit
        |> buildTree
        |> Seq.append extraUnits
        |> partition
        |> printTree