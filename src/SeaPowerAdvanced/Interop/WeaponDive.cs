using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// Flying a jammed missile into the sea, through the game's own flight model.
///
/// <b>What the research settled.</b> Read from the decompiled game through two mods that work against
/// it - Real Air Combat Tactics and SeaPowerMultiplayer - the missile is a Unity component whose
/// transform <i>is</i> its position on the map grid, and:
///
/// <list type="bullet">
/// <item><c>Missile.OnFixedUpdate</c> carries <b>motion integration, guidance, seeker, fuse and the
///   state machine</b>. It runs every physics tick.</item>
/// <item><c>Missile.OnUpdateEveryFrame</c> carries the global time-compression clamp and <b>a
///   water-dip destruction path</b> - so this game <i>does</i> remove a weapon that gets into the
///   water. 0.19.0's dive never put one there: it dragged the <i>model</i> down and left the
///   simulation at its skimming height.</item>
/// <item>The flight state is plain data on the weapon: <c>ConnectionLost</c> (a
///   <c>ReactiveProperty&lt;bool&gt;</c>), <c>_lostGuidanceTime</c>, <c>_targetLost</c>,
///   <c>_velocityVecInUnity</c>, <c>_velocityInUnity</c>, <c>_velocityInKnots</c>,
///   <c>_remainingRangePercent</c>.</item>
/// <item><b>When the guidance link is lost the missile coasts on its last track</b> - the game's own
///   <c>_lostGuidanceTime</c> extrapolation: last known position plus last known velocity times time
///   since the loss. That is the state a jammed missile is supposed to be in, and it is a state the
///   game provides.</item>
/// </list>
///
/// <b>What went wrong in 0.20.0</b>, in the game's terms: the dive "broke the lock" by finding any
/// member whose name mentioned a target or a seeker and <i>setting it to null</i>. On this type that
/// is <c>_seeker</c> and <c>CurrentTarget</c> - objects <c>OnFixedUpdate</c> dereferences every tick.
/// The result was a <c>NullReferenceException</c> inside the game's own physics step, which is the
/// "Repeated Exceptions" warning the player saw; the missile's motion integration never ran again, so
/// it stopped in mid-air; and the mod kept rotating its transform, so it spun on the spot.
///
/// <b>So this build never writes null to anything.</b> It uses the game's own lock-loss state, and
/// then flies the weapon down by writing the two numbers the flight model actually integrates:
/// the <c>_velocityVecInUnity</c> vector and the transform's orientation. Descent is metred from the
/// height difference and the weapon's own speed, so a sea-skimmer at fifty feet noses over by a couple
/// of degrees and takes a couple of seconds, while one at ten thousand metres pitches much harder -
/// and the nose <b>stops at 90 degrees</b>, because past vertical it would be pointing back at the sky.
///
/// Nothing here destroys a weapon and nothing here calls a warhead. The removal is the game's: its own
/// water-dip path, once the weapon is genuinely in the water.
/// </summary>
internal static class WeaponDive
{
    /// <summary>What was found on one weapon type - both to steer it and to end its flight.</summary>
    private sealed class Shape
    {
        // --- the guidance state that says "this missile is no longer being guided" ---
        internal MemberInfo? ConnectionLost;
        internal MemberInfo? LostGuidanceTime;
        internal MemberInfo? TargetLost;
        internal MemberInfo? ConnectionLostForever;

        // --- the flight model's own numbers ---
        internal MemberInfo? VelocityVector;
        internal MemberInfo? VelocityUnits;
        internal MemberInfo? VelocityKnots;
        internal MemberInfo? RemainingRange;

        /// <summary>Its own height above the sea, when the build keeps one. This is the number the
        /// "50 ft" reading in the player's test came from, so it is written as well as steered.</summary>
        internal MemberInfo? Altitude;

        internal bool Transform;

        /// <summary>0 not steered yet, 1 velocity vector, 2 orientation only, 3 neither.</summary>
        internal int Route;

        /// <summary>True once the altitude write has been refused enough times to call it a floor.</summary>
        internal bool FloorFound;
        internal float Floor;

        /// <summary>The seeker as the game names it, and whether radio jamming can do anything to it.</summary>
        internal string Seeker = "";
        internal bool SeekerKnown;
        internal bool SeekerJammable;

        internal bool RefusalLogged;

        /// <summary>Last-resort ways to end a flight, used only if the water dip does not happen.</summary>
        internal List<Handler> Endings = new();

        internal List<string> Found = new();
        internal List<string> Members = new();
    }

    /// <summary>One way to ask a weapon to finish, and what came of it.</summary>
    private sealed class Handler
    {
        internal MemberInfo Member = null!;

        /// <summary><c>fuel</c>, <c>life</c> or <c>state</c>.</summary>
        internal string Kind = "";

        internal object? Value;
        internal object? Was;
        internal bool Tried;
        internal string Result = "";
    }

    /// <summary>One weapon being flown down.</summary>
    private sealed class Dive
    {
        internal float Started;
        internal float LastStep;

        /// <summary>Where it started, how fast it was going, and which way it was pointed.</summary>
        internal float StartHeight;
        internal float Speed;
        internal float Yaw;

        /// <summary>How long the descent is planned to take, and the pitch that achieves it.</summary>
        internal float Seconds;
        internal float NeededPitch;

        /// <summary>Nose-down applied so far, never more than <see cref="PitchLimit"/>.</summary>
        internal float Pitch;

