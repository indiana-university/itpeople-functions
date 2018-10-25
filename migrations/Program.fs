namespace Migrations
    
module Program =

    open Npgsql
    open SimpleMigrations
    open SimpleMigrations.Console
    open SimpleMigrations.DatabaseProvider
    open System.Reflection

    /// Migrate an MSSql (SQL Server) database
    let migrate connection args =
        try
            use db = new NpgsqlConnection(connection)
            let provider = PostgresqlDatabaseProvider(db)
            let assembly = Assembly.GetExecutingAssembly()
            let migrator = SimpleMigrator(assembly, provider)
            let runner = ConsoleRunner(migrator)
            args |> List.toArray |> runner.Run
        with
        | exn -> 
            printf "Error: %s" exn.Message

    [<EntryPoint>]
    let main argv =

        match argv |> List.ofSeq with
        | connection :: args->
            migrate connection args
        | _ ->
            printf """Usage : dotnet database.dll '<conn>' <args>

    <conn>: the Postgres database connection string
    <args>: SimpleMigration args (try 'help')
    """

        0
