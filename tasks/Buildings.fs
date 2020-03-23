namespace Tasks

module Buildings =

    open System
    open System.Net.Http
    open System.Net.Http.Headers
    open Core.Types
    open Core.Util
    open Database.Command
    open Logging

    type DenodoResponse<'T> =
      { name: string
        elements: seq<'T> }

    type DenodoBuilding =
      { building_code: string
        site_code: string
        building_name: string
        building_long_description: string
        street: string
        city: string
        state: string
        zip: string }

    let getDenodoResponse url user password = 
        let req = new HttpRequestMessage(HttpMethod.Get, url|>Uri)
        let basicauth = 
            sprintf "%s:%s" user password
            |> Text.Encoding.GetEncoding("ISO-8859-1").GetBytes
            |> Convert.ToBase64String            
        req.Headers.Authorization <- AuthenticationHeaderValue("Basic", basicauth)
        sendAsync<DenodoResponse<DenodoBuilding>> req        

    let mapToDomainBuilding (denodoBuildings:DenodoResponse<DenodoBuilding>) =
        let valueOrEmpty str = if isNull str then "" else str
        
        denodoBuildings.elements
        |> Seq.filter (fun e -> not (isNull e.building_name || isNull e.building_code))
        |> Seq.map (fun e -> 
            { Id=0
              Building.Name = e.building_long_description 
              Code = e.building_code
              Address = valueOrEmpty e.street
              City = valueOrEmpty e.site_code
              State = ""
              PostCode = ""
              Country = "" } )
        |> ok

    let fetchAllBuildings url user password = pipeline {
        let! denodoBuildings = getDenodoResponse url user password
        return! denodoBuildings |> mapToDomainBuilding 
    }
    let updateBuildingRecords connStr (buildings:seq<Building>) = 
        let sql = 
            """INSERT INTO buildings (name, code, address, city, state, post_code, country) VALUES
               (@Name, @Code, @Address, @City, @State, @PostCode, @Country)
               ON CONFLICT(code) DO UPDATE SET 
               name = EXCLUDED.name, 
               address = EXCLUDED.address,
               city = EXCLUDED.city,
               state = EXCLUDED.state,
               post_code = EXCLUDED.post_code,
               country = EXCLUDED.country;
               """
        execute connStr sql buildings
    let updateBuildings connStr buildingUrl buildingUser buildingPassword (log:Serilog.ILogger) = pipeline {
        log |> logInfo "Fetching buildings from Denodo..." None
        let! buildings = fetchAllBuildings buildingUrl buildingUser buildingPassword
        log |> logInfo (sprintf "Fetched %d buildings from Denodo." (buildings |> Seq.length)) None
        return! updateBuildingRecords connStr buildings
    }