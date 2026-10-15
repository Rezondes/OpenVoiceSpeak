using System.Globalization;
using OVS.Client.ViewModels;

namespace OVS.Client.Models;

/// <summary>Package 167: one heading of the member list (a group, or "Online") with its members.</summary>
/// <param name="Title">The group's name, or "Online".</param>
/// <param name="Members">The members under it, by name.</param>
internal sealed record MemberSection(string Title, IReadOnlyList<UserViewModel> Members)
{
    /// <summary>The heading as shown: the title in capitals and the count.</summary>
    public string Heading => $"{Title.ToUpper(CultureInfo.CurrentCulture)} ({Members.Count})";

    /// <inheritdoc/>
    public bool Equals(MemberSection? other) => other is not null && Title == other.Title && Members.SequenceEqual(other.Members);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Title, Members.Count);
}
