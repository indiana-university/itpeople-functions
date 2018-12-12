namespace Tests

open Chessie.ErrorHandling
open Functions.Util
open Functions.Types
open Functions.Jwt
open System
open Xunit

module JwtUtilTests =

    let name = "johndoe"
    let id = 1

    let person = 
      { Person.Id=id
        NetId=name
        Name="" 
        Hash=""
        Campus=""
        CampusEmail=""
        CampusPhone=""
        Responsibilities=Responsibilities.None
        Tools=Tools.None
        Position=""
        Location=""
        Expertise=""
        Notes=""
        PhotoUrl=""
        HrDepartmentId=0 }

    let expiration = DateTime(2030,9,13,15,44,03,DateTimeKind.Utc)

    /// NOTE: You can view the contents of these tokens at jwt.io.
    let expiredJwt = "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJleHAiOiIxNTE1NTQ0NjQzIiwidXNlcl9uYW1lIjoiam9obmRvZSIsInVzZXJfcm9sZSI6ImFkbWluIn0.rz4RXtrGr1WfX0tUBAu2yj-KU7u1gqwZ4oWInm2vd-4"

    [<Fact>]
    let ``Decode UAA JWT`` () =
        let expected = Ok ({ UserName=name; UserId=0; Expiration=expiration; }, [])
        let actual = decodeUaaJwt {access_token = TestFakes.validJwt}
        Assert.Equal(expected, actual)

    [<Fact>]
    let ``Encode app JWT`` () =
        let expected = Ok ({access_token = TestFakes.validJwt}, [])
        let actual = encodeAppJwt TestFakes.jwtSingingSecret expiration person
        Assert.Equal(expected, actual)

    [<Fact>]
    let ``Decode app JWT`` () =
        let expected = Ok ({ UserName=name; UserId=1; Expiration=expiration }, [])
        let actual = decodeAppJwt TestFakes.jwtSingingSecret TestFakes.validJwt
        Assert.Equal(expected, actual)

    [<Fact>]
    let ``Decode app JWT validates signature`` () =
        let expected = Bad ([(Status.Unauthorized, "Access token has invalid signature")])
        let actual = decodeAppJwt "different signing secret" TestFakes.validJwt
        Assert.Equal(expected, actual)

    [<Fact>]
    let ``Decode app JWT validates expiration`` () =
        let expected = Bad ([(Status.Unauthorized, "Access token has expired")])
        let actual = decodeAppJwt TestFakes.jwtSingingSecret expiredJwt
        Assert.Equal(expected, actual)


    [<Fact>]
    let ``Parse double`` () =
        let actual = "123" |> System.Double.Parse
        let expected = float 123
        Assert.Equal(expected, actual)
        

    