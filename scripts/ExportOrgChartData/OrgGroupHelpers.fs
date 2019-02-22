module OrgGroupHelpers

open OrgTypes
open Database
open OrgXmlHelpers
open System.Xml

let divisionDataToGroup (page : PageRecord, xml : XmlDocument) : Group =
    let members = xml |> getDivisionMembers
    let children = xml |> getDivisionChildren
    { id = page.cms_id
      path = page.path
      name = page.title
      description = page.description
      members = members
      children = children }

let unitDataToGroup (page : PageRecord, xml : XmlDocument) : Group =
    let members = xml |> getUnitMembers
    let children = xml |> getUnitChildren

    let title = match page.title with 
                | null -> ""
                | "Title" -> ""
                | s -> s
    let name = if title <> "" then title
                else
                  match xml.SelectSingleNode "/region-render/system-data-structure/unit-info/name" with 
                      | null -> page.name
                      | s -> match s.InnerText with
                              | "" -> page.name
                              | "Title" -> page.name
                              | s -> s

    { id = page.cms_id
      path = page.path
      name = name
      description = page.description
      members = members
      children = children }

let teamDataToGroup (page : PageRecord, xml : XmlDocument) : Group =
    let members = xml |> getTeamMembers
    let title = match page.title with 
                | null -> ""
                | "Title" -> ""
                | s -> s
    let name = if title <> "" then title
                else
                match xml.SelectSingleNode "/region-render/system-data-structure/group-info/name" with 
                  | null -> page.name
                  | s -> match s.InnerText with
                          | "" -> page.name
                          | "Title" -> page.name
                          | s -> s


    { id = page.cms_id
      path = page.path
      name = name
      description = page.description
      members = members
      children = [] }

let pageToGroup (page : PageRecord) =
    let xml = contentToXml page.content
    if xml |> isDivision then Some(divisionDataToGroup (page, xml))
    else if xml |> isUnit then Some(unitDataToGroup (page, xml))
    else if xml |> isTeam then Some(teamDataToGroup (page, xml))
    else None

let pagesToGroups (pages : PageRecord list) =
    pages
    |> List.map pageToGroup
    |> List.choose id
