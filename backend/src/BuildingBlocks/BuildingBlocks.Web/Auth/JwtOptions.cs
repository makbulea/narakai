namespace BuildingBlocks.Web.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "ecommerce-local";
    public string Audience { get; set; } = "ecommerce-api";

    /// <summary>
    /// Symmetric signing key, supplied through configuration/environment only.
    ///
    /// Symmetric is a deliberate local-development shortcut. A real deployment uses an
    /// identity provider (Keycloak) issuing RS256 tokens, at which point this becomes a
    /// JWKS URL and nothing else in the codebase changes — validation already happens
    /// in one place.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 60;
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Customer = "Customer";
    public const string Support = "Support";

    /// <summary>
    /// A service calling another service on its own behalf, with no user behind the
    /// request. Granted read access across boundaries but never catalogue writes or
    /// refunds — a compromised service should not be able to issue money back.
    /// </summary>
    public const string Service = "Service";
}

public static class Policies
{
    /// <summary>Write access to catalogue and stock. Admin only.</summary>
    public const string ManageCatalog = "ManageCatalog";

    /// <summary>Read access to any customer's data. Admin and Support.</summary>
    public const string ViewAnyCustomer = "ViewAnyCustomer";

    /// <summary>Placing and cancelling orders. Any authenticated role.</summary>
    public const string PlaceOrders = "PlaceOrders";

    /// <summary>Refunds. Admin and Support — deliberately not Customer.</summary>
    public const string IssueRefunds = "IssueRefunds";
}
