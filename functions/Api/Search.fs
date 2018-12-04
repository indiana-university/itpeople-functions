namespace Functions.Api

open Chessie.ErrorHandling
open Functions.Common.Types
open Functions.Common.Jwt
open Functions.Common.Http
open System.Net.Http

module Search =
    /// <summary>
    /// Conduct a simple search of units, departments, and users. 
    /// Will return any unit, department, or user whose name matches any part of 
    /// the 'term' query parameter.
    /// </summary>
    /// <param name="req">The HTTP request that triggered this function</param>
    /// <param name="getSearchResults">A function to fetch matching units, departments, and users provided some search term.</param>
    /// <returns>
    /// A search result, or error information.
    /// </returns>
    let getSimple (req: HttpRequestMessage) (getSearchResults: string -> AsyncResult<SimpleSearch,Error>) = asyncTrial {
        let! term = getQueryParam "term" req
        return! getSearchResults term
    }
