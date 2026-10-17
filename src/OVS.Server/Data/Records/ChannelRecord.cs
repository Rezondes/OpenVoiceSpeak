using System.Text.Json.Serialization;
using OVS.Server.Security;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Channels;

namespace OVS.Server.Data.Records;

/// <summary>A channel as stored.</summary>
internal sealed class ChannelRecord
{
    /// <summary>The channel's id.</summary>
    public Guid Id { get; set; }
    /// <summary>The channel's name.</summary>
    public string Name { get; set; } = "";
    /// <summary>The channel's description.</summary>
    public string Description { get; set; } = "";
    /// <summary>Its place in the channel list.</summary>
    public int Order { get; set; }
    /// <summary>Package 34: nobody in this channel is heard.</summary>
    public bool IsMuted { get; set; }
    /// <summary>Package 35: 0 = unlimited. The default channel is always unlimited.</summary>
    public int MaxUsers { get; set; }
    /// <summary>
    /// Package 93 (A109): null = no group lock. Otherwise only members of one of these groups (and admins) may join;
    /// empty once its groups were deleted, which means admins only. Older files load as null, without a new data version.
    /// </summary>
    public List<Guid>? AllowedGroupIds { get; set; }
    /// <summary>Package 94 (A109): salted PBKDF2 like the server password; null = no password lock.</summary>
    [JsonPropertyName("passwordHash")]
    public StoredPassword? StoredPassword { get; set; }
    /// <summary>Package 111: older files load as voice channels, without a new data version.</summary>
    public ChannelKind Kind { get; set; }
    /// <summary>
    /// Package 132 (A141): kbps; older files load as the default (the channels moved from the old fixed 32 kbps), without
    /// a new data version; Package 183 (A194): the default is 128. A separator gets 0 and is never asked for it.
    /// </summary>
    public int Bitrate { get; set; } = ProtocolDefaults.Bitrate;

    /// <summary>Constant-time in both formats; true when there is no password.</summary>
    public bool CheckPassword(string? password) => StoredPassword?.Matches(password) ?? true;
}