        /// <summary>True once the weapon is at the waterline; the game's water-dip path takes it then.</summary>
        internal bool InWater;
        internal float WaterAt;

        /// <summary>When the crew is allowed to start the descent - the contact has to be seen and
        /// the solution has to settle.</summary>
        internal float ReadyAt;

        internal bool Announced;

        /// <summary>The last height written to the weapon's own altitude member, for the read-back.</summary>
        internal float Written = float.NaN;
        internal int Refused;
        internal float Floor = float.NaN;

        /// <summary>Last observed position, to notice a game that has stopped integrating motion.</summary>
        internal Vector3 LastPosition;
        internal float LastMoved;
        internal bool FrozenReported;

        /// <summary>Nothing left worth doing: stopped touching it, and said so once.</summary>
        internal bool Done;
        internal int Next;
        internal float TryAt;
    }

    private static readonly Dictionary<Type, Shape> Shapes = new();
    private static readonly Dictionary<object, Dive> Diving = new();

    // --- numbers ---------------------------------------------------------------------------------

    /// <summary>At or below this height the weapon is in the water as far as the mod can tell.</summary>
    private const float WaterlineMetres = 1f;

    /// <summary>Shortest and longest planned descent, in seconds.</summary>
    private const float ShortestDive = 2.5f;
    private const float LongestDive = 20f;

    /// <summary>Metres of height per second of the planned descent. 300 m -> 10 s, 6,000 m -> capped.</summary>
    private const float PlannedDescentMetresPerSecond = 30f;

    /// <summary>Nose-down rate, degrees per second, by height. Higher weapons get their nose down
    /// faster because getting the nose down is the whole job.</summary>
    private const float PitchSlow = 6f;
    private const float PitchMedium = 18f;
    private const float PitchFast = 30f;
    private const float MediumMetres = 1000f;
    private const float HighMetres = 5000f;

    /// <summary>The nose never goes past this. Ninety degrees is straight down; past it is the sky.</summary>
    private const float PitchLimit = 90f;

    /// <summary>How long the game's own water-dip path gets before anything else is tried.</summary>
    private const float WaterGraceSeconds = 6f;

    /// <summary>Seconds between last-resort attempts, and after which a stuck weapon is given up on.</summary>
    private const float RetireGap = 2f;
    private const float GiveUpSeconds = 20f;

    /// <summary>Unity units per knot-second, the game's own conversion.</summary>
    private const float UnityPerKnotSecond = 0.0076554087f;

    /// <summary>Metres per Unity unit on the map grid - the grid's own scale.</summary>
    private const float MetresPerUnityUnit = 67.2f;

    private static bool reported;
    private static float sweptAt = -100f;
    private static bool clockTried;
    private static MemberInfo? clockMember;

    internal static int Steered { get; private set; }
    internal static int WaterDipped { get; private set; }
    internal static int Confirmed { get; private set; }
    internal static int Left { get; private set; }

    /// <summary>Missiles this build refused to decoy because their seeker is not a radar.</summary>
    internal static int Immune { get; private set; }

    // --- the two steps ---------------------------------------------------------------------------

    /// <summary>
    /// Breaks the weapon's guidance <b>the way the game does</b>: the connection flag and the loss
    /// clock, which puts it into the coasting state its own code already understands.
    ///
    /// Nothing is set to null here. That is the whole point: 0.20.0 nulled <c>_seeker</c> and
    /// <c>CurrentTarget</c>, which the game dereferences on every physics tick, and the result was a
    /// frozen, spinning missile and a repeated exception inside <c>GameMain.FixedUpdate</c>.
    /// </summary>
    internal static bool BreakLock(object? weapon, out string note)
    {
        note = "";
        if (weapon == null) return false;

        Shape shape = ShapeOf(weapon);

        if (shape.ConnectionLost == null)
        {
            note = "no guidance-link member on this type - the lock is broken by flying it down instead";
            return false;
        }

        if (!Hold(shape, weapon))
        {
            note = "the connection flag would not take true";
            return false;
        }

        note = "guidance link lost - coasting on its last track";
        return true;
    }

