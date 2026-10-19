namespace OVS.Manager.Accounts;

/// <summary>Package M28: one setting of the manager itself, such as whether two-factor login is required.</summary>
internal sealed class ManagerSetting
{
    /// <summary>The setting's name.</summary>
    public string Key { get; set; } = "";

    /// <summary>Its value.</summary>
    public string Value { get; set; } = "";
}
