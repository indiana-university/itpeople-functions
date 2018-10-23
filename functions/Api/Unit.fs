namespace MyFunctions.Api

open Chessie.ErrorHandling
open MyFunctions.Common.Types
open MyFunctions.Common.Jwt
open MyFunctions.Common.Http
open System.Net.Http

/// This module provides functions to fetch and update units and unit memberships
module Unit =

    /// <summary>
    /// Get the the unit associated with a given ID.
    /// </summary>
    /// <param name="req">The HTTP request that triggered this function</param>
    /// <param name="config">The application configuration</param>
    /// <param name="id">The ID of the unit to fetch</param>
    /// <param name="queryUnit">A function to fetch a given unit by its Id</param>
    /// <returns>
    /// A JSON-encoded unit profile
    /// </returns>

    let getById (req: HttpRequestMessage) config id (queryUnit:FetchById<UnitProfile>)  = asyncTrial {
        let! _ = requireMembership config req
        let! result = queryUnit id
        return result |> jsonResponse Status.OK
    }
    
    /// <summary>
    /// Get all units.
    /// </summary>
    /// <param name="req">The HTTP request that triggered this function</param>
    /// <param name="config">The application configuration</param>
    /// <param name="queryUnits">A function to fetch all units</param>
    /// <returns>
    /// A JSON-encoded list of units
    /// </returns>
    let getAll (req: HttpRequestMessage) config (queryUnits:FetchAll<UnitList>) = asyncTrial {
        let! _ = requireMembership config req
        let! result = queryUnits()
        return result |> jsonResponse Status.OK
    }


    