    /// <summary>
    /// Keeps the guidance link down, and points the weapon at the water.
    ///
    /// Returns true while it is still being handled, and sets <paramref name="atSurface"/> once it is
    /// in the water (from then on the game's own water-dip path owns it).
    /// </summary>
    internal static bool Steer(object? weapon, out string note, out bool atSurface)
    {
        note = "";
        atSurface = false;

        if (weapon == null) return false;

        // Removed already: nothing to steer, and nothing to report as a failure.
        if (weapon is UnityEngine.Object gone && gone == null)
        {
            if (Diving.TryGetValue(weapon, out Dive? removed))
            {
                Diving.Remove(weapon);
                Confirmed++;
                note = "gone - the game removed it at the waterline" + Detail(removed);
            }
            atSurface = true;
            return false;
        }

        Shape shape = ShapeOf(weapon);
        float now = Time.unscaledTime;

        if (!Diving.TryGetValue(weapon, out Dive? dive))
        {
            dive = Begin(weapon, shape, now);
            Diving[weapon] = dive;
            Steered++;
        }

        float delta = Mathf.Clamp(now - dive.LastStep, 0f, 0.25f);
        dive.LastStep = now;
        float elapsed = now - dive.Started;

        // The link is re-asserted every frame, because the game's own update clears it: this is
        // exactly what a working notch mod has to do for the same reason.
        Hold(shape, weapon);

        if (dive.Done) return false;

        // The reaction delay: the jammer is on the missile from the moment it is seen, but the
        // descent - the part that kills it - waits for the contact to be designated and the
        // solution to settle. This is the second thing that stopped the feature being a wall.
        if (now < dive.ReadyAt)
        {
            note = "designating - the descent starts in "
                   + (dive.ReadyAt - now).ToString("0.0") + " s";
            return true;
        }

        // --- in the water: the game owns it from here ---------------------------------------------

        if (dive.InWater)
        {
            atSurface = true;

            if (now - dive.WaterAt < WaterGraceSeconds)
            {
                note = "in the water - waiting for the game's own water-dip removal";
                return true;
            }

            string ending = Retire(weapon, shape, dive, now);
            note = "in the water and still there after " + WaterGraceSeconds.ToString("0")
                   + " s" + (ending.Length > 0 ? "; " + ending : "");
            return !dive.Done;
        }

        // --- still in the air: point it at the water ----------------------------------------------

        // 1. Its own height, walked down over the planned time. This is a straight line, not a step
        //    per frame, so a long frame cannot put it in the water in one go.
        float planned = dive.StartHeight * (1f - Mathf.Clamp01(elapsed / dive.Seconds));
        float target = Mathf.Max(0f, planned);
        bool heightWritten = false;

        if (shape.Altitude != null)
        {
            try
            {
                object? raw = MemberProbe.Read(shape.Altitude, weapon);
                float read = raw == null ? float.NaN : Convert.ToSingle(raw);

                if (MemberProbe.Write(shape.Altitude, weapon, target))
                {
                    dive.Written = target;
                    heightWritten = true;
                }

                // Handed back higher than it was written, several times in a row: this build is
                // holding the weapon at a floor and no write will get under it. Say so, once.
                if (elapsed > ShortestDive)
                {
                    if (!float.IsNaN(read) && !float.IsNaN(dive.Written) && read > dive.Written + 1f)
                        dive.Refused++;
                    else
                        dive.Refused = 0;

                    if (dive.Refused >= 3 && float.IsNaN(dive.Floor))
                    {
                        dive.Floor = read;
                        shape.FloorFound = true;
                        shape.Floor = read;
                        ModLog.Warn("Defensive ECM: this build holds the missile at "
                                    + read.ToString("0") + " m - it will not go lower, so the "
                                    + "waterline has to be reached another way. That figure is in "
                                    + "ecm-dive.txt.");
                        WriteReport();
                    }
                }
            }
            catch (Exception) { }
        }

        GameObject? scene = Scene(weapon);

        if (scene == null)
        {
            if (shape.Route == 0) shape.Route = 3;

            if (!dive.FrozenReported)
            {
                dive.FrozenReported = true;
                ModLog.Warn("Defensive ECM: a weapon this build cannot steer (no scene object) was "
                            + "reported inbound; the lock is broken but its flight cannot be aimed");
            }
            note = "no scene object - the mod can break its lock but not aim it";
            return true;
        }

        Transform model = scene.transform;

        float pitch = Mathf.Min(PitchLimit,
                                Mathf.Min(dive.NeededPitch, elapsed * PitchRate(dive.StartHeight)));

        try
        {
            // Absolute, not incremental: an orientation that is *assigned* every frame cannot wind
            // itself up, however many frames the game skips. That is what turned 0.20.0's dive into
            // a missile spinning on the spot.
            Quaternion wanted = Quaternion.Euler(pitch, dive.Yaw, 0f);
            model.rotation = wanted;

            if (shape.VelocityVector != null && dive.Speed > 0.0001f)
            {
                Vector3 velocity = (wanted * Vector3.forward) * dive.Speed;

                if (MemberProbe.Write(shape.VelocityVector, weapon, velocity))
                {
                    if (shape.Route == 0) shape.Route = 1;
                }
            }
            else if (shape.Route == 0)
            {
                shape.Route = 2;
            }
        }
        catch (Exception error)
        {
            ModLog.Error("Defensive ECM could not aim a missile down", error);
        }

        dive.Pitch = pitch;

        // The transform is the position - the map grid's own truth, and the number the game's own
        // replication converts to latitude, longitude and height. The altitude member is reported
        // beside it, and written, but it is not trusted to decide whether the weapon is in the water.
        float water = model.position.y;
        float member = ReadHeight(weapon, shape);

        note = "height " + water.ToString("0") + " m"
               + (float.IsNaN(member) ? "" : " (own altitude member " + member.ToString("0") + " m)")
               + (heightWritten ? " written" : "")
               + ", nose " + pitch.ToString("0") + " deg down, " + dive.Speed.ToString("0.0")
               + " u/s";

        // Did anything actually move? A game that has stopped integrating this weapon is the failure
        // mode of 0.20.0, and it is worth naming rather than flying a corpse down for a minute.
        if (Vector3.Distance(model.position, dive.LastPosition) > 0.05f)
        {
            dive.LastPosition = model.position;
            dive.LastMoved = now;
        }
        else if (elapsed > 3f && now - dive.LastMoved > 3f && !dive.FrozenReported)
        {
            dive.FrozenReported = true;
            ModLog.Warn("Defensive ECM: the missile is not moving - the game has stopped "
                        + "integrating it, which means something the mod wrote was refused. "
                        + "Send ecm-dive.txt.");
        }

        if (water <= WaterlineMetres)
        {
            dive.InWater = true;
            dive.WaterAt = now;
            dive.TryAt = now;
            atSurface = true;

            note = "in the water at " + water.ToString("0") + " m - the game's own water-dip path "
                   + "takes it from here";
            WriteReport();
            return true;
        }

        return true;
    }

