namespace Database

module Migration =

    open Npgsql
    open SimpleMigrations
    open SimpleMigrations.Console
    open SimpleMigrations.DatabaseProvider
    open System.Reflection

    let migrator db = 
        let provider = PostgresqlDatabaseProvider(db)
        let assembly = Assembly.GetExecutingAssembly()
        let migrator = SimpleMigrator(assembly, provider)
        migrator

    let migrate connection args =
        try
            use db = new NpgsqlConnection(connection)
            let runner = db |> migrator |> ConsoleRunner
            args |> List.toArray |> runner.Run
            0
        with
        | exn -> 
            printf "Error: %s" exn.Message
            1

