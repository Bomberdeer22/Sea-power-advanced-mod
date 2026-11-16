using System;
using System.Runtime.CompilerServices;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// State the mod owns about a unit, for everything the game does not model.
///
/// The readiness report found two real gaps: "general quarters is not a settable state" and no crew
/// fatigue. Those are not blockers - they are state nobody keeps. This keeps it, keyed on the unit
/// itself so it disappears with the unit and needs no cleanup, saving or versioning.
///
/// The rule that keeps this honest: mod-owned state may only ever *drive* the game's own levers
/// (reload timers, emitter switches, throttle). It never pretends to be something the simulation
/// reads, because nothing else would read it.
/// </summary>
internal sealed class UnitRecord
{
    /// <summary>General quarters: the ship is closed up and expecting action.</summary>
    internal bool GeneralQuarters;

    /// <summary>0 = rested, 1 = exhausted. Only ever consumed by features that act on it.</summary>
    internal float Fatigue;

    /// <summary>Ship is alternating sprint and drift for ASW.</summary>
    internal bool SprintDrift;

    /// <summary>Currently in the fast half of the cycle rather than the quiet half.</summary>
    internal bool SprintPhase;

    /// <summary>Game-clock time at which the next phase flip is due.</summary>
    internal float NextPhaseAt;

    /// <summary>Radar blink is cycling this unit's emitters.</summary>
    internal bool Blink;

    /// <summary>Game-clock time at which the next blink flip is due.</summary>
    internal float NextBlinkAt;

    /// <summary>Radars were on when the blink started, so they can be put back as found.</summary>
    internal bool BlinkRestoreOn;

    /// <summary>Set when the player has taken manual control of the emitters in the meantime.</summary>
    internal bool BlinkCancelled;
}

internal static class UnitState
{
    private static readonly ConditionalWeakTable<ObjectBase, UnitRecord> Table =
        new ConditionalWeakTable<ObjectBase, UnitRecord>();

    /// <summary>The record for this unit, created empty on first use.</summary>
    internal static UnitRecord Of(ObjectBase unit) => Table.GetValue(unit, _ => new UnitRecord());

    /// <summary>The record only if something has already been stored for this unit.</summary>
    internal static bool TryGet(ObjectBase? unit, out UnitRecord record)
    {
        if (unit != null && Table.TryGetValue(unit, out UnitRecord stored))
        {
            record = stored;
            return true;
        }
        record = null!;
        return false;
    }

    internal static void Forget(ObjectBase unit) => Table.Remove(unit);
}