    /// <summary>
    /// True the first time this weapon is finished with - removed by the game, or given up on after
    /// every route was tried. Called every frame; it never touches the weapon.
    /// </summary>
    internal static bool Finished(object? weapon, out string note)
    {
        note = "";

        if (weapon == null) return false;
        if (!Diving.TryGetValue(weapon, out Dive? dive)) return false;

        if (weapon is UnityEngine.Object asset && asset == null)
        {
            Diving.Remove(weapon);
            Confirmed++;
            if (dive.InWater) WaterDipped++;

            note = (dive.InWater ? "taken by the game at the waterline" : "removed by the game")
                   + Detail(dive);
            return true;
        }

        if (dive.Done)
        {
            Diving.Remove(weapon);
            note = "left where this build put it" + Detail(dive);
            return true;
        }

        return false;
    }

    /// <summary>True when this weapon already has a dive - so a ship is holding it, not picking it up.</summary>
    internal static bool IsDiving(object? weapon) => weapon != null && Diving.ContainsKey(weapon);

    /// <summary>
    /// Whether radio jamming has anything to work on.
    ///
    /// A radar seeker does. An infrared, imaging, optical or anti-radiation seeker does not - radio
    /// frequency jamming cannot blind a heat seeker, and an anti-radiation missile is homing on the
    /// jammer. When the type cannot be read at all the answer is yes, and the report says so: the
    /// mod does not get to claim a missile was immune on the strength of a missing member.
    /// </summary>
    internal static bool CanBeDecoyed(object? weapon, out string why)
    {
        why = "";

        if (weapon == null) return true;
        if (!ModSettings.EcmDiveRadarSeekersOnly) return true;

        Shape shape = ShapeOf(weapon);

        if (!shape.SeekerKnown)
        {
            why = "seeker type could not be read - treated as jammable";
            return true;
        }

        if (shape.SeekerJammable) return true;

        why = shape.Seeker + " seeker - radio jamming cannot blind that";

        if (!shape.RefusalLogged)
        {
            shape.RefusalLogged = true;
            Immune++;
            ModLog.Info("Defensive ECM: left a missile alone because of its seeker - " + why
                        + ". EcmDiveRadarSeekersOnly is on; see ecm-dive.txt for the type.");
            WriteReport();
        }

        return false;
    }

    /// <summary>Marks a weapon as being in the water, for the caller that saw it there first.</summary>
    internal static void AtSurface(object? weapon)
    {
        if (weapon == null || !Diving.TryGetValue(weapon, out Dive? dive)) return;

        if (!dive.InWater)
        {
            dive.InWater = true;
            dive.WaterAt = Time.unscaledTime;
            dive.TryAt = Time.unscaledTime;
        }
    }

    /// <summary>True the first time this weapon is announced, so the log gets one line per missile.</summary>
    internal static bool Announce(object? weapon)
    {
        if (weapon == null || !Diving.TryGetValue(weapon, out Dive? dive)) return false;
        if (dive.Announced) return false;

        dive.Announced = true;
        return true;
    }

    /// <summary>One line for the menu.</summary>
    internal static string Describe()
    {
        if (Steered == 0) return "no missile has had to be put down";

        string line = Steered + " missile(s) pointed at the water";

        if (WaterDipped > 0) line += ", " + WaterDipped + " taken by the game's water dip";
        if (Confirmed > WaterDipped) line += ", " + (Confirmed - WaterDipped) + " removed by the game";
        if (Left > 0) line += ", " + Left + " left where this build put them";
        if (Immune > 0) line += ", " + Immune + " left alone because their seeker is not radar";

        return line;
    }

    // --- keeping the guidance down ----------------------------------------------------------------

    /// <summary>
    /// Writes the game's own "no guidance link" state: <c>ConnectionLost</c> true,
    /// <c>_lostGuidanceTime</c> stamped with the mission clock, and <c>_targetLost</c> true.
    ///
    /// All three are values, not references. Nothing here can leave the game holding a null.
    /// </summary>
    private static bool Hold(Shape shape, object weapon)
    {
        bool wrote = false;

        try
        {
            if (shape.ConnectionLost != null &&
                MemberProbe.WriteReactiveBool(shape.ConnectionLost, weapon, true))
                wrote = true;

            if (shape.TargetLost != null && MemberProbe.Write(shape.TargetLost, weapon, true))
                wrote = true;

            if (shape.LostGuidanceTime != null && TryMissionTime(weapon, out double clock))
            {
                // Stamped, not zeroed: the game dead-reckons from this, and a clock of zero would
                // mean "lost at the start of the mission", which extrapolates for ever.
                if (MemberProbe.Write(shape.LostGuidanceTime, weapon, clock)) wrote = true;
            }
        }
        catch (Exception) { }

        return wrote;
    }

    /// <summary>The game's own mission clock, by reflection, because naming its type has cost builds
    /// before. False when it cannot be found, in which case the loss clock is simply left alone.</summary>
    private static bool TryMissionTime(object weapon, out double seconds)
    {
        seconds = 0d;

        try
        {
            if (!clockTried)
            {
                clockTried = true;
                Type? type = weapon.GetType().Assembly.GetType("SeaPower.GameTime", false);
                if (type != null) clockMember = MemberProbe.Value(type, "missionElapsedTime");
            }

            if (clockMember == null) return false;

            object? raw = MemberProbe.Read(clockMember, null);
            if (raw is double d) { seconds = d; return true; }
            if (raw is float f) { seconds = f; return true; }
        }
        catch (Exception) { }

        return false;
    }

