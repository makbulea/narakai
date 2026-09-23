using System.Security.Cryptography;
using System.Text;

namespace CodeIntelligence.Rag.Hashing;

/// <summary>
/// Deterministic content hashing used to detect unchanged chunks so the indexing
/// pipeline can skip re-embedding them. See docs/incremental-indexing.md.
/// </summary>
public static class ContentHasher
{
    public static string Sha256Hex(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
