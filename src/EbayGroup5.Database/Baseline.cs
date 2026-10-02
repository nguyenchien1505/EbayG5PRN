using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EbayGroup5.Database;

public static class Baseline
{
    public const string Sha256 = "D56EC9C0FFCEA721E2F7BA3453249794F03AFEB1F17CC4A2F9EBD0ADA38E0754";

    public static string ReadTableDdl()
    {
        using var source = typeof(Baseline).Assembly.GetManifestResourceStream("Baseline.Source.sql")
            ?? throw new InvalidOperationException("Baseline resource missing.");
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (Convert.ToHexString(SHA256.HashData(bytes)) != Sha256)
            throw new InvalidOperationException("Baseline checksum changed.");
        var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        var prefix = Regex.Match(text, @"\A\s*CREATE DATABASE CloneEbayDB;\s*GO\s*USE CloneEbayDB;\s*GO\s*", RegexOptions.CultureInvariant);
        if (!prefix.Success) throw new InvalidOperationException("Unexpected baseline database preamble.");
        return text[prefix.Length..];
    }
}
