namespace OVS.Shared.Moderation;

/// <summary>Where a ban stands at a given time; <see cref="BanStatusRule"/> decides it for the server and the client.</summary>
public enum BanStatus { Active, Expired, Lifted }
