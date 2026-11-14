namespace Stockroom.Domain.Topology;

public enum ZoneType
{
    Receiving,
    Storage,
    Picking,
    Shipping,
}

/// <summary>Velocity class: A = fast movers stored closest to the dock, C = slow movers.</summary>
public enum AbcClass
{
    A,
    B,
    C,
}
