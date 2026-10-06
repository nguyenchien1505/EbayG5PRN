namespace EbayGroup5.Web.Chat;

public sealed class ChatOriginPolicy
{
    private readonly HashSet<string> origins;
    public ChatOriginPolicy(IConfiguration configuration)
    {
        var configured = configuration.GetSection("Chat:AllowedOrigins").Get<string[]>() ?? [];
        if (configured.Length == 0 || configured.Any(x => !IsOrigin(x))) throw new InvalidOperationException("Valid chat origins are required.");
        origins = new(configured, StringComparer.Ordinal);
    }
    private static bool IsOrigin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && uri.UserInfo == "" && uri.Query == "" && uri.Fragment == "" &&
        uri.AbsolutePath == "/" && value == uri.GetLeftPart(UriPartial.Authority);
    public bool Allows(string? origin) => origin != null && origins.Contains(origin);
}
