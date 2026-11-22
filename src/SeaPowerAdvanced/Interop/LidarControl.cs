using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// The reading half of the LIDAR: what a hull can actually see with a laser, and what it may be
/// pointed at.
///
/// Two facts shape everything in this file, and both are the game's rather than the mod's:
///
/// <list type="number">
/// <item><b>There is no laser sensor in this game.</b> The families a hull's parameter block lists
///   are radar, sonar, ESM/RWR and ECM; nothing in the sensor list is a laser rangefinder or a
///   directed beam. So a LIDAR cannot be a new sensor in the simulation - it has to be built out of
///   evidence the game already produces.</item>
/// <item><b>A contact cannot be conjured.</b> The only detection lever the mod has is
///   <c>Utils.RevealContactToObject</c>, and it acts on a pair of <see cref="ObjectBase"/>s: it
///   records one unit on another's picture. It does not search, it cannot see through the fog, and
///   it cannot find something the game has not already placed in the world.</item>
/// </list>
///
/// So the honest shape is a <b>range-limited, bearing-limited, clear-weather look with a laser
/// set</b>, whose evidence comes from the hull's own sensor list: a hull carrying an optical,
/// infrared or laser director can see a contact at short range with its radars silent, and what the
/// mod does is put that contact on the hull's own picture - the same call a reconnaissance report
/// uses, position only.
///
/// The parts of this file that matter most are the refusals. A unit that is not in the player's
/// contact picture cannot be a passive sighting, because nothing has reported it: letting the mod
/// look for one would be letting it see through the fog of war, which is the whole difference
/// between a LIDAR and a cheat.
/// </summary>
internal static class LidarControl
{
    // --- is there a laser set on this hull? ------------------------------------------------------

    private static readonly Dictionary<Type, Fit> FitCache = new();

    /// <summary>What was found about one hull's optical/laser fit, and what said so.</summary>
    internal sealed class Fit
    {
        internal Fit(bool fitted, bool laser, string evidence, string quote)
        {
            Fitted = fitted;
            Laser = laser;
            Evidence = evidence;
            Quote = quote;
        }

        /// <summary>True when the hull's own sensor list holds something to look through.</summary>
        internal bool Fitted { get; }

        /// <summary>
        /// True when what it holds is a laser or optronic director - a sight with a rangefinder on
        /// it. False means the hull has <b>lookouts</b>: the game's own "Eyes Mk 1" optical sensor,
        /// which is a man with binoculars and not an instrument. The distinction is the range they
        /// are allowed, and nothing else.
        /// </summary>
        internal bool Laser { get; }

        /// <summary>The fit in three words, for a menu label and a report line.</summary>
        internal string Kind => !Fitted ? "nothing the mod recognises"
                            : Laser ? "a laser director"
                            : "lookouts (Eyes Mk 1)";

        /// <summary>
        /// Whether this hull may look. With <c>LidarAnyHull</c> on - the default - that is every
        /// hull: a hull's own data being silent about an optical set is not evidence that it has no
        /// lookouts, and the range that matters (the lookouts') applies either way. With it off,
        /// only a hull whose list positively holds something may look.
        ///
        /// One question, asked in one place, so the menu cannot offer an order the sweep refuses.
        /// </summary>
        internal bool Allowed => Fitted || ModSettings.LidarAnyHull;

        /// <summary>
        /// What it is allowed to reach, and on what basis - for a label and a report line that must
        /// not overclaim. A hull with no optical entry in its data is looking on the assumption that
        /// a warship has lookouts, and says so.
        /// </summary>
        internal string Basis => Laser ? "a laser director"
                               : Fitted ? "its lookouts (Eyes Mk 1 in its sensor list)"
                               : "lookouts assumed - its sensor list shows no optical set";

        /// <summary>The sensor list entries that decided it, by class name - for the player.</summary>
        internal string Evidence { get; }

        /// <summary>
        /// The same answer for a label the player must not find enormous: <c>SensorSystemInfrared</c>
        /// says what it is in one word, and a menu is not the place for the whole list.
        /// </summary>
        internal string Quote { get; }
    }

    /// <summary>
    /// The sensor list entry names that mean "something to look through": an instrument first, and
    /// the base game's own lookouts after them.
    ///
    /// <c>Eyes Mk 1</c> is the game's name for a hull's visual watch - a man on the bridge with
    /// binoculars, carried by every ship, and modelled as an optical sensor in the hull's own list.
    /// It is matched on the word "Eyes" rather than on the full string, because the class name may
    /// carry it as <c>SensorSystemEyesMk1</c>, <c>EyesMk1</c> or a display name with a space in it,
    /// and a lookup that only matched one spelling would read as "this hull has no eyes", which is
    /// the exact wrong answer.
    /// </summary>
    private static readonly string[] InstrumentNames =
    {
        "Optronic", "Optronics", "Optical", "ElectroOptical", "ElectroOptic",
        "Laser", "Lidar", "LIDAR", "Rangefinder", "Designator", "IRST", "Infrared",
        "Flir", "FLIR", "Television", "Sight", "Periscope",
    };

    /// <summary>The game's own visual watch: lookouts, not an instrument.</summary>
    private static readonly string[] LookoutNames = { "Eyes" };