    // --- the last resort: ask the game to end the flight ------------------------------------------

    /// <summary>
    /// Tried only after the weapon has been in the water for <see cref="WaterGraceSeconds"/> and the
    /// game has not removed it: fuel to almost nothing, life expired, then the weapon's own state
    /// machine put into a finished state. One candidate every two seconds.
    ///
    /// Warhead methods are refused by name and there is no <c>Destroy</c> anywhere in this file.
    /// </summary>
    private static string Retire(object weapon, Shape shape, Dive dive, float now)
    {
        if (shape.Endings.Count == 0)
        {
            if (!dive.Done)
            {
                dive.Done = true;
                Left++;
                return "and this type has no end-of-flight member the mod is willing to touch";
            }
            return "";
        }

        if (dive.Next >= shape.Endings.Count)
        {
            if (now - dive.WaterAt > GiveUpSeconds)
            {
                dive.Done = true;
                Left++;
                return "every route was tried and the game kept it - see ecm-dive.txt";
            }
            return "";
        }

        if (now < dive.TryAt) return "";
        dive.TryAt = now + RetireGap;

        Handler handler = shape.Endings[dive.Next++];
        handler.Tried = true;
        handler.Was = MemberProbe.Read(handler.Member, weapon);

        bool ok = MemberProbe.Write(handler.Member, weapon, handler.Value);

        handler.Result = ok ? "written, no effect yet" : "the game would not take it";
        WriteReport();

        return handler.Kind + " " + handler.Member.Name + (ok ? " written" : " refused");
    }

    // --- setting a dive up -----------------------------------------------------------------------

    /// <summary>
    /// Reads the weapon's start conditions: its height, the heading it was on (so the dive does not
    /// spiral), its speed, and the pitch needed to reach the water in the planned time.
    /// </summary>
    private static Dive Begin(object weapon, Shape shape, float now)
    {
        var dive = new Dive
        {
            Started = now,
            LastStep = now,
            TryAt = now,
            LastMoved = now,
            ReadyAt = now + ModSettings.EcmDiveReactionSeconds,
        };

        GameObject? scene = Scene(weapon);

        if (scene != null)
        {
            try
            {
                Transform model = scene.transform;
                dive.StartHeight = Math.Max(0f, model.position.y);
                dive.Yaw = model.eulerAngles.y;
                dive.LastPosition = model.position;
            }
            catch (Exception) { }
        }

        float own = ReadHeight(weapon, shape);
        if (dive.StartHeight <= 1f && !float.IsNaN(own) && own > 0f) dive.StartHeight = own;

        dive.Speed = SpeedOf(weapon, shape);

        dive.Seconds = Mathf.Clamp(Math.Max(0f, dive.StartHeight) / PlannedDescentMetresPerSecond,
                                   ShortestDive, LongestDive);

        // The descent rate the plan asks for, in metres per second - and then in the grid's own
        // units, because the weapon's speed member is Unity units per second (about 67 m each).
        // Mixing the two is what would turn a gentle nudge into a vertical dive.
        float wantedMetres = dive.Seconds > 0f ? dive.StartHeight / dive.Seconds : 0f;
        float wantedUnits = wantedMetres / MetresPerUnityUnit;

        // The pitch that produces that descent at this weapon's own speed. A sea-skimmer at 600 kt
        // needs about one degree; something at ten thousand metres needs most of a right angle.
        // Capped at vertical, because past that it is climbing.
        dive.NeededPitch = dive.Speed > 0.0001f
            ? Mathf.Clamp(Mathf.Asin(Mathf.Clamp01(wantedUnits / dive.Speed)) * Mathf.Rad2Deg,
                          0f, PitchLimit)
            : PitchLimit;

        shape.Found.Add("found at       " + dive.StartHeight.ToString("0") + " m, heading "
                        + dive.Yaw.ToString("0") + " deg, " + dive.Speed.ToString("0.00")
                        + " unity units/s, planned " + dive.Seconds.ToString("0.0") + " s at "
                        + dive.NeededPitch.ToString("0") + " deg");

        return dive;
    }

    /// <summary>The weapon's speed: its own units-per-second member, else knots, else the vector.</summary>
    private static float SpeedOf(object weapon, Shape shape)
    {
        try
        {
            if (shape.VelocityUnits != null)
            {
                object? raw = MemberProbe.Read(shape.VelocityUnits, weapon);
                if (raw != null)
                {
                    float units = Convert.ToSingle(raw);
                    if (units > 0.0001f) return units;
                }
            }

            if (shape.VelocityKnots != null)
            {
                object? raw = MemberProbe.Read(shape.VelocityKnots, weapon);
                if (raw != null)
                {
                    float knots = Convert.ToSingle(raw);
                    if (knots > 0.0001f) return knots * UnityPerKnotSecond;
                }
            }

            if (shape.VelocityVector != null && MemberProbe.Read(shape.VelocityVector, weapon) is Vector3 v)
                return v.magnitude;
        }
        catch (Exception) { }

        return 0f;
    }

    private static float ReadHeight(object weapon, Shape shape)
    {
        try
        {
            if (shape.Altitude != null)
            {
                object? raw = MemberProbe.Read(shape.Altitude, weapon);
                if (raw != null) return Convert.ToSingle(raw);
            }
        }
        catch (Exception) { }

        return float.NaN;
    }

