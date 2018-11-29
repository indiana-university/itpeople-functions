namespace Integration

module ContractTests =
    open PactNet
    open PactNet.Infrastructure.Outputters
    open Xunit.Abstractions
    open Xunit
    open TestFixture
    open TestHost
    open System
    open PostgresContainer
    open Dapper
    open Npgsql
    open Functions.Common.Types
    open Functions.Common.Fakes
    open Functions.Common.Database
    open Migrations.Program

    
    type XUnitOutput(output: ITestOutputHelper)=
        let output = output
        interface IOutput with  
            member this.WriteLine(message: string)=
                message |> output.WriteLine
    
    let readyDatabaseState connectionString = 
        clearAndMigrate connectionString
        let db = sqlConnection connectionString
        // units
        let parksAndRecId = db.Insert<Unit>(parksAndRec).GetValueOrDefault()
        let cityId = db.Insert<Unit>(city).GetValueOrDefault()
        let fourthFloorId = db.Insert<Unit>(fourthFloor).GetValueOrDefault()
        // departments
        let parksDeptId = db.Insert<Department>(parksDept).GetValueOrDefault()
        // people
        let swansonId = db.Insert<Person>({swanson with HrDepartmentId=parksDeptId}).GetValueOrDefault()
        let knopeId = db.Insert<Person>({knope with HrDepartmentId=parksDeptId}).GetValueOrDefault()
        let sebastianId = db.Insert<Person>({sebastian with HrDepartmentId=parksDeptId}).GetValueOrDefault()
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

    let verifyPact functionPort output = 
        let functionUrl = sprintf "http://localhost:%d" functionPort
        let outputters = ResizeArray<IOutput> [XUnitOutput(output) :> IOutput]
        let verifier = PactVerifierConfig(Outputters=outputters, Verbose=true) |> PactVerifier
        verifier
            .ServiceProvider("API", functionUrl)
            .HonoursPactWith("Client")
            .PactUri("https://raw.githubusercontent.com/indiana-university/itpeople-app/develop/contracts/itpeople-app-itpeople-functions.json")
            .Verify()

    type Pact(output: ITestOutputHelper)=
        inherit HttpIntegrationTestBase()
        let output = output

        [<Fact>]
        member __.``Test Contracts`` () = async {
            let functionScriptPath = "../../../../functions/bin/Debug/netcoreapp2.1"
            let functionServerPort = 9091
            let mutable functionServer = None

            try            
                Environment.SetEnvironmentVariable("CorsHosts","*")
                Environment.SetEnvironmentVariable("UseFakeData","false")
                Environment.SetEnvironmentVariable("JwtSecret","jwt signing secret")
                Environment.SetEnvironmentVariable("DbConnectionString",connectionString)

                "---> Preparing database..." |> output.WriteLine
                do readyDatabaseState connectionString
                "---> Starting functions hst..." |> output.WriteLine
                let! functionsServer = startTestServer functionServerPort functionScriptPath output
                "---> Verifying contract..." |> output.WriteLine
                verifyPact functionServerPort output
            finally
                stopTestServer functionServer
        }