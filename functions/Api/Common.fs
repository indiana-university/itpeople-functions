namespace MyFunctions.Api

open Chessie.ErrorHandling
open MyFunctions.Common.Types
open MyFunctions.Common.Jwt
open System.Net.Http

module Common =


    /// <summary>
    /// Get all items.
    /// </summary>
    /// <param name="req">The HTTP request that triggered this function</param>
    /// <param name="config">The application configuration</param>
    /// <param name="fn">A function to fetch all items</param>
    /// <returns>
    /// A collection of items, or error information.
    /// </returns>
    let getAll<'T> (req:HttpRequestMessage) (config:AppConfig) (fn:unit->AsyncResult<'T,Error>) = asyncTrial {
        let! _ = requireMembership config req
        let! result = fn ()
        return result
    }

    /// <summary>
    /// Get a single item by ID.
    /// </summary>
    /// <param name="req">The HTTP request that triggered this function</param>
    /// <param name="config">The application configuration</param>
    /// <param name="fn">A function to fetch a given item by its Id</param>
    /// <returns>
    /// A single item, or error information.
    /// </returns>
    let getById<'T> (req:HttpRequestMessage) (config:AppConfig) (id:Id) (fn:Id->AsyncResult<'T,Error>) = asyncTrial {
        let! _ = requireMembership config req
        let! result = fn id
        return result
    }

