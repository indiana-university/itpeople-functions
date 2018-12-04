namespace Integration 

module TestFixture =

    open Xunit
    open Xunit.Abstractions
    open Xunit.Sdk
    open Chessie.ErrorHandling
    open Dapper
    open PostgresContainer
    open Functions.Common.Database
    open Functions.Common.Fakes
    open Functions.Common.Types
    open Migrations.Program

    let mutable parksAndRecId = 0
    let mutable cityId = 0
    let mutable fourthFloorId = 0
    let mutable parksDeptId = 0
    let mutable swansonId = 0
    let mutable knopeId = 0
    let mutable sebastianId = 0

    let addTestFakesToDatabase () = 
        clearAndMigrate connectionString
        let db = sqlConnection connectionString
        // units
        parksAndRecId <- db.Insert<Unit>(parksAndRec).GetValueOrDefault()
        cityId <- db.Insert<Unit>(city).GetValueOrDefault()
        fourthFloorId <- db.Insert<Unit>(fourthFloor).GetValueOrDefault()
        // departments
        parksDeptId <- db.Insert<Department>(parksDept).GetValueOrDefault()
        // people
        swansonId <- db.Insert<Person>({swanson with HrDepartmentId=parksDeptId}).GetValueOrDefault()
        knopeId <- db.Insert<Person>({knope with HrDepartmentId=parksDeptId}).GetValueOrDefault()
        sebastianId <- db.Insert<Person>({sebastian with HrDepartmentId=parksDeptId}).GetValueOrDefault()
        // unit membership

        let _ = db.Insert<UnitMember>({UnitId=parksAndRecId; PersonId=swansonId; Role=Role.Leader; Title="Director"; Tools=Tools.AccountMgt; Percentage=100; Name=""; PhotoUrl=""; Description=""})
        let _ = db.Insert<UnitMember>({UnitId=parksAndRecId; PersonId=knopeId; Role=Role.Sublead; Title="Deputy Director"; Tools=Tools.None; Percentage=100; Name=""; PhotoUrl=""; Description=""})
        let _ = db.Insert<UnitMember>({UnitId=parksAndRecId; PersonId=sebastianId; Role=Role.Member; Title="Mascot"; Tools=Tools.None; Percentage=100; Name=""; PhotoUrl=""; Description=""})
        // unit relationsips
        let _ = db.Insert<UnitRelation>({ChildUnitId=parksAndRecId; ParentUnitId=cityId})
        let _ = db.Insert<UnitRelation>({ChildUnitId=fourthFloorId; ParentUnitId=parksAndRecId})
        // support relationship
        let _ = db.Insert<SupportedDepartment>({DepartmentId=parksDeptId; UnitId=parksAndRecId})
        ()

    // Generally:
    // 1. Go fetch the postgres docker image.
    // 2. Create and start a postgres container.
    // 3. Migrate the DB to the latest version
    // 4. Within a test, populate and exercise DB code
    // 5. Stop and remove the container.
    type IntegrationFixture (output: IMessageSink)=
        // A flag to determine whether the Postgres server container was 
        // started prior to running the tests. This will true for tests run 
        // in Circle CI, and (usually) false for tests running locally.
        do
            let log (msg:string) = msg |> System.Console.WriteLine
            ensureDatabaseServerStarted log |> Async.RunSynchronously

    // This collection provides a common interface for all 
    // integration tests so that the postgres server only gets 
    // started/stopped once.
    [<CollectionDefinition("Integration collection")>]
    type IntegrationCollection() =
        interface ICollectionFixture<IntegrationFixture>

    // A base class for all integration tests that clears the
    // database and migrates it to the latest version.
    [<Collection("Integration collection")>]
    type DatabaseIntegrationTestBase() =
        do addTestFakesToDatabase()

