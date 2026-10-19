using System.Globalization;
using OVS.Manager.Localization;

namespace OVS.Manager.Mail;

/// <summary>Package M26: the mails' texts in the user's language ("de" unless the account chose "en").</summary>
internal static class MailTemplates
{
    /// <summary>AC1: the link to set a new password, valid for an hour.</summary>
    public static OutgoingMail Reset(string to, string userName, string? language, string link) =>
        new(to, Text(nameof(Strings.Mail_ResetSubject), language), TextFormat.Of(Text(nameof(Strings.Mail_ResetBody), language), userName, link));

    /// <summary>AC3: the admin's test mail.</summary>
    public static OutgoingMail Test(string to, string? language) => new(to, Text(nameof(Strings.Mail_TestSubject), language), Text(nameof(Strings.Mail_TestBody), language));

    static string Text(string name, string? language) =>
        Strings.ResourceManager.GetString(name, CultureInfo.GetCultureInfo(language is "en" ? "en" : "de")) ?? name;
}
