namespace OVS.Manager.Api;

/// <summary>Package M31 (AC1): a user's token for the REST API; only the hash of its secret is stored.</summary>
internal sealed class ApiToken
{
    /// <summary>Its id.</summary>
    public string Id { get; set; } = "";

    /// <summary>The user it acts as.</summary>
    public string UserId { get; set; } = "";

    /// <summary>The name the user gave it, in the audit lines of its calls.</summary>
    public string Name { get; set; } = "";

    /// <summary>The SHA-256 of its secret, hex.</summary>
    public string Hash { get; set; } = "";

    /// <summary>When it was made.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>From when it no longer works.</summary>
    public DateTime ExpiresAtUtc { get; set; }
}
