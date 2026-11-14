namespace Stockroom.Domain.Common;

/// <summary>A business rule was violated. Mapped to HTTP 422.</summary>
public class DomainException(string message) : Exception(message);

/// <summary>An entity was asked to move to a state its lifecycle does not allow. Mapped to HTTP 409.</summary>
public sealed class InvalidStateTransitionException(string entity, object from, object to)
    : DomainException($"{entity} cannot move from {from} to {to}.");
