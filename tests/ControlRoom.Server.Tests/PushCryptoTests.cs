using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary>RFC 8291 <c>aes128gcm</c> encryption (Appendix A vector and a round trip) and the RFC 8292 VAPID
/// JWT.</summary>
public sealed class PushCryptoTests
{
    // RFC 8291 Appendix A.
    private const string Plaintext = "V2hlbiBJIGdyb3cgdXAsIEkgd2FudCB0byBiZSBhIHdhdGVybWVsb24";
    private const string SenderPublic =
        "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";
    private const string SenderPrivate = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw";
    private const string ReceiverPublic =
        "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    private const string ReceiverPrivate = "q1dXpw3UpT5VOmu_cf_v6ih07Aems3njxI-JWgLcM94";
    private const string Salt = "DGv6ra1nlYgDCS1FRnbzlw";
    private const string AuthSecret = "BTBZMqHH6r4Tts7J_aSIgg";
    private const string Body =
        "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK"
        + "6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN";

    [Fact]
    public void Encrypt_WithTheRfcInputs_GivesTheRfcBody()
    {
        byte[] senderPublic = Decode(SenderPublic);
        using ECDiffieHellman sender = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Decode(SenderPrivate),
            Q = new ECPoint { X = senderPublic[1..33], Y = senderPublic[33..] },
        });

        byte[] body = PushCrypto.Encrypt(
            Decode(Plaintext), Decode(ReceiverPublic), Decode(AuthSecret), sender, Decode(Salt));

        Assert.Equal(Body, Base64Url.Encode(body));
    }

    [Fact]
    public void Encrypt_TheRfcReceiverDecryptsTheRfcBody()
    {
        using PushReceiver receiver = new(Decode(ReceiverPublic), Decode(ReceiverPrivate), Decode(AuthSecret));

        Assert.Equal("When I grow up, I want to be a watermelon", receiver.DecryptText(Decode(Body)));
    }

    [Fact]
    public void Encrypt_WithRandomInputs_RoundTrips()
    {
        using PushReceiver receiver = new();
        PushCrypto crypto = new(new FakeTime(), PushCrypto.SystemEphemeralKey, AccessToken.SystemRandom, null);
        byte[] payload = Encoding.UTF8.GetBytes("{\"kind\":\"test\",\"reason\":\"Test notification\"}");

        byte[] first = crypto.Encrypt(payload, receiver.PublicKey, receiver.Auth);
        byte[] second = crypto.Encrypt(payload, receiver.PublicKey, receiver.Auth);

        Assert.Equal(payload, receiver.Decrypt(first));
        Assert.Equal(payload, receiver.Decrypt(second));
        Assert.NotEqual(first[..16], second[..16]);
        Assert.NotEqual(first[21..86], second[21..86]);
    }

    [Fact]
    public void Jwt_HasTheFixedHeaderAudienceExpiryAndAVerifyingSignature()
    {
        FakeTime time = new();
        PushKeyPair keys = PushCrypto.GenerateKeyPair();
        PushCrypto crypto = new(time, PushCrypto.SystemEphemeralKey, AccessToken.SystemRandom, null);

        string jwt = crypto.Jwt(new Uri("https://web.push.apple.com/QGuQyavXutnMH/abc?x=1"), keys);

        JwtParts parts = JwtParts.Parse(jwt);
        Assert.Equal("{\"typ\":\"JWT\",\"alg\":\"ES256\"}", parts.Header);
        using JsonDocument claims = JsonDocument.Parse(parts.Claims);
        Assert.Equal("https://web.push.apple.com", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal(FakeTime.Start.AddHours(12).ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());
        Assert.Equal(PushCrypto.DefaultSubject, claims.RootElement.GetProperty("sub").GetString());
        Assert.True(parts.Verifies(keys.PublicKey));
    }

    [Fact]
    public void Jwt_UsesTheConfiguredSubjectAndTheClock()
    {
        FakeTime time = new() { Now = FakeTime.Start.AddDays(3) };
        PushKeyPair keys = PushCrypto.GenerateKeyPair();
        PushCrypto crypto = new(time, PushCrypto.SystemEphemeralKey, AccessToken.SystemRandom, "mailto:me@example.org");

        string jwt = crypto.Jwt(new Uri("https://fcm.googleapis.com/fcm/send/x"), keys);

        using JsonDocument claims = JsonDocument.Parse(JwtParts.Parse(jwt).Claims);
        Assert.Equal("https://fcm.googleapis.com", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal(time.Now.AddHours(12).ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());
        Assert.Equal("mailto:me@example.org", claims.RootElement.GetProperty("sub").GetString());
    }

    [Fact]
    public void Authorization_IsVapidWithTokenAndKey()
    {
        PushKeyPair keys = PushCrypto.GenerateKeyPair();
        PushCrypto crypto = new(new FakeTime(), PushCrypto.SystemEphemeralKey, AccessToken.SystemRandom, null);

        string header = crypto.Authorization(new Uri("https://updates.push.services.mozilla.com/wpush/v2/x"), keys);

        Assert.StartsWith("vapid t=", header, StringComparison.Ordinal);
        Assert.EndsWith(", k=" + Base64Url.Encode(keys.PublicKey), header, StringComparison.Ordinal);
    }

    [Fact]
    public void IsValidPublicKey_RefusesPointsOffTheCurveAndWrongForms()
    {
        byte[] good = Decode(ReceiverPublic);
        byte[] offCurve = (byte[])good.Clone();
        offCurve[64] ^= 1;
        byte[] compressed = (byte[])good.Clone();
        compressed[0] = 0x02;

        Assert.True(PushCrypto.IsValidPublicKey(good));
        Assert.False(PushCrypto.IsValidPublicKey(offCurve));
        Assert.False(PushCrypto.IsValidPublicKey(compressed));
        Assert.False(PushCrypto.IsValidPublicKey(good[..64]));
        Assert.False(PushCrypto.IsValidPublicKey(new byte[65]));
    }

    private static byte[] Decode(string text)
    {
        return Base64Url.Decode(text)!;
    }
}

/// <summary>A JWT split into its decoded header and claims and its signature.</summary>
internal sealed class JwtParts
{
    private JwtParts(string signingInput, string header, string claims, byte[] signature)
    {
        SigningInput = signingInput;
        Header = header;
        Claims = claims;
        Signature = signature;
    }

    public string SigningInput { get; }

    public string Header { get; }

    public string Claims { get; }

    public byte[] Signature { get; }

    public static JwtParts Parse(string jwt)
    {
        string[] parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);
        return new JwtParts(
            parts[0] + "." + parts[1],
            Encoding.UTF8.GetString(Base64Url.Decode(parts[0])!),
            Encoding.UTF8.GetString(Base64Url.Decode(parts[1])!),
            Base64Url.Decode(parts[2])!);
    }

    /// <summary>True when the ES256 (r || s) signature verifies with the uncompressed public point.</summary>
    public bool Verifies(byte[] publicKey)
    {
        Assert.Equal(65, publicKey.Length);
        Assert.Equal(0x04, publicKey[0]);
        using ECDsa key = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..] },
        });
        return key.VerifyData(
            Encoding.ASCII.GetBytes(SigningInput), Signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}
