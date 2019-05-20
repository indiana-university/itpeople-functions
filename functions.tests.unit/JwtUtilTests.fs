// Copyright (C) 2018 The Trustees of Indiana University
// SPDX-License-Identifier: BSD-3-Clause

namespace Tests

open Core.Fakes
open Core.Types
open Functions.Jwt
open System
open Xunit

module JwtUtilTests =

    let name = "johndoe"
    /// NOTE: You can view the contents of these tokens at jwt.io.
    let invalidJwt = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VyX25hbWUiOiJqb2huZG9lIiwiZXhwIjoxNTE2MjM5MDIyfQ.RRdc-M105p8LlK59nqKtCmHlFDEIXTsEha7Y-CcwkNbGqvntdpckkLuuTlGeGgs-QHNzEypTEOoQi-TjFIXmhMhTbXcP5Vo3Ht2qC5h5H4aeQ18fFBAdRaRH_4QEfpitYT7uuUt-xa7cgr0UgJz8aMGI5wskFuCyd7F0D0LFRhkAInLiLNGG0G9PNgHCwcqriQ0qonMbX0DQrnAbfWxl04-GSJ88HmMowuLL9d65Tg-7VE65-UPAOncm2IA_PeVl-gcNJibhikhG9IKiYM0g1W82BQJVLG1HuHnMZR8OzmgCV0oQVTG3jmX8JBjZxITmss0cA0FtD8JBN_3orSaXYZ"
    let expiredJwt = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1c2VyX25hbWUiOiJqb2huZG9lIiwiZXhwIjoxNTE2MjM5MDIyfQ.RRdc-M105p8LlK59nqKtCmHlFDEIXTsEha7Y-CcwkNbGqvntdpckkLuuTlGeGgs-QHNzEypTEOoQi-TjFIXmhMhTbXcP5Vo3Ht2qC5h5H4aeQ18fFBAdRaRH_4QEfpitYT7uuUt-xa7cgr0UgJz8aMGI5wskFuCyd7F0D0LFRhkAInLiLNGG0G9PNgHCwcqriQ0qonMbX0DQrnAbfWxl04-GSJ88HmMowuLL9d65Tg-7VE65-UPAOncm2IA_PeVl-gcNJibhikhG9IKiYM0g1W82BQJVLG1HuHnMZR8OzmgCV0oQVTG3jmX8JBjZxITmss0cA0FtD8JBN_3orSaIOg"

    [<Fact>]
    let ``Decode app JWT`` () =
        let expected = Ok (name)
        let actual = decodeJwt fakePublicKey uaaJwt.access_token |> Async.RunSynchronously
        Assert.Equal(expected, actual)

    [<Fact>]
    let ``Decode app JWT validates signature`` () =
        let expected = Error ((Status.Unauthorized, "Access token is not valid."))
        let actual = decodeJwt fakePublicKey invalidJwt |> Async.RunSynchronously
        Assert.Equal(expected, actual)

    [<Fact>]
    let ``Decode app JWT validates expiration`` () =
        let expected = Error ((Status.Unauthorized, "Access token has expired."))
        let actual = decodeJwt fakePublicKey expiredJwt |> Async.RunSynchronously
        Assert.Equal(expected, actual)

    [<Fact>]
    let ``Parse double`` () =
        let actual = "123" |> System.Double.Parse
        let expected = float 123
        Assert.Equal(expected, actual)
        
    [<Fact>]
    let ``parse public key`` () =
        let actual = importPublicKey fakePublicKey
        Assert.Equal("rsa", actual.KeyExchangeAlgorithm.ToLowerInvariant())

    