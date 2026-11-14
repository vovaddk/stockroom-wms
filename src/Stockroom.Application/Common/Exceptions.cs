namespace Stockroom.Application.Common;

/// <summary>Mapped to HTTP 404.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>The request clashes with existing data (e.g. duplicate document number). Mapped to HTTP 409.</summary>
public sealed class ConflictException(string message) : Exception(message);
