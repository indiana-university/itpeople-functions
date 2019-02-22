module OrgXmlHelpers

open OrgTypes
open Database
open System.Xml

let NotNull x = x <> null
let contentToXml content =
    let doc = new XmlDocument()
    doc.LoadXml content
    doc

let isDivision (doc : XmlDocument) =
    match doc.DocumentElement.SelectSingleNode
              "/region-render/system-data-structure/division-info" with
    | null -> false
    | _ -> true

let isUnit (doc : XmlDocument) =
    match doc.DocumentElement.SelectSingleNode
              "/region-render/system-data-structure/unit-info" with
    | null -> false
    | _ -> true

let isTeam (doc : XmlDocument) =
     doc.DocumentElement.SelectSingleNode "/region-render/system-data-structure/group-info" 
     |> NotNull

let getTitleFromNode (node:XmlNode) =
    let title = match node.SelectSingleNode("title") with
                    | null -> ""
                    | n -> match n.InnerText with 
                            | null -> ""
                            | "Title" -> ""
                            | s -> s
    if title <> "" then
        title
    else
        let businessTitle = 
            match node.SelectSingleNode("username/dynamic-metadata[./name[contains(text(), 'businessTitle')]]/value") with 
            | null -> ""
            | n -> match n.InnerText with
                    | null -> ""
                    | "Title" -> ""
                    | s -> s

        if businessTitle <> "" then
            businessTitle
        else ""

let getMembersByXPath (xml : XmlDocument) xpath role (includeVacant:bool) : Member list =
    xml.DocumentElement.SelectNodes xpath
    |> Seq.cast<XmlNode>
    |> Seq.filter NotNull
    |> Seq.map (fun node -> 
                match node.SelectSingleNode("username/name") with 
                | null -> 
                    match node.SelectSingleNode("title").InnerText with 
                    | "" -> None
                    | title -> if includeVacant then Some { role = role; title = title; username = null; vacant = true} else None
                | n -> Some {
                     vacant = false
                     role = role 
                     title = getTitleFromNode node
                     username = n.InnerText}
    )
    |> Seq.choose id
    |> Seq.toList

let getAdministrativeAssistantByXPath (xml : XmlDocument) xpath: Member list =
    xml.DocumentElement.SelectNodes xpath
    |> Seq.cast<XmlNode>
    |> Seq.filter NotNull
    |> Seq.filter (fun n -> n.SelectSingleNode("name") <> null) // must have a username
    |> Seq.map (fun n -> {
         vacant = false
         role = "other" 
         title = match n.SelectSingleNode("title").InnerText with
                    | null -> "Administrative Assistant"
                    | "Title" -> "Administrative Assistant" 
                    | s -> s
         username = n.SelectSingleNode("name").InnerText})
    |> Seq.toList

let getDivisionMembers (xml : XmlDocument) =
    let avps = getMembersByXPath xml "//region-render/system-data-structure/division-info/associateVP" "leadership" true
    let adminAssistants = getAdministrativeAssistantByXPath xml "//region-render/system-data-structure/division-info/assistant-profile"
    avps @ adminAssistants |> List.distinct

let getUnitMembers (xml : XmlDocument) =
    let directors = getMembersByXPath xml "//region-render/system-data-structure/unit-info/directorInfo" "leadership" true
    let adminAssistants = getAdministrativeAssistantByXPath xml "//region-render/system-data-structure/unit-info/assistant-profile"
    directors @ adminAssistants |> List.distinct

let getTeamMembers (xml : XmlDocument) =
    let managers = getMembersByXPath xml "//region-render/system-data-structure/group-info/managerInfo" "leadership" true
    let adminAssistants = getAdministrativeAssistantByXPath xml "//region-render/system-data-structure/group-info/assistant-profile"
    let members = getMembersByXPath xml "//region-render/system-data-structure/group-info/staffInfo" "member" false
    managers @ adminAssistants @ members |> List.distinct

let getChildGroupsFromXPath (xml:XmlDocument, xpath) =
    xml.SelectNodes xpath
    |> Seq.cast<XmlNode>
    |> Seq.filter NotNull
    |> Seq.map (fun n -> n.InnerText)
    |> Seq.filter (fun p -> p <> "/")
    |> Seq.map GetPageByPath
    |> Seq.choose id
    |> Seq.map (fun page -> { 
                        cms_id = page.cms_id
                        name = match page.title with  
                                | null -> page.name
                                | "Title" -> page.name
                                | s -> s
                        })
    |> Seq.toList

let getDivisionChildren (xml : XmlDocument) = 
    getChildGroupsFromXPath (xml, "//unit/path")

let getUnitChildren (xml : XmlDocument) = 
    getChildGroupsFromXPath (xml, "//group/path")

