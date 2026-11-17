using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace SeaPowerAdvanced;

/// <summary>
/// Settings, stored in <c>BepInEx/config/io.github.bomberdeer22.seapoweradvanced.cfg</c>.
/// Every read falls back to a default, so a missing or broken config can never stop the mod.
/// </summary>
internal static class ModSettings
{
    private static ConfigEntry<bool>? enabled;
    private static ConfigEntry<bool>? includeGuns;
    private static ConfigEntry<bool>? allowEnemyUnits;
    private static ConfigEntry<bool>? showDiagnostics;
    private static ConfigEntry<bool>? showWhenNoMounts;
    private static ConfigEntry<bool>? verboseLog;

    private static ConfigEntry<bool>? featuresEnabled;
    private static ConfigEntry<float>? blinkSeconds;
    private static ConfigEntry<float>? salvoSpacingSeconds;
    private static ConfigEntry<float>? jamRangeNm;
    private static ConfigEntry<float>? jamAirRangeNm;
    private static ConfigEntry<bool>? showEcmCone;
    private static ConfigEntry<float>? ecmDiveNm;

    internal static bool Enabled => enabled?.Value ?? true;
    internal static bool IncludeGuns => includeGuns?.Value ?? false;
    internal static bool AllowEnemyUnits => allowEnemyUnits?.Value ?? false;
    internal static bool ShowDiagnostics => showDiagnostics?.Value ?? true;
    internal static bool ShowWhenNoMounts => showWhenNoMounts?.Value ?? true;
    internal static bool VerboseLog => verboseLog?.Value ?? true;

    /// <summary>Master switch for the menu features (hatch control is unaffected).</summary>
    internal static bool FeaturesEnabled => featuresEnabled?.Value ?? true;

    /// <summary>Seconds between radar blink flips. Short enough to see in a test mission.</summary>
    internal static float BlinkSeconds => blinkSeconds?.Value ?? 6f;

    /// <summary>Draw the ECM cones on the map.</summary>
    internal static bool ShowEcmCone => showEcmCone?.Value ?? true;

    /// <summary>
    /// How far the focused offensive jammer reaches against a ship, in nautical miles.
    ///
    /// 150 nm, which is deliberately generous: it is the range at which the mod will *offer* a
    /// contact as a target and keep holding it. Whether a given set actually degrades a given radar
    /// at that distance is still the game's own ECM model deciding - see the note in
    /// <see cref="JamAirRangeNm"/> about the radio horizon. Raising the figure moves the menu and the
    /// hold out; it does not overrule the simulation.
    /// </summary>
    internal static float JamRangeNm => jamRangeNm?.Value ?? 150f;

    /// <summary>
    /// How far the same jammer reaches against something in the air.
    ///
    /// The same 150 nm by default. In the real world this is the easier of the two - a masthead at
    /// 20 m sees 4.5 nm, an aircraft at 15,000 ft sees 65 nm on its own, so the pair see each other
    /// at roughly 70 nm - while ship-to-ship at 150 nm is well past the horizon. Both figures are the
    /// mod's reach, not a promise that the other set goes blind at that distance: the game's own
    /// radar calculator has the last word on whether the jam bites.
    /// </summary>
    internal static float JamAirRangeNm => jamAirRangeNm?.Value ?? 150f;

    /// <summary>
    /// How far out a defensive jammer starts pointing an inbound missile at the sea.
    ///
    /// **Ten nautical miles**, and the reason is the descent, not the defence. The missile is flown
    /// down gradually - a dozen metres a second, nose following - so it looks like a seeker losing
    /// its reference rather than a model being dragged into the water. At two miles a sea-skimmer
    /// closing at six hundred knots covers the distance in twelve seconds, which is not enough for a
    /// gradual descent to finish safely; from ten it has a minute.
    ///
    /// Zero switches the dive off and leaves the jam alone. Only defensive ECM uses it, and only on
    /// hulls that carry a jammer.
    /// </summary>
    internal static float EcmDiveNm => ecmDiveNm?.Value ?? 10f;

