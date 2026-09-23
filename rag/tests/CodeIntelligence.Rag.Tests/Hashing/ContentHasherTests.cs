using System.Text.RegularExpressions;
using CodeIntelligence.Rag.Hashing;

namespace CodeIntelligence.Rag.Tests.Hashing;

public sealed partial class ContentHasherTests
{
    [Fact]
    public void Sha256Hex_SameInput_ProducesSameHash()
    {
        const string content = "public void Foo() { }";

        var first = ContentHasher.Sha256Hex(content);
        var second = ContentHasher.Sha256Hex(content);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Sha256Hex_DifferentInput_ProducesDifferentHash()
    {
        var first = ContentHasher.Sha256Hex("public void Foo() { }");
        var second = ContentHasher.Sha256Hex("public void Bar() { }");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Sha256Hex_SingleCharacterDifference_ProducesDifferentHash()
    {
        var first = ContentHasher.Sha256Hex("a");
        var second = ContentHasher.Sha256Hex("b");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Sha256Hex_ReturnsSixtyFourLowercaseHexChars()
    {
        var hash = ContentHasher.Sha256Hex("some content");

        Assert.Equal(64, hash.Length);
        Assert.Matches(HexPattern(), hash);
        Assert.Equal(hash, hash.ToLowerInvariant());
    }

    [Fact]
    public void Sha256Hex_EmptyString_ReturnsKnownSha256OfEmptyInput()
    {
        // SHA-256("") is a well-known constant; pinning it catches any accidental algorithm change.
        var hash = ContentHasher.Sha256Hex(string.Empty);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", hash);
    }

    [Fact]
    public void Sha256Hex_KnownVector_MatchesExpectedDigest()
    {
        // SHA-256("abc") is another well-known test vector from FIPS 180-4.
        var hash = ContentHasher.Sha256Hex("abc");
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex HexPattern();
}
