namespace Functions

open Types
open Jwt
open Database
open Fakes
open Logging
open Util
open Http
open System.Collections.Generic
open System.Diagnostics
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open Chessie.ErrorHandling
open Microsoft.Azure.WebJobs
open Microsoft.Extensions.Configuration
open Serilog.Core
open Newtonsoft.Json


module Api =

    let private getConfiguration(context: ExecutionContext) : AppConfig =
        let configRoot = 
            ConfigurationBuilder()
                .AddJsonFile("local.settings.json", optional=true)
                .AddKeyPerFile("/run/secrets", optional=true)
                .AddEnvironmentVariables()
                .Build();

        {
            OAuth2ClientId = configRoot.["OAuthClientId"]
            OAuth2ClientSecret = configRoot.["OAuthClientSecret"]
            OAuth2TokenUrl = configRoot.["OAuthTokenUrl"]
            OAuth2RedirectUrl = configRoot.["OAuthRedirectUrl"]
            JwtSecret = configRoot.["JwtSecret"]
            DbConnectionString = configRoot.["DbConnectionString"]
            UseFakes = bool.Parse configRoot.["UseFakeData"]
            CorsHosts = configRoot.["CorsHosts"]
        }

    let private getData config =
        if config.UseFakes
        then FakesRepository() :> IDataRepository
        else DatabaseRepository(config.DbConnectionString) :> IDataRepository

    let addCORSHeader (res:HttpResponseMessage) (origin:string) (corsHosts:string) =
        match corsHosts with
        | null -> ()
        | "" -> ()
        | _ ->
            if corsHosts = "*" || corsHosts.Split(',') |> Seq.exists (fun c -> c = origin)
            then 
                res.Headers.Add("Access-Control-Allow-Origin", value=origin)
                res.Headers.Add("Access-Control-Allow-Headers", "origin, content-type, accept, authorization")
                res.Headers.Add("Access-Control-Allow-Credentials", "true")
            else ()

    let origin (req:HttpRequestMessage) =
        if req.Headers.Contains("origin")
        then req.Headers.GetValues("origin") |> Seq.head
        else ""

    /// Construct an HTTP response with JSON content
    let jsonResponse req corsHosts status model = 
        let content = 
            JsonConvert.SerializeObject(model, Json.JsonSettings)
            |> (fun s -> new StringContent(s))
        let response = new HttpResponseMessage(status)
        response.Content <- content
        response.Content.Headers.ContentType <- "application/json" |> MediaTypeHeaderValue;
        addCORSHeader response (origin req) corsHosts
        response

    /// Organize the errors into a status code and a collection of error messages. 
    /// If multiple errors are found, the aggregate status will be that of the 
    /// most severe error (500, then 404, then 400, etc.)
    let failure msgs =
        let l = msgs |> Seq.toList

        // Determine the aggregate status code based on the most severe error.
        let statusCode = 
            if l |> any Status.InternalServerError then Status.InternalServerError
            elif l |> any Status.NotFound then Status.NotFound
            elif l |> any Status.BadRequest then Status.BadRequest
            else l.Head |> fst

        // Flatten all error messages into a single array.
        let errors = 
            l 
            |> Seq.map snd 
            |> Seq.toArray 
            |> (fun es -> { errors = es } )
        
        ( statusCode, errors )

    /// Convert an ROP trial into an HTTP response. 
    /// The result of a successful trial will be passed to the provided success function.
    /// The result(s) of a failed trial will be aggregated, logged, and returned as a 
    /// JSON error message with an appropriate status code.
    let constructResponse (req:HttpRequestMessage) (log:Logger) (corsHosts:string) trialResult elapsed : HttpResponseMessage =
        match trialResult with
        | Ok(result, _) -> 
            logInfo log req Status.OK elapsed
            result |> jsonResponse req corsHosts Status.OK
        | Bad(msgs) -> 
            let (status, errors) = failure (msgs)
            logError log req status elapsed errors
            jsonResponse req corsHosts status errors


    let private resolveDependenciesAndDo (req:HttpRequestMessage) context fn = 
        async {
            let config = getConfiguration context
            let data = getData config
            let log = createLogger config
            let timer = Stopwatch.StartNew()
            try
                let! result = fn config data |> Async.ofAsyncResult
                return constructResponse req log config.CorsHosts result timer.ElapsedMilliseconds
            with
            | exn -> 
                logFatal log req (timer.ElapsedMilliseconds) exn
                return (jsonResponse req "*" Status.InternalServerError "A server error occurred.")
        } |> Async.StartAsTask

    /// Given an API function, resolve required dependencies and get a response.  
    let getAnonymousResponse<'T> req context (fn: AppConfig->IDataRepository->AsyncResult<'T,Error>) =
        resolveDependenciesAndDo req context fn
    
    let doWithAuth<'T> (req:HttpRequestMessage) (config:AppConfig) (fn:JwtClaims->AsyncResult<'T,Error>) = asyncTrial {
        let! user = authorizeRequest config req
        return! fn user
    }

    /// Given an API function, resolve required dependencies and get a response.  
    let getAuthorizedResponse<'T> 
        (req: HttpRequestMessage)
        (context: ExecutionContext) 
        (fn: IDataRepository -> JwtClaims -> AsyncResult<'T,Error>) = 
        resolveDependenciesAndDo req context (fun config data -> doWithAuth req config (fn data))

    /// Given an API function, get a response.  
    let optionsResponse
        (req: HttpRequestMessage)
        (context: ExecutionContext)  = 
            let config = getConfiguration context
            let origin = origin req
            let response = new HttpResponseMessage(Status.OK)
            addCORSHeader response origin config.CorsHosts
            response

    type ResponseModel = {
        access_token: string
    }

    /// Generate a form url-encoded request to exchange the code for a JWT.
    let private createTokenRequest clientId clientSecret redirectUrl code =
        let fn () = 
            dict[
                "grant_type", "authorization_code"
                "code", code
                "client_id", clientId
                "client_secret", clientSecret
                "redirect_uri", redirectUrl
            ]
            |> Dictionary
            |> (fun d-> new FormUrlEncodedContent(d))
        tryf Status.InternalServerError fn

    /// Determine the application role associated with the authenticated user.
    let private getAppRole queryUserByName username = async {
        let! result = queryUserByName username
        match result with
        | Ok(_:Person,_) -> return ok ROLE_USER
        | Bad([(Status.NotFound, _)]) -> return fail (HttpStatusCode.Forbidden, "Only registered IT Pros are allowed to view this informaiton.")
        | Bad(msgs) -> return msgs |> List.head |> fail
    }

    /// Exchange an OAuth code for a UAA JWT. Fetch the user associated with the JWT and roll a new JWT
    /// containing the original JWT, the user ID, and user Role.  
    let getAuthToken (req: HttpRequestMessage) config (queryUserByName:string -> AsyncResult<Person,Error>) = asyncTrial {
        let getUaaJwt request = bindAsyncResult (fun () -> postAsync<ResponseModel> config.OAuth2TokenUrl request)
        let! oauthCode = getQueryParam "oauth_code" req
        let! uaaRequest = createTokenRequest config.OAuth2ClientId config.OAuth2ClientSecret config.OAuth2RedirectUrl oauthCode
        let! uaaJwt = getUaaJwt uaaRequest
        let! uaaClaims = decodeUaaJwt uaaJwt.access_token
        let! user = queryUserByName uaaClaims.UserName
        let! appJwt = encodeJwt config.JwtSecret uaaClaims.Expiration user.Id user.NetId
        let result = { access_token = appJwt }          
        return result
    }