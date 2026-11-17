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
/// Every field here is written by exactly the feature that owns it and read by the same feature or
/// the ticker. Four fields that nothing ever wrote were removed when the compiler started warning
/// about them (CS0649): state nobody changes is not state, it is a comment that lies.
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

    /// <summary>Radar blink is cycling this unit's emitters.</summary>
    internal bool Blink;

    /// <summary>
    /// How many weapons the missile shield has broken or taken down for this unit.
    ///
    /// Kept per unit, and not on the weapon, because the weapon does not outlive the tick it is
    /// stopped in. It is what stops a shield having to re-explain the same break every 50 ms while
    /// the game's own lists catch up - which is a log flood, not a second effect.
    /// </summary>
    internal int ShieldStops;

    /// <summary>Which half of the blink cycle the ship is in right now.</summary>
    internal bool BlinkPhase;

    /// <summary>Blink is cycling the air-search radar (the player chose which set to snapshot).</summary>
    internal bool BlinkAir;

    /// <summary>Blink is cycling the surface-search radar.</summary>
    internal bool BlinkSurface;

    /// <summary>Game-clock time at which the next blink flip is due.</summary>
    internal float NextBlinkAt;

    // --- countermeasures -----------------------------------------------------------------------

    /// <summary>Cut the radars automatically when an anti-radiation shot is inbound.</summary>
    internal bool AutoArm;

    /// <summary>Radars are currently cut by the auto-ARM logic rather than by the player.</summary>
    internal bool AutoArmCut;

    /// <summary>When the cut may end, so a volley does not leave the ship blind for ever.</summary>
    internal float AutoArmRestoreAt;

    // --- defensive ECM -------------------------------------------------------------------------

    /// <summary>Automatic defensive jamming is armed for this ship.</summary>
    internal bool DefensiveEcm;

    /// <summary>The jammer is up and being held on a missile right now.</summary>
    internal bool EcmEngaged;

    /// <summary>The engagement has been written to the log, so it is not repeated every refresh.</summary>
    internal bool EcmEngagedLogged;

    /// <summary>Game-clock time at which the lock should be re-pointed.</summary>
    internal float EcmRefreshAt;

    /// <summary>Game-clock time after which the jammer may stand down.</summary>
    internal float EcmReleaseAt;

    /// <summary>Air-search radar state before the engagement, so it can be put back.</summary>
    internal bool EcmAirWasOn;

    /// <summary>
    /// The missile the defensive jammer is currently pointed at. Kept as an object because the
    /// weapon types cannot be named in code, and read only by the map overlay, which needs a
    /// position to draw the beam onto.
    /// </summary>
    internal object? EcmTarget;

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
