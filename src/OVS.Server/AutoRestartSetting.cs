namespace OVS.Server;

/// <summary>Package 69: the daily restart as set in the administration.</summary>
/// <param name="On">Whether the server restarts every day.</param>
/// <param name="At">When, in server local time.</param>
public readonly record struct AutoRestartSetting(bool On, TimeOnly At);
