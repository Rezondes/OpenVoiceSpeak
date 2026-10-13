namespace OVS.Client.Dialogs;

/// <param name="Save">Package 39: store the password with the bookmark, after a successful connection only.</param>
/// <param name="Password">The password typed.</param>
public sealed record PasswordAnswer(string Password, bool Save);
