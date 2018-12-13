module OrgTypes

type Member =
    { username : string
      title : string
      role : string 
      vacant : bool}

type ChildGroup =
    { cms_id : string
      name : string }

type Group =
    { name : string
      path: string
      id: string
      description : string
      members : Member list
      children : ChildGroup list }

type Groups = Group list