    private static float PitchRate(float startHeight) =>
        startHeight > HighMetres ? PitchFast : startHeight > MediumMetres ? PitchMedium : PitchSlow;

    private static string Detail(Dive dive) =>
        " (" + dive.StartHeight.ToString("0") + " m at the start, nose "
        + dive.Pitch.ToString("0") + " deg)";

    // --- helpers ---------------------------------------------------------------------------------

    /// <summary>
    /// The scene object behind a weapon: its GameObject when it is a component, itself when it is a
    /// GameObject, and nothing when it is neither. <c>UnityEngine.Object</c> is the base of both and
    /// has no <c>gameObject</c> member.
    /// </summary>
    private static GameObject? Scene(object? thing) => thing switch
    {
        Component component => component.gameObject,
        GameObject direct => direct,
        _ => null,
    };

    /// <summary>
    /// Forgets weapons the game has thrown away without offering them to us again, so the dive's
    /// bookkeeping does not hold every missile of the mission for ever.
    /// </summary>
    internal static void Sweep()
    {
        if (Diving.Count == 0) return;

        float now = Time.unscaledTime;
        if (now - sweptAt < 2f) return;
        sweptAt = now;

        var gone = new List<object>();

        foreach (KeyValuePair<object, Dive> entry in Diving)
        {
            bool destroyed = entry.Key is UnityEngine.Object asset && asset == null;
            bool stale = entry.Value.Done && now - entry.Value.Started > GiveUpSeconds + 10f;

            if (destroyed || stale) gone.Add(entry.Key);
        }

        foreach (object weapon in gone)
        {
            bool destroyed = weapon is UnityEngine.Object asset && asset == null;
            Diving.Remove(weapon);
            if (destroyed) Confirmed++;
        }
    }

    // --- what a weapon looks like ----------------------------------------------------------------

    private static Shape ShapeOf(object weapon)
    {
        Type type = weapon.GetType();
        if (Shapes.TryGetValue(type, out Shape? cached)) return cached;

        var shape = new Shape();
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (FieldInfo field in type.GetFields(all))
            Classify(shape, field, field.FieldType, field.Name);

        foreach (PropertyInfo property in type.GetProperties(all))
            Classify(shape, property, property.PropertyType, property.Name);

        shape.Transform = Scene(weapon) != null;
        ReadSeeker(weapon, shape);

        // The whole member list, because the next fix is written from this file rather than guessed.
        foreach (MemberInfo member in MemberProbe.All(type))
        {
            if (member is MethodInfo method && (method.IsSpecialName || method.GetParameters().Length > 0))
                continue;
            if (member is not FieldInfo && member is not PropertyInfo && member is not MethodInfo)
                continue;

            string kind = member switch
            {
                FieldInfo f => f.FieldType.Name,
                PropertyInfo p => p.PropertyType.Name,
                _ => "method()",
            };

            shape.Members.Add(kind + " " + member.Name);
        }
        shape.Members.Sort(StringComparer.OrdinalIgnoreCase);

        shape.Found.Add("type           " + type.Name);
        shape.Found.Add("guidance link  " + (shape.ConnectionLost?.Name ?? "<none found>"));
        shape.Found.Add("loss clock     " + (shape.LostGuidanceTime?.Name ?? "<none found>"));
        shape.Found.Add("target lost    " + (shape.TargetLost?.Name ?? "<none found>"));
        shape.Found.Add("velocity vector" + " " + (shape.VelocityVector?.Name ?? "<none found>"));
        shape.Found.Add("height member  " + (shape.Altitude?.Name ?? "<none found>"));
        shape.Found.Add("speed member   " + (shape.VelocityUnits?.Name
                            ?? shape.VelocityKnots?.Name ?? "<none found>"));
        shape.Found.Add("never touched  " + (shape.ConnectionLostForever?.Name + " " ?? "")
                            + (shape.RemainingRange?.Name ?? "")
                            + " - the first is the game's permanent-loss flag, the second is a"
                            + " readout; neither is needed to coast a missile into the water");
        shape.Found.Add("scene object   " + (shape.Transform ? "yes" : "no"));
        shape.Found.Add("seeker         " + (shape.SeekerKnown
                            ? shape.Seeker + (shape.SeekerJammable
                                ? " - radar: the jammer can work on it"
                                : " - not radar: this dive will leave it alone")
                            : "<could not be read - the dive treats it as jammable>"));

        if (shape.FloorFound)
            shape.Found.Add("floor found    " + shape.Floor.ToString("0")
                            + " m - this build refuses to fly a weapon lower than that");
        shape.Found.Add("end of flight  " + (shape.Endings.Count == 0
                            ? "<none by name - the water dip is the only route>"
                            : Endings(shape)));

        Shapes[type] = shape;

        if (!reported)
        {
            reported = true;
            WriteReport();
        }

        return shape;
    }

