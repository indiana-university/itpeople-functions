namespace ImportOrgData

module Util =
    open Chessie.ErrorHandling
    open Types


    let bindAsync<'T> (asyncResult: Async<Result<'T,(Error)>>) = asyncTrial {
        let! result = asyncResult
        let! bound = result
        return bound
    }

    let tryfAsync<'T> msg (fn:unit -> Async<Result<'T,Error>>) = asyncTrial {
        let doAsync msg fn = async {
          try
                let! result = fn()
                return result
            with
            | exn -> return fail (-1, sprintf "%s: %s" msg (exn.Message))
        }
        
        let! result = doAsync msg fn |> bindAsync
        return result
    }

    