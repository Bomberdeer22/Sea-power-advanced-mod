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

    /// <summary>Which half of the blink cycle the ship is in right now.</summary>
    internal bool BlinkPhase;

    /// <summary>Game-clock time at which the next blink flip is due.</summary>
    internal float NextBlinkAt;

    /// <summary>Radars were on when the blink started, so they can be put back as found.</summary>
    internal bool BlinkRestoreOn;

    /// <summary>Set when the player has taken manual control of the emitters in the meantime.</summary>
    internal bool BlinkCancelled;

    // --- countermeasures -----------------------------------------------------------------------

    /// <summary>Cut the radars automatically when an anti-radiation shot is inbound.</summary>
    internal bool AutoArm;

    /// <summary>Radars are currently cut by the auto-ARM logic rather than by the player.</summary>
    internal bool AutoArmCut;

    /// <summary>When the cut may end, so a volley does not leave the ship blind for ever.</summary>
    internal float AutoArmRestoreAt;

    // --- strike --------------------------------------------------------------------------------

    /// <summary>This ship is waiting for its slot in a deconflicted salvo.</summary>
    internal bool AwaitingSalvoSlot;

    /// <summary>When this ship is released to fire.</summary>
    internal float SalvoReleaseAt;

    // --- readiness -----------------------------------------------------------------------------

    /// <summary>Fraction of a reload to shave while at action stations (0 = none).</summary>
    internal float GqReloadBonus;

    /// <summary>Seconds of battle stations accumulated, for the fatigue model.</summary>
    internal float BattleStationsSeconds;

    // --- deception -----------------------------------------------------------------------------

    /// <summary>Commercial disguise is on and these were the pre-disguise settings.</summary>
    internal bool Disguised;

    internal bool DisguiseRadarsWereOn;

    internal float DisguiseSetAt;

    /// <summary>The signature string the hull had before a disguise, so it can be put back.</summary>
    internal string? OriginalSignature;

    // --- degraded control ----------------------------------------------------------------------

    /// <summary>Guns and CIWS are fighting under local control rather than from the ship's picture.</summary>
    internal bool LocalControl;
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
