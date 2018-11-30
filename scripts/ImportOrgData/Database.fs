namespace ImportOrgData

open Chessie.ErrorHandling
open Dapper
open Npgsql
open Util
open System

module Database =

    type Unit = {
        [<Column("name")>] Name: string
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

    
    
