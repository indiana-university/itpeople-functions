namespace ImportOrgData

open Chessie.ErrorHandling
open Dapper
open Npgsql
open Util
open System

module Database =

    type Unit = {
        Name: string
    }

    type Person = {
        NetId: string
    } 

    type UnitRelation = {
        ChildId: int
        ParentId: int
    }

    type UserRelation = {
        UnitId: int
        PersonId: int
        Title: string
        Role: int
    }

    let sqlConnection connectionString =
        SimpleCRUD.SetDialect(SimpleCRUD.Dialect.PostgreSQL)
        new NpgsqlConnection(connectionString)

    /// Fetch a user given a netid (e.g. 'jhoerr')
    let getUitsId connStr = 
        let sql = "Select id From units where name='UITS'"
        use cn = sqlConnection connStr
        let value = cn.QueryFirstOrDefault<int>(sql) 
        match value with
        | 0 -> raise (Exception "UITS missing")
        | v -> v

    let getUserForNetId connStr netId =
        let sql = "Select id from people where netId=@NetId"
        use cn = sqlConnection connStr
        cn.QueryFirstOrDefault<int>(sql, {NetId = netId}) 

    let addUnitToDb connStr name= 
        let sql = """
            with s as (
                select id
                from units
                where name=@name
            ), 
            i as (
                insert into units (name, description, url)
                select @Name, '',''
                where not exists (select 1 from s)
                returning id
            )
            select id from i
            union all
            select id from s
        """
        use cn = sqlConnection connStr
        let value = cn.QueryFirstOrDefault<int>(sql, {Name = name})
        match value with
        | 0 -> raise (Exception (sprintf "could not insert unit %s" name))
        | v -> v

    let addUnitRelation connStr unitId childId = 
        let sql = """
            insert into unit_relations (child_id, parent_id)
               select @ChildId, @ParentId
               where not exists (select 1 from unit_relations
                where child_id=@ChildId AND parent_id=@ParentId)
            
        """
        use cn = sqlConnection connStr
        cn.Execute(sql, {ChildId = childId; ParentId = unitId}) |> ignore
        childId

    let addMemberRelation' connStr unitId personId title role = 
        let sql = """
            insert into unit_members (unit_id, person_id, title, role, percentage, tools)
            Values (@UnitId, @PersonId, @Title, @Role, 100, 0)
            where not exists 
                ( select 1 from unit_members 
                  where unit_id = @UnitId AND person_id = @personId
                )
        """

        use cn = sqlConnection connStr
        
        let paramaters = {
            UnitId = unitId
            PersonId = personId
            Title = title
            Role = role
        }
        cn.Execute(sql, paramaters) |> ignore
        true

    let addMemberRelation connStr unitId netId title role =
        let userId = getUserForNetId connStr netId
        match userId with
        | 0 -> false
        | id -> addMemberRelation' connStr unitId id title role

        
    
    
