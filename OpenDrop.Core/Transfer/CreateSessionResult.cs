namespace OpenDrop.Core.Transfer;

public sealed record CreateSessionResult(string SessionId, byte[] ReceiverEphemeralPublicKey, byte[] Salt);
