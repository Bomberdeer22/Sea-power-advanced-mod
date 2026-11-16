using SeaPower;

namespace SeaPowerAdvanced.Features;

/// <summary>
/// One jammer pointed at one thing, as the map overlay needs it: where the beam starts, what it is
/// aimed at, and whether it is transmitting.
///
/// The target is an <see cref="object"/> because the two kinds of target are different types the mod
/// cannot name - a missile for the defensive set, a ship for the offensive one - and the overlay only
/// needs to ask it for a position.
/// </summary>
internal readonly struct EcmBeam
{
    internal EcmBeam(ObjectBase jammer, object? target, bool transmitting, bool offensive)
    {
        Jammer = jammer;
        Target = target;
        Transmitting = transmitting;
        Offensive = offensive;
    }

    /// <summary>The ship with the jammer.</summary>
    internal ObjectBase Jammer { get; }

    /// <summary>The missile or ship the beam is held on.</summary>
    internal object? Target { get; }

    /// <summary>True when the set is actually radiating, false when it is locked but silent.</summary>
    internal bool Transmitting { get; }

    /// <summary>True for the offensive set (cyan), false for the defensive one (amber).</summary>
    internal bool Offensive { get; }
}