    /// <summary>Seconds between ships being released in a deconflicted salvo.</summary>
    internal static float SalvoSpacingSeconds => salvoSpacingSeconds?.Value ?? 8f;

    internal static void Bind()
    {
        try
        {
            var file = new ConfigFile(Path.Combine(Paths.ConfigPath, ModInfo.Id + ".cfg"), true);

            enabled = file.Bind("General", "Enabled", true,
                "Master switch for the mod.");

            includeGuns = file.Bind("General", "IncludeGuns", false,
                "Also list guns and CIWS in the hatch menu. Off by default: they have no silo hatch, "
                + "so only their mount cover (if any) can open.");

            allowEnemyUnits = file.Bind("General", "AllowEnemyUnits", false,
                "Show the hatch menu on enemy and neutral ships as well as your own.");

            showDiagnostics = file.Bind("Menu", "ShowDiagnosticsItem", true,
                "Add a 'Log hatch report' entry to the hatch menu.");

            showWhenNoMounts = file.Bind("Menu", "ShowWhenNoMounts", true,
                "Show the Weapon hatches entry even on units with no launchers, so it is obvious the "
                + "mod is running. Set to false for a cleaner menu once everything works.");

            verboseLog = file.Bind("Diagnostics", "VerboseLog", true,
                "Log every hatch action with the game method used and its result.");

            featuresEnabled = file.Bind("Features", "Enabled", true,
                "Add the advanced features to the game's right-click menus: sensors and EMCON, "
                + "countermeasures, readiness, manoeuvres, air operations, strike, deception, logistics.");

            blinkSeconds = file.Bind("Features", "BlinkSeconds", 6f,
                "Seconds between radar blink flips (2-120). Blink cycles the surface-search radar so "
                + "an anti-radiation seeker has less to home on.");

            showEcmCone = file.Bind("Features", "ShowEcmCone", true,
                "Draw a long, narrow cone from each jammer to the missile or ship it is holding. "
                + "Cyan for the offensive set, amber for the defensive one.");

            jamRangeNm = file.Bind("Features", "JamRangeNm", 150f,
                "How far the focused offensive jammer reaches against a ship, in nautical miles "
                + "(5-300). Beyond this the lock drops and the target's crew can bring its sensors "
                + "back up. This is the mod's reach: whether the jam bites at that distance is the "
                + "game's own ECM model deciding.");

            jamAirRangeNm = file.Bind("Features", "JamAirRangeNm", 150f,
                "How far the same jammer reaches against an aircraft or a helicopter, in nautical "
                + "miles (5-300).");

            // A config written before 0.18.1 still holds the old defaults, and BepInEx keeps
            // whatever is stored - so a player who never edited these would stay at 40/90 and think
            // nothing had changed. Only the exact old defaults are moved; anything the player chose
            // is left exactly as they set it.
            if (Math.Abs(jamRangeNm.Value - 40f) < 0.01f) jamRangeNm.Value = 150f;
            if (Math.Abs(jamAirRangeNm.Value - 90f) < 0.01f) jamAirRangeNm.Value = 150f;

            ecmDiveNm = file.Bind("Features", "EcmDiveNm", 10f,
                "How far out, in nautical miles, an armed defensive jammer starts pointing an "
                + "inbound missile at the sea: its lock is broken, its nose is pitched down and its "
                + "height is walked down gradually until it is on the water, where the game's own sea "
                + "impact removes it. 10 by default, because the descent has to be gradual; 0 "
                + "switches the dive off entirely and leaves the jam alone. Guns and rockets are "
                + "never affected - they have no lock to break.");

            // A config written before 0.19.0 holds the old two mile figure. That is the exact value
            // this build no longer wants (the descent cannot finish from two miles), so it is moved;
            // anything the player chose themselves is left alone.
            if (Math.Abs(ecmDiveNm.Value - 2f) < 0.01f) ecmDiveNm.Value = 10f;

            salvoSpacingSeconds = file.Bind("Features", "SalvoSpacingSeconds", 8f,
                "Seconds between ships being released to fire in a deconflicted salvo (1-60).");

        }
        catch (Exception error)
        {
            ModLog.Error("Could not load the config; defaults are used", error);
        }
    }
}