    /// <summary>Puts one member where it belongs, or ignores it.</summary>
    private static void Classify(Shape shape, MemberInfo member, Type type, string name)
    {
        string lower = name.ToLowerInvariant().TrimStart('_');

        switch (lower)
        {
            case "connectionlost":
                if (shape.ConnectionLost == null && IsFlag(type)) shape.ConnectionLost = member;
                return;

            case "connectionlostforever":
                if (shape.ConnectionLostForever == null && IsFlag(type)) shape.ConnectionLostForever = member;
                return;

            case "lostguidancetime":
                if (shape.LostGuidanceTime == null && type == typeof(double)) shape.LostGuidanceTime = member;
                return;

            case "targetlost":
                if (shape.TargetLost == null && type == typeof(bool)) shape.TargetLost = member;
                return;

            case "velocityvecinunity":
                if (shape.VelocityVector == null && type == typeof(Vector3)) shape.VelocityVector = member;
                return;

            case "velocityinunity":
                if (shape.VelocityUnits == null && type == typeof(float)) shape.VelocityUnits = member;
                return;

            case "velocityinknots":
                if (shape.VelocityKnots == null && type == typeof(float)) shape.VelocityKnots = member;
                return;

            case "remainingrangepercent":
                if (shape.RemainingRange == null && type == typeof(float)) shape.RemainingRange = member;
                return;
        }

        // Its own height above the sea. Not a target height, not a height above ground: those would
        // be a different number and writing them would make the missile aim at the wrong place.
        if (type == typeof(float) && shape.Altitude == null
            && (lower.Contains("height") || lower.Contains("altitude") || lower == "y")
            && !lower.Contains("target") && !lower.Contains("agl") && !lower.Contains("above")
            && !lower.Contains("ground") && !lower.Contains("min") && !lower.Contains("max")
            && !lower.Contains("cruise") && !lower.Contains("desired") && !lower.Contains("command"))
        {
            shape.Altitude = member;
            return;
        }

        // Fuel, life and state, for the last resort only.
        if (lower.Contains("fuel") || lower.Contains("endurance"))
        {
            if (type == typeof(float)) shape.Endings.Add(Number(member, "fuel", 0.01f));
            else if (type == typeof(double)) shape.Endings.Add(Number(member, "fuel", 0.01d));
            return;
        }

        if (lower.Contains("lifetime") || lower.Contains("ttl") || lower == "life"
            || lower.Contains("remaininglife"))
        {
            if (type == typeof(float)) shape.Endings.Add(Number(member, "life", 0f));
            else if (type == typeof(double)) shape.Endings.Add(Number(member, "life", 0d));
            return;
        }

        if (type.IsEnum && (lower.Contains("state") || lower.Contains("phase") || lower.Contains("stage")))
        {
            object? finished = FinishedValue(type);
            if (finished != null)
                shape.Endings.Add(new Handler { Member = member, Kind = "state", Value = finished });
        }
    }

    /// <summary>
    /// Reads the weapon's seeker type: <c>_seeker._guidanceType</c> on the weapon, or the same name
    /// on its ammunition parameters. Only the enum's <i>name</i> is used, so a renamed or reordered
    /// enum cannot make the mod think an infrared missile is a radar one - the words are checked.
    /// </summary>
    private static void ReadSeeker(object weapon, Shape shape)
    {
        try
        {
            object? seeker = MemberProbe.Read(Exact(weapon.GetType(), "seeker"), weapon);
            object? kind = seeker == null
                ? null
                : MemberProbe.Read(Exact(seeker.GetType(), "guidanceType"), seeker);

            if (kind == null)
            {
                object? ammo = MemberProbe.Read(Exact(weapon.GetType(), "ap"), weapon);
                if (ammo != null)
                    kind = MemberProbe.Read(Exact(ammo.GetType(), "guidanceType"), ammo);
            }

            if (kind == null) return;

            shape.Seeker = kind.ToString() ?? "";
            if (shape.Seeker.Length == 0) return;

            shape.SeekerKnown = true;
            shape.SeekerJammable = LooksJammable(shape.Seeker);
        }
        catch (Exception) { }
    }

    /// <summary>An exactly-named member, ignoring the underscore the game puts in front of fields.</summary>
    private static MemberInfo? Exact(Type type, string name)
    {
        foreach (MemberInfo member in MemberProbe.All(type))
        {
            if (member is not FieldInfo && member is not PropertyInfo) continue;
            if (string.Equals(member.Name.TrimStart('_'), name, StringComparison.OrdinalIgnoreCase))
                return member;
        }

        return null;
    }

    /// <summary>
    /// Whether a seeker named like this can be talked out of the sky.
    ///
    /// The words are the game's own enum names: <c>ActiveRadarHoming</c> and
    /// <c>SemiActiveRadarHoming</c> are jammable; <c>IRHoming</c>, imaging, optical, laser and
    /// television seekers are not; and an <c>AntiRadiation</c> seeker is homing on the emission, so
    /// jamming it is helping it. Anything unrecognised counts as jammable, and the report says so.
    /// </summary>
    private static bool LooksJammable(string value)
    {
        string name = value.ToLowerInvariant();

        if (name.Contains("infra") || name.Contains("imaging") || name.Contains("optical")
            || name.Contains("television") || name.Contains("laser") || name.Contains("heat")
            || name == "ir" || name.StartsWith("irh") || name.StartsWith("ir_"))
            return false;

        if (name.Contains("antirad") || name.Contains("radiation")) return false;

        return true;
    }

    /// <summary>True for a <c>ReactiveProperty&lt;bool&gt;</c>, which is how the game stores its
    /// live flags.</summary>
    private static bool IsFlag(Type type) =>
        type == typeof(bool) || type.Name.StartsWith("ReactiveProperty", StringComparison.Ordinal);

    private static Handler Number(MemberInfo member, string kind, object value) =>
        new() { Member = member, Kind = kind, Value = value };

