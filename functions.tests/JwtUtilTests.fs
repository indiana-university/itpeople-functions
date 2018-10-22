namespace Tests

open Chessie.ErrorHandling
open MyFunctions.Common
open MyFunctions.Types
open System
open Xunit

module JwtUtilTests =

    let name = "johndoe"
    let id = 1
    let expiration = DateTime(2030,9,13,15,44,03,DateTimeKind.Utc)
    let secret = "jwt signing secret"

    /// NOTE: You can view the contents of these tokens at jwt.io.
    
    // This token payload is: { "user_name":"johndoe", "user_id":1, "exp":1915544643 }
    let jwt = "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJleHAiOiIxOTE1NTQ0NjQzIiwidXNlcl9pZCI6IjEiLCJ1c2VyX25hbWUiOiJqb2huZG9lIn0.9uerDlhPKrtBrMMHuRoxbJ5x0QA7KOulDEHx9DKXpnQ"
    let expiredJwt = "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJleHAiOiIxNTE1NTQ0NjQzIiwidXNlcl9uYW1lIjoiam9obmRvZSIsInVzZXJfcm9sZSI6ImFkbWluIn0.rz4RXtrGr1WfX0tUBAu2yj-KU7u1gqwZ4oWInm2vd-4"

    [<Fact>]
    let ``Decode UAA JWT`` () =
        let expected = Ok ({ UserName=name; UserId=0; Expiration=expiration; }, [])
        let actual = decodeUaaJwt jwt
        Assert.Equal(expected, actual)

    [<Fact>]
    let ``Encode app JWT`` () =
        let expected = Ok (jwt, [])
        let actual = encodeJwt secret expiration id name
        Assert.Equal(expected, actual)

    [<Fact>]
    let ``Decode app JWT`` () =
        let expected = Ok ({ UserName=name; UserId=1; Expiration=expiration }, [])
        let actual = decodeAppJwt secret jwt
        Assert.Equal(expected, actual)

    [<Fact>]
    let ``Decode app JWT validates signature`` () =
        let expected = Bad ([(Status.Unauthorized, "Access token has invalid signature")])
        let actual = decodeAppJwt "different signing secret" jwt
        Assert.Equal(expected, actual)

    [<Fact>]
    let ``Decode app JWT validates expiration`` () =
        let expected = Bad ([(Status.Unauthorized, "Access token has expired")])
        let actual = decodeAppJwt secret expiredJwt
        Assert.Equal(expected, actual)


    [<Fact>]
    let ``Parse double`` () =
        let actual = "123" |> System.Double.Parse
        let expected = float 123
        Assert.Equal(expected, actual)
        

    