    /// <summary>
    /// What this hull has to look through, read from its own sensor list - the same list
    /// <see cref="OffensiveJam"/> reads for its jammer, and read the same way: by class name, because
    /// the game names its sensor systems for what they are.
    ///
    /// The answer is one of three, and the third is why this still reads the list even though every
    /// hull may look (see <c>ModSettings.LidarAnyHull</c>): a laser director, the game's own lookouts,
    /// or <i>nothing the mod recognises</i>. The last one is not a refusal any more - it is what the
    /// report prints and what the menu says out loud, because a hull's own data saying nothing about
    /// an optical set is evidence, and silently treating it as "must have eyes" is how a mod starts
    /// inventing sensors. A look-out on a warship is close to certain; a look-out on a hull whose data
    /// the mod cannot read is not, and the report is where that distinction survives.
    /// </summary>
    internal static Fit Detect(ObjectBase? unit)
    {
        if (unit == null) return new Fit(false, false, "no unit", "no unit");

        Type type = unit.GetType();

        if (FitCache.TryGetValue(type, out Fit cached)) return cached;

        Fit fit = Read(unit);
        FitCache[type] = fit;
        return fit;
    }

    private static Fit Read(ObjectBase unit)
    {
        var instruments = new List<string>();
        var lookouts = new List<string>();
        bool readable = false;

        try
        {
            object? parameters = Capabilities.ParametersOf(unit);
            if (parameters == null) parameters = unit._obp;

            if (parameters == null)
                return new Fit(false, false, "the unit has no parameter block",
                               "its data is unreadable");

            FieldInfo? field = Sensors(parameters.GetType());
            if (field == null)
                return new Fit(false, false, "the parameter block has no sensor list",
                               "its data is unreadable");

            if (MemberProbe.Read(field, parameters) is not IEnumerable list)
                return new Fit(false, false, "the sensor list could not be read",
                               "its data is unreadable");

            readable = true;

            foreach (object? entry in list)
            {
                if (entry == null) continue;

                string name = entry.GetType().Name;

                // A radar is not an eye: a hull with only radar and sonar has nothing to look
                // through, and counting a name that merely contains "sight" inside "GunsightRadar"
                // would be exactly the guess this feature must not make.
                if (LooksLikeRadarOrSonar(name)) continue;

                if (Mentions(name, InstrumentNames)) instruments.Add(name);
                else if (Mentions(name, LookoutNames)) lookouts.Add(name);
            }
        }
        catch (Exception error)
        {
            return new Fit(false, false, "the fit could not be read (" + error.GetType().Name + ")",
                           "its data is unreadable");
        }

        if (!readable)
            return new Fit(false, false, "the sensor list could not be read", "its data is unreadable");

        if (instruments.Count == 0 && lookouts.Count == 0)
        {
            return new Fit(false, false,
                           "its sensor list holds nothing to look through (" + List(unit) + ")",
                           "no optical sensor in its list");
        }

        if (instruments.Count > 0)
        {
            return new Fit(true, true,
                          instruments.Count + " director(s) in the sensor list ("
                          + string.Join(", ", instruments.ToArray()) + ")",
                          instruments[0]);
        }

        return new Fit(true, false,
                       "lookouts, not an instrument: " + string.Join(", ", lookouts.ToArray())
                       + " in the sensor list",
                       lookouts[0]);
    }

    /// <summary>Radar and sonar families by name, so a "sight" inside a radar's class name is not
    /// mistaken for an eye.</summary>
    private static bool LooksLikeRadarOrSonar(string name) =>
        Mentions(name, new[] { "Radar", "Sonar", "ESM", "RWR", "ECM", "Jam", "Intercept", "Warning" });

    private static bool Mentions(string name, string[] words)
    {
        foreach (string word in words)
        {
            if (name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }

        return false;
    }

    /// <summary>The sensor list members on a parameter block, found by name the way
    /// <see cref="OffensiveJam"/> finds its own - never by guessing a type.</summary>
    private static readonly Dictionary<Type, FieldInfo?> SensorFields = new();

    private static FieldInfo? Sensors(Type parametersType)
    {
        if (SensorFields.TryGetValue(parametersType, out FieldInfo? cached)) return cached;

        FieldInfo? found = null;

        try
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            found = parametersType.GetField("_sensorSystems", all)
                    ?? parametersType.GetField("_sensors", all);

            if (found == null)
            {
                foreach (FieldInfo field in parametersType.GetFields(all))
                {
                    if (!typeof(IEnumerable).IsAssignableFrom(field.FieldType)) continue;
                    if (field.FieldType == typeof(string)) continue;
                    if (field.Name.IndexOf("sensor", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    // More than one candidate means the name is choosing, which it is not allowed to
                    // do here: report nothing rather than pick the wrong list.
                    if (found != null) { found = null; break; }
                    found = field;
                }
            }
        }
        catch (Exception) { found = null; }

        SensorFields[parametersType] = found;
        return found;
    }

    /// <summary>The sensor families on this hull, for the report and for the menu's evidence line.</summary>
    private static string List(ObjectBase unit)
    {
        try
        {
            object? parameters = Capabilities.ParametersOf(unit);
            if (parameters == null) parameters = unit._obp;
            if (parameters == null) return "no parameter block";

            FieldInfo? field = Sensors(parameters.GetType());
            if (field == null) return "no sensor field";

            if (MemberProbe.Read(field, parameters) is not IEnumerable list) return "no sensor list";

            var names = new List<string>();

            foreach (object? entry in list)
            {
                if (entry == null) continue;
                names.Add(entry.GetType().Name);
            }

            return names.Count == 0 ? "the list is empty" : string.Join(", ", names.ToArray());
        }
        catch (Exception) { return "the list could not be read"; }
    }

    /// <summary>Every hull type asked so far, so the report can say what was read. Names only.</summary>
    internal static void Remember(ObjectBase? unit, Fit fit, List<string> into)
    {
        if (unit == null) return;

        string line = unit.GetType().Name + ": "
                      + (fit.Laser ? "LASER DIRECTOR - "
                         : fit.Fitted ? "LOOKOUTS LISTED - "
                         : "LOOKOUTS ASSUMED, no optical set in its data - ") + fit.Evidence;

        if (!into.Contains(line)) into.Add(line);
    }
}
