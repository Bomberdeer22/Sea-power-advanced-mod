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
    private static ConfigEntry<int>? ecmDiveChannels;
    private static ConfigEntry<float>? ecmDiveIntervalSeconds;
    private static ConfigEntry<float>? ecmDiveReactionSeconds;
    private static ConfigEntry<bool>? ecmDiveRadarSeekersOnly;
    private static ConfigEntry<int>? ecmJamChannels;
    private static ConfigEntry<float>? ecmJamHoldSeconds;
    private static ConfigEntry<float>? ecmJamRecoverySeconds;
    private static ConfigEntry<bool>? coopDatalink;
    private static ConfigEntry<bool>? lidarEnabled;
    private static ConfigEntry<float>? lidarRangeNm;
    private static ConfigEntry<float>? lidarLookoutNm;
    private static ConfigEntry<float>? lidarArcDegrees;
    private static ConfigEntry<float>? lidarSeconds;
    private static ConfigEntry<float>? coopDatalinkSeconds;

    internal static bool Enabled => enabled?.Value ?? true;
    internal static bool IncludeGuns => includeGuns?.Value ?? false;
    internal static bool AllowEnemyUnits => allowEnemyUnits?.Value ?? false;
    internal static bool ShowDiagnostics => showDiagnostics?.Value ?? true;
    internal static bool ShowWhenNoMounts => showWhenNoMounts?.Value ?? true;
    internal static bool VerboseLog => verboseLog?.Value ?? true;

    /// <summary>Master switch for the menu features (hatch control is unaffected).</summary>
    internal static bool FeaturesEnabled => featuresEnabled?.Value ?? true;

    /// <summary>
    /// Hand this force's contact picture to the other player's force in a co-op session.
    ///
    /// On by default, because the gap it fills is one a mission with a side per player creates
    /// without anybody asking for it, and the player who notices is the one who cannot see what
    /// their friend can. It does nothing at all outside a co-op session - the multiplayer mod's
    /// own co-op flag is required - and it never passes identity, only a position.
    /// </summary>
    internal static bool CoopDatalink => coopDatalink?.Value ?? true;

    /// <summary>Seconds between datalink sweeps (1-30). Three is inside the ten-second life of an
    /// air track, so a track the other force is given does not flicker while it is still true.</summary>
    internal static float CoopDatalinkSeconds => coopDatalinkSeconds?.Value ?? 3f;

    /// <summary>Whether the laser rangefinder order may be given at all.</summary>
    internal static bool LidarEnabled => lidarEnabled?.Value ?? true;

    /// <summary>How far a laser director can see, in nautical miles. Short by design: this is an
    /// eye with a rangefinder on it, not a search set.</summary>
    internal static float LidarRangeNm => lidarRangeNm?.Value ?? 12f;

    /// <summary>
    /// How far the game's own lookouts - "Eyes Mk 1" - can be pointed, in nautical miles. A man with
    /// binoculars on a bridge picks a hull out of the haze at a few miles, not at a director's range,
    /// and a feature that let every ship see to twelve miles without a radar would be a sensor the
    /// game does not have.
    /// </summary>
    internal static float LidarLookoutNm => lidarLookoutNm?.Value ?? 6f;

    /// <summary>Half-angle of the arc the beam may be pointed in, either side of the hull's heading
    /// (so 90 means "a quarter of the horizon").</summary>
    internal static float LidarArcDegrees => lidarArcDegrees?.Value ?? 90f;

    /// <summary>Seconds between looks. A laser is a glass on a bearing, not a scanning set.</summary>
    internal static float LidarSeconds => lidarSeconds?.Value ?? 4f;

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

    /// <summary>
    /// How many missiles one ship's jammer can hold down at the same time.
    ///
    /// **Three**, which is the player's chosen middle: enough that a two-missile shot is stopped,
    /// not so many that a salvo is pointless. Real ships defend on *guidance channels* - a handful
    /// of illuminators and fire-control loops that can be committed to a handful of threats - not on
    /// an unlimited attention span. A four-missile salvo still gets through; 99 is the old
    /// unbeatable version, kept so it remains reachable.
    /// </summary>
    internal static int EcmDiveChannels => Math.Max(1, Math.Min(99, ecmDiveChannels?.Value ?? 3));

    /// <summary>
    /// Seconds between one missile starting its descent and the next one being allowed to.
    ///
    /// The crew has to see the result of the first one before the next solution is settled. **Two
    /// seconds** is a watch working quickly; zero is the unbeatable version.
    /// </summary>
    internal static float EcmDiveIntervalSeconds =>
        Math.Max(0f, Math.Min(60f, ecmDiveIntervalSeconds?.Value ?? 2f));

    /// <summary>
    /// Seconds between a missile crossing the dive range and the dive starting.
    ///
    /// A jammer does not react in the same frame: the contact has to be seen, designated and the
    /// fire-control solution allowed to settle. **One second** is brisk; zero is the arcade version.
    /// </summary>
    internal static float EcmDiveReactionSeconds =>
        Math.Max(0f, Math.Min(30f, ecmDiveReactionSeconds?.Value ?? 1f));

    /// <summary>
    /// Only missiles with a radar seeker can be talked out of the sky.
    ///
    /// True, because that is how electronic attack works: radio-frequency jamming does not blind an
    /// infrared or imaging seeker, and against an anti-radiation missile a jammer is the thing it is
    /// homing on. This is the single biggest limit on the feature, and it is physics rather than a
    /// difficulty setting - which is why it defaults on and is worth leaving on.
    /// </summary>
    internal static bool EcmDiveRadarSeekersOnly => ecmDiveRadarSeekersOnly?.Value ?? true;

    /// <summary>
    /// How many contacts the task force's jammers can hold sensors down on at the same time.
    ///
    /// **Two**, and it is a budget for the whole force rather than a per-ship number, because a
    /// ship's jammer already holds one contact - the game gives a set one assignment - so a
    /// per-ship figure above one could only ever be a promise the simulation will not keep. What
    /// this counts is the expensive half: switching a contact's own sets off and keeping them off.
    /// Every lock still gets the game's own jam, whatever the budget says; a lock with no channel
    /// free is a jam without the blinding, and the menu says so. Electronic attack is a staffed,
    /// finite thing - a handful of tracks at once - and 99 is the old unlimited version.
    /// </summary>
    internal static int EcmJamChannels => Math.Max(1, Math.Min(99, ecmJamChannels?.Value ?? 2));

    /// <summary>
    /// How long one hold lasts before the target's crew get their sensors back, in seconds.
    ///
    /// **Sixty**. A crew is not paralysed by a jammer: it notices the set has gone, works out it is
    /// being jammed, and starts switching things back on. The mod models that by simply stopping,
    /// which is the honest way round - it does not switch anything back on for the enemy, it stops
    /// holding it down. Zero is the old, unlimited version.
    /// </summary>
    internal static float EcmJamHoldSeconds =>
        Math.Max(0f, Math.Min(300f, ecmJamHoldSeconds?.Value ?? 60f));

    /// <summary>
    /// Seconds after a hold ends during which the same contact cannot be held down again.
    ///
    /// **Fifteen**. Without this the window above is a formality: the player would click again the
    /// instant it lapsed and the target would never get a sensor back. With it, the target has a
    /// real quarter of a minute with its radar up - long enough for it to see, shoot or call
    /// something in - and the jammer has to wait it out. Zero makes the window free to renew.
    /// </summary>
    internal static float EcmJamRecoverySeconds =>
        Math.Max(0f, Math.Min(120f, ecmJamRecoverySeconds?.Value ?? 15f));

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

            ecmDiveChannels = file.Bind("Features", "EcmDiveChannels", 3,
                "How many missiles one armed ship's jammer can hold down at once (1-99). Real ships "
                + "defend on a handful of guidance channels, not on unlimited attention: at 3 a "
                + "four-missile salvo still gets through. 99 is the unbeatable version.");

            ecmDiveIntervalSeconds = file.Bind("Features", "EcmDiveIntervalSeconds", 2f,
                "Seconds between one missile starting its descent and the next being allowed to "
                + "(0-60). The crew has to see the first result before the next solution settles.");

            ecmDiveReactionSeconds = file.Bind("Features", "EcmDiveReactionSeconds", 1f,
                "Seconds between a missile crossing EcmDiveNm and its dive starting (0-30). The "
                + "contact has to be seen, designated and the fire-control solution allowed to "
                + "settle; 0 is the arcade version.");

            ecmDiveRadarSeekersOnly = file.Bind("Features", "EcmDiveRadarSeekersOnly", true,
                "Only missiles with a radar seeker can be decoyed. Infrared, imaging, optical and "
                + "anti-radiation seekers are immune - radio jamming cannot blind them, and an "
                + "anti-radiation missile homes on the jammer itself. Set false to let the dive "
                + "work on every homing missile, which is the unrealistic version.");

            // 0.22.0 shipped 2 / 4 / 2. The player chose 3 / 2 / 1 as the shipped defaults, and
            // BepInEx keeps whatever is stored, so the exact old figures are moved once - the same
            // rule as the jam-range and dive-range migrations above. A player who chose their own
            // numbers is left exactly as they set them.
            if (ecmDiveChannels.Value == 2) ecmDiveChannels.Value = 3;
            if (Math.Abs(ecmDiveIntervalSeconds.Value - 4f) < 0.01f) ecmDiveIntervalSeconds.Value = 2f;
            if (Math.Abs(ecmDiveReactionSeconds.Value - 2f) < 0.01f) ecmDiveReactionSeconds.Value = 1f;

            ecmJamChannels = file.Bind("Features", "EcmJamChannels", 2,
                "How many contacts the task force's jammers can hold sensors down on at once (1-99). "
                + "This is the expensive half of offensive ECM - switching a contact's own sets off "
                + "and keeping them off. Every lock still gets the game's own jam; a lock with no "
                + "channel free is a jam without the blinding, and the menu says so. 2 by default; "
                + "99 is the old unlimited version.");

            ecmJamHoldSeconds = file.Bind("Features", "EcmJamHoldSeconds", 60f,
                "How long one hold on a contact lasts before its crew get their sensors back, in "
                + "seconds (0-300). 60 by default: a crew notices the set has gone and starts "
                + "switching things back on. The mod does not switch anything back on for the "
                + "enemy - it simply stops holding it down. 0 is the old unlimited version.");

            ecmJamRecoverySeconds = file.Bind("Features", "EcmJamRecoverySeconds", 15f,
                "Seconds after a hold ends during which the same contact cannot be held down again "
                + "(0-120). 15 by default, so the target gets a real window with its radar up - long "
                + "enough to see, shoot or call something in. 0 makes the window free to renew.");

            salvoSpacingSeconds = file.Bind("Features", "SalvoSpacingSeconds", 8f,
                "Seconds between ships being released to fire in a deconflicted salvo (1-60).");

            coopDatalink = file.Bind("Features", "CoopDatalink", true,
                "Hand this force's contact picture to the other player's force in a co-op "
                + "session, through the game's own intel-sharing call. Only in a session the "
                + "multiplayer mod considers co-op, only to a force that is not flying a contact "
                + "in our own picture, and position only - never an identity. This is what makes "
                + "a mission with a side per player behave like one shared picture.");

            lidarEnabled = file.Bind("Features", "LidarEnabled", true,
                "Allow a ship with an optical or laser director in its own sensor list to look with "
                + "it: contacts the force already holds, inside range and inside the arc, are put on "
                + "the ship's own picture through the game's own reveal call. Position only. It "
                + "never looks for a contact the force has not already seen.");

            lidarRangeNm = file.Bind("Features", "LidarRangeNm", 12f,
                "How far the laser can see (1-40 nm). Short by design - an eye with a rangefinder "
                + "on it does not search the sea to the horizon.");

            lidarLookoutNm = file.Bind("Features", "LidarLookoutNm", 6f,
                "How far a hull with only lookouts (the game's own Eyes Mk 1 optical sensor) may "
                + "look, in nautical miles (1-40). Shorter than a laser director by design: a man "
                + "with binoculars is not an instrument. Never more than LidarRangeNm.");

            lidarArcDegrees = file.Bind("Features", "LidarArcDegrees", 90f,
                "Half-angle of the arc the beam may be pointed in, either side of the ship's heading "
                + "(10-360). A laser is a single bearing, not a hemisphere.");

            lidarSeconds = file.Bind("Features", "LidarSeconds", 4f,
                "Seconds between looks (1-60). A rangefinder is a glass on a bearing, not a "
                + "scanning set.");

            coopDatalinkSeconds = file.Bind("Features", "CoopDatalinkSeconds", 3f,
                "Seconds between datalink sweeps (1-30). An air track only lives about ten "
                + "seconds without a refresh, so a longer gap lets the far side's picture "
                + "flicker.");

        }
        catch (Exception error)
        {
            ModLog.Error("Could not load the config; defaults are used", error);
        }
    }
}
