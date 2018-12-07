namespace ImportOrgData

module Types =
    open FSharp.Data

    type OrgData = JsonProvider<"OrgDataSample.json">

    type Member = {
        Name: string
        Title: string
        Role: string
        Percentage: int
    }

    type Unit = {
        Id: string
        Name: string
        Url: string
        Members: seq<Member>
        ChildrenRaw: seq<OrgData.Child>
        Children: seq<Unit>
    }