    /// <summary>
    /// The enum value that means "no longer flying", chosen by name and refusing the ones that mean
    /// "hit something": a state called Detonated or Impact would be the warhead, not a miss.
    /// </summary>
    private static object? FinishedValue(Type type)
    {
        string[] wanted = { "expired", "dead", "lost", "finished", "ended", "done", "missed", "miss" };

        try
        {
            foreach (object value in Enum.GetValues(type))
            {
                string lower = (value.ToString() ?? "").ToLowerInvariant();

                if (lower.Contains("detonat") || lower.Contains("explod") || lower.Contains("warhead")
                    || lower.Contains("hit") || lower.Contains("impact") || lower.Contains("kill"))
                    continue;

                foreach (string word in wanted)
                    if (lower == word || lower.EndsWith(word)) return value;
            }
        }
        catch (Exception) { }

        return null;
    }

    private static string Endings(Shape shape)
    {
        var names = new List<string>();
        foreach (Handler handler in shape.Endings)
            names.Add(handler.Kind + " " + handler.Member.Name);

        return string.Join(", ", names);
    }

    // --- report ----------------------------------------------------------------------------------

    /// <summary>
    /// Writes <c>ecm-dive.txt</c>: what was found on each weapon type, its whole member list, what was
    /// written to steer it, whether the game moved it, and the session totals.
    /// </summary>
    internal static void WriteReport()
    {
        try
        {
            var text = new StringBuilder();
            text.AppendLine();
            text.AppendLine("Sea Power Advanced - missiles pointed at the water");
            text.AppendLine("------------------------------------------------");
            text.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            + "   mod " + ModInfo.Version);
            text.AppendLine();

            if (Shapes.Count == 0)
            {
                text.AppendLine("No guided weapon has been seen yet, so nothing has been read off one.");
            }
            else
            {
                foreach (KeyValuePair<Type, Shape> entry in Shapes)
                {
                    text.AppendLine("weapon type " + entry.Key.Name);
                    foreach (string line in entry.Value.Found) text.AppendLine("  " + line);
                    text.AppendLine("  route used     " + RouteName(entry.Value.Route));

                    if (entry.Value.FloorFound)
                        text.AppendLine("  FLOOR          " + entry.Value.Floor.ToString("0")
                                        + " m - the height the game would not let the mod write under");

                    foreach (Handler handler in entry.Value.Endings)
                    {
                        text.AppendLine("  last resort " + handler.Kind + " " + handler.Member.Name
                                        + (handler.Value == null ? "" : " = " + handler.Value)
                                        + (handler.Was == null ? "" : " (was " + handler.Was + ")")
                                        + " : " + (handler.Tried ? handler.Result : "not needed"));
                    }

                    text.AppendLine("  members, as this build declares them");
                    foreach (string member in entry.Value.Members) text.AppendLine("    " + member);
                    text.AppendLine();
                }
            }

            text.AppendLine("Totals this session: " + Describe());
            text.AppendLine();
            text.AppendLine("What happens, and why it is done this way");
            text.AppendLine("-----------------------------------------");
            text.AppendLine("  1. the guidance link is broken THE GAME'S WAY: ConnectionLost = true and");
            text.AppendLine("     _lostGuidanceTime stamped with the mission clock. The missile then");
            text.AppendLine("     coasts on its last track, which is the state the game's own code");
            text.AppendLine("     already understands for a jammed missile;");
            text.AppendLine("  2. its orientation is assigned every frame - pitch towards the water,");
            text.AppendLine("     heading held - and its own velocity vector is written to match, so");
            text.AppendLine("     the flight model integrates a real descent. The pitch needed is");
            text.AppendLine("     worked out from the height and the weapon's own speed: a sea-skimmer");
            text.AppendLine("     at 15 m noses over by about a degree, one at 5,000 m by tens of");
            text.AppendLine("     degrees;");
            text.AppendLine("  3. the nose stops at 90 degrees. Past vertical it would be climbing;");
            text.AppendLine("  4. at the waterline the mod stops writing. Missile.OnUpdateEveryFrame");
            text.AppendLine("     carries this game's own water-dip destruction path, and that is what");
            text.AppendLine("     removes the weapon.");
            text.AppendLine();
            text.AppendLine("NOTHING HERE WRITES NULL TO A REFERENCE. 0.20.0 cleared _seeker and");
            text.AppendLine("CurrentTarget to 'break the lock'; the game dereferences both in");
            text.AppendLine("Missile.OnFixedUpdate, which carries motion integration, guidance and the");
            text.AppendLine("fuse. The player saw the result: a repeated-exception warning, a missile");
            text.AppendLine("that stopped in mid-air, and the mod still rotating it, so it span on the");
            text.AppendLine("spot. No reference is written here, and nothing calls Detonate, Explode,");
            text.AppendLine("SelfDestruct or Destroy.");
            text.AppendLine();
            text.AppendLine("If the totals say a missile was 'left where this build put it', the water");
            text.AppendLine("dip did not take it and the last-resort routes did not either. That file,");
            text.AppendLine("with the member list above, is what the next fix is written from.");

            string path = ModFolders.AppendText("ecm-dive.txt", text.ToString());
            ModLog.Info("Missile dive report written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("Missile dive: could not write ecm-dive.txt", error);
        }
    }

    private static string RouteName(int route) => route switch
    {
        1 => "velocity vector + orientation (the flight model's own numbers)",
        2 => "orientation only - no velocity member on this type",
        3 => "neither: this type cannot be aimed",
        _ => "not used yet",
    };
}
