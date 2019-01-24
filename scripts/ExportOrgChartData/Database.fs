module Database

open MySql.Data.MySqlClient

let mutable ConnectionString = @"server=localhost;database=orgchart_wcms;user=root;pwd=secret;"

type PageRecord =
    {id: string
     cms_id: string
     metadata_id: string
     name: string
     path: string
     title: string
     description: string
     content: string}

[<Literal>]
let sqlSelectPages = """
   SELECT
       p.id,
       cms_id,
       metadata_id,
       name,
       path,
       title,
       description,
       content
   FROM
       orgchart_wcms.page p
   LEFT JOIN orgchart_wcms.metadata m on p.metadata_id = m.id
   WHERE path not like 'staff%'
"""

let GetPages =
    use rawSqlConnection = new MySqlConnection(ConnectionString)
    rawSqlConnection.Open()
    use command = new MySqlCommand(sqlSelectPages, rawSqlConnection)
    use reader = command.ExecuteReader()
    let results =
        [while reader.Read() do
             yield {id = reader.GetValue(0).ToString()
                    cms_id = reader.GetValue(1).ToString()
                    metadata_id = reader.GetValue(2).ToString()
                    name = reader.GetValue(3).ToString()
                    path = reader.GetValue(4).ToString()
                    title = reader.GetValue(5).ToString()
                    description = reader.GetValue(6).ToString()
                    content = reader.GetValue(7).ToString()}]
    results


let GetPageByPath (p:string) =
    let path = if(p.StartsWith("/")) then p.Substring(1) else p
    let sqlPageByPath = sqlSelectPages + " AND path like '" + path + "'" 
    use rawSqlConnection = new MySqlConnection(ConnectionString)
    rawSqlConnection.Open()
    use command = new MySqlCommand(sqlPageByPath, rawSqlConnection)
    use reader = command.ExecuteReader()
    let results =
        [while reader.Read() do
             yield {id = reader.GetValue(0).ToString()
                    cms_id = reader.GetValue(1).ToString()
                    metadata_id = reader.GetValue(2).ToString()
                    name = reader.GetValue(3).ToString()
                    path = reader.GetValue(4).ToString()
                    title = reader.GetValue(5).ToString()
                    description = reader.GetValue(6).ToString()
                    content = reader.GetValue(7).ToString()}]
    
    if results.IsEmpty then
        None
    else
        Some results.[0]
