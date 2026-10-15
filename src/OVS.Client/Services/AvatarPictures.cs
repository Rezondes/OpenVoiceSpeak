using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Client.Services;

/// <summary>
/// Package 168: the profile pictures of the connected server by hash. A picture not here yet is asked for once; one that
/// arrives is taken only when it passes <see cref="AvatarFormat.Validate"/> and matches its hash, so every client draws
/// only a checked PNG. Kept for the connection (each server has its own pictures).
/// </summary>
/// <param name="ask">Sends GetAvatar to the server.</param>
internal sealed class AvatarPictures(Func<GetAvatar, Task> ask)
{
    readonly Dictionary<AvatarHash, byte[]> pictures = [];
    readonly HashSet<AvatarHash> asked = [];

    /// <summary>A picture arrived; the rows showing its hash take it now.</summary>
    public event Action<AvatarHash, byte[]>? Arrived;

    /// <summary>The picture of <paramref name="hash"/>, null while it is not here (it is asked for then) or for no hash.</summary>
    public byte[]? Png(AvatarHash? hash)
    {
        if (hash is not { } wanted) return null;
        if (pictures.TryGetValue(wanted, out var png)) return png;
        if (asked.Add(wanted)) _ = ask(new GetAvatar(wanted));
        return null;
    }

    /// <summary>The server's answer: kept and told when it is a valid picture with the hash asked for.</summary>
    public void Receive(Avatar avatar)
    {
        if (avatar.PngBase64 is null || !Base64Text.TryDecode(avatar.PngBase64, out var png)) return;
        if (AvatarFormat.Validate(png) is not null || AvatarHash.Of(png) != avatar.Hash) return;
        pictures[avatar.Hash] = png;
        Arrived?.Invoke(avatar.Hash, png);
    }
}
