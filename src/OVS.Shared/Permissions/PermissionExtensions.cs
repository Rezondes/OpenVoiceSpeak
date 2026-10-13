namespace OVS.Shared.Permissions;

/// <summary>Set operations on <see cref="Permission"/> flags.</summary>
public static class PermissionExtensions
{
    /// <summary>True when perms holds every flag of required.</summary>
    public static bool Has(this Permission perms, Permission required) => (perms & required) == required;

    /// <summary>True when subset holds no flag outside superset.</summary>
    public static bool IsSubsetOf(this Permission subset, Permission superset) => (subset & ~superset) == 0;

    /// <summary>Package 84 (A102): actions on others only work on users whose rights are a strict subset of the actor's.</summary>
    public static bool CanActOn(this Permission actor, Permission target) => target != actor && target.IsSubsetOf(actor);

    /// <summary>
    /// Package 84 (A102): the one rule for every action on another user (kick, ban, unban, mute, move, group change, delete),
    /// used by the server to decide and by the client to show the actions: never oneself, only strictly weaker users.
    /// </summary>
    public static bool CanModerate(this Permission actor, string actorFingerprint, Permission target, string targetFingerprint) =>
        actorFingerprint != targetFingerprint && actor.CanActOn(target);
}
