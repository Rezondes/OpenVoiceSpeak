using OVS.Server.Data;
using OVS.Server.Logging;
using OVS.Shared.Permissions;
using OVS.Shared.Protocol;
using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Server.State;

/// <summary>Package 168: setting, removing and handing out the users' profile pictures.</summary>
internal sealed partial class ServerState
{
    // ---- Package 168: profile pictures per server ----

    /// <summary>
    /// The own picture is set or removed by anyone; another user's is only removed, with UserDelete and the rank rule.
    /// A picture is stored only when it passes <see cref="AvatarFormat.Validate"/>; uploads are throttled like the logo's.
    /// </summary>
    void OnSetAvatar(Session session, SetAvatar request)
    {
        var target = request.Fingerprint ?? session.Fingerprint;
        bool own = target == session.Fingerprint;
        Session? online = own ? session : null;
        if (!own)
        {
            if (!AvatarStore.IsFingerprint(target))
            {
                Fail(session, request, ReasonCodes.InvalidValue, "No fingerprint.");
                return;
            }
            if (request.PngBase64 is not null)
            {
                Fail(session, request, ReasonCodes.PermissionDenied, "Only the own picture can be set.");
                return;
            }
            if (!Require(session, request, Permission.UserDelete) || !FindKnownTarget(session, request, target, out _, out online)) return;
        }
        if (!ThrottleHeavy(session, request)) return;
        byte[]? png = null;
        if (request.PngBase64 is not null)
        {
            if (!Base64Text.TryDecode(request.PngBase64, out var decoded))
            {
                Fail(session, request, ReasonCodes.InvalidValue, "The picture is not valid Base64.");
                return;
            }
            if (AvatarFormat.Validate(decoded) is { } problem)
            {
                Fail(session, request, ReasonCodes.InvalidValue, problem);
                return;
            }
            png = decoded;
        }
        avatars.Set(target, png);
        if (!own) logs.Server.AvatarRemovedBy(online?.Nickname ?? FindUser(target)?.LastNickname ?? target[..12], session.Nickname);
        else if (png is null) logs.Server.AvatarRemoved(session.Nickname);
        else logs.Server.AvatarChanged(session.Nickname);
        if (online is not null) BroadcastUser(online);
    }

    /// <summary>A picture by its hash, as Base64; null when nobody has it any more. Throttled, many arrive at a connect.</summary>
    void OnGetAvatar(Session session, GetAvatar request)
    {
        if (!Throttle(session, request, ServerTuning.AvatarsPerSecond, ServerTuning.AvatarBurst)) return;
        session.Outbox.Send(new Avatar(request.RequestId, request.Hash, avatars.Find(request.Hash)));
    }
}
