// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Tests

open Xunit
open Novell.Directory.Ldap

module AdTests =

    let username = "CHANGEME"
    let password = "CHANGEME"
    let searchBase = "ou=Accounts,dc=ads,dc=iu,dc=edu"
    let dn = "CHANGEME"
    let searchFilter = sprintf "(memberOf=%s)" dn
    let memberAttribute = LdapAttribute("member", sprintf "cn=opsbot,%s" searchBase)

    let doLdapThing fn = 
        try
            use ldap = new LdapConnection()
            let connected = System.DateTime.Now
            ldap.Connect("ads.iu.edu", 389)
            ldap.Bind(username, password)  
            let started = System.DateTime.Now
            ldap |> fn
            let now = System.DateTime.Now
            printfn "Time from connect: %f ms" ((now - connected).TotalMilliseconds)
            printfn "Time from fn: %f ms" ((now - started).TotalMilliseconds)
            ldap.Disconnect()
        with exn -> printfn "PUUUUUKE 🤮 %A" exn

    // [<Fact>]
    let ``list group members`` () =
        let fn (ldap:LdapConnection) = 
            printfn "Members of group..."
            let mutable count = 0
            let search = ldap.Search(searchBase, 1, searchFilter, [|"cn"|], false)          
            while search.hasMore() do
                let next = search.next()
                printfn "  %s" (next.getAttribute("cn").StringValue)
                count <- count + 1
            printfn "  Found %d members." count
        doLdapThing fn

    //[<Fact>]
    let ``can add a member to group`` () =
        let addOpsbot (ldap:LdapConnection) =
            printfn "Add member to group..."
            let modification = LdapModification(LdapModification.ADD, memberAttribute)
            ldap.Modify(dn, modification)
        
        ``list group members``()
        doLdapThing addOpsbot
        ``list group members``()
        ()


    // [<Fact>]
    let ``can remove a member from group`` () =
        let removeOpsbot (ldap:LdapConnection) =
            printfn "Remove member from group..."
            let modification = LdapModification(LdapModification.DELETE, memberAttribute)
            ldap.Modify(dn, modification)

        ``list group members``()
        doLdapThing removeOpsbot
        ``list group members``()
        ()

