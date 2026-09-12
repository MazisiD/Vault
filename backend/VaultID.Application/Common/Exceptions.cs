namespace VaultID.Application.Common;

/// <summary>The requested resource (vault, organisation, grant) was not found.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>The request violated a business rule or was malformed.</summary>
public sealed class ValidationException(string message) : Exception(message);

/// <summary>A precondition for the operation was not met (e.g. agreement not signed).</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>
/// The caller has made too many failed attempts and is being throttled. Used by
/// the share-code redemption endpoint so it cannot be run as a guessing oracle.
/// </summary>
public sealed class TooManyAttemptsException(string message) : Exception(message);
