using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SeaPower;
using UnityEngine;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// Flying a jammed missile into the sea - and, when the game will not take it, asking the game to
/// end its flight the way the game ends a flight of its own accord.
///
/// <b>What the player's test on 0.19.0 proved</b>, and what it cost:
///
/// <list type="bullet">
/// <item>the weapon was seen to arrive in the water "as if it teleported", because the descent rate
///   was a fixed 12 m/s: from fifty feet that is one and a quarter seconds, which is not a dive, it
///   is a jump;</item>
/// <item>the model was dragged down to sea level while the simulation still had the weapon at
///   <b>50 ft</b> - so the thing on screen was in the water and the thing on the ship's threat list
///   was at its skimming height, and the two never met;</item>
/// <item>this build of the game <b>does not remove a weapon that reaches the surface</b>. It held the
///   missile at about fifty feet and it stayed there. The previous build's assumption - "the game's
///   own sea impact will take it" - was simply wrong for weapons, whatever is true for ships.</item>
/// </list>
///
/// So the dive is now:
///
/// <list type="number">
/// <item>the lock is broken, so it cannot correct;</item>
/// <item>a descent <b>time</b> is chosen once, from the height it was found at - short at low level,
///   longer when the weapon is high, never more than forty seconds - and the height is then a
///   straight line over that time. A stutter cannot teleport it, and a weapon at ten thousand metres
///   does not take fourteen minutes;</item>
/// <item>the nose pitches over but <b>never past ninety degrees</b>, and it pitches faster when the
///   weapon is high, because a nose that takes fifteen seconds to come down is time spent not
///   coming down;</item>
/// <item>the model is only ever moved <b>to</b> the height the simulation reports, never below it -
///   which is what stopped the waterline phantom;</item>
/// <item>when it arrives - or when the game holds it at a skimming floor - the weapon is handed to
///   the game's own end-of-flight route: fuel exhausted, life expired, or its own state machine put
///   into a finished state. One candidate at a time, and each one is checked before the next is
///   tried.</item>
/// </list>
///
/// <b>Still nothing here destroys a weapon and nothing calls the warhead.</b> An earlier build
/// detonated them, once per tick, beside the ship they were aimed at; the build after that called
/// <c>Destroy</c>, which removed an object the game was still walking inside <c>GameMain.FixedUpdate</c>
/// and threw a <c>NullReferenceException</c>. Those routes stay closed. What is left is the state the
/// game itself acts on.
///
/// Every candidate found, every write, every read-back and the reason for every decision is written
/// to <c>ecm-dive.txt</c>.
/// </summary>
internal static class WeaponDive
{
    /// <summary>One way to ask this type of weapon to finish, and whether it worked.</summary>
    private sealed class Handler
    {
        internal MemberInfo Member = null!;

        /// <summary>What the member is: <c>fuel</c>, <c>life</c>, <c>state</c> or <c>method</c>.</summary>
        internal string Kind = "";

        /// <summary>The value written, and what was read before writing it.</summary>
        internal object? Value;
        internal object? Was;

        internal bool Tried;
        internal string Result = "";
    }

    /// <summary>What was found on one weapon type, and which route actually moves it.</summary>
    private sealed class Shape
    {
        internal List<MemberInfo> Homing = new();
        internal MemberInfo? Altitude;
        internal MemberInfo? Vertical;
        internal bool Transform;
        internal List<string> Found = new();

        /// <summary>0 none yet, 1 height member, 2 vertical member, 3 transform.</summary>
        internal int Route;

        /// <summary>The ways this type of weapon can be asked to end its flight, best first.</summary>
        internal List<Handler> Handlers = new();

        /// <summary>Everything the type has, for the report - the next fix is written from this.</summary>
        internal List<string> Members = new();
    }

    private sealed class Dive
    {
        internal float Started;
        internal float LastStep;

        /// <summary>What the simulation last said, and what the model last showed.</summary>
        internal float Height = float.NaN;
        internal float Model = float.NaN;

        /// <summary>The height it was found at, and how long the descent is planned to take.</summary>
        internal float StartHeight;
        internal float Seconds = 3f;

        /// <summary>Degrees of nose-down applied so far, from 0 to 90.</summary>
        internal float Pitch;

        internal bool Announced;
        internal bool AtSurface;
        internal float SurfaceAt;

        /// <summary>The last height written, for comparing against what the game reads back.</summary>
        internal float Written = float.NaN;

        /// <summary>Read-backs that did not follow the write: the game is holding the weapon up.</summary>
        internal int Refused;
        internal float Floor = float.NaN;

        /// <summary>Which handler is next, and when it may be tried.</summary>
        internal int Next;
        internal float TryAt;

        /// <summary>Nothing left to try: it stays where the game put it, and that is reported.</summary>
        internal bool Done;
    }

    private static readonly Dictionary<Type, Shape> Shapes = new();
    private static readonly Dictionary<object, Dive> Diving = new();

    // --- the dive, in numbers -------------------------------------------------------------------

    /// <summary>Where the waterline is, for the model. Below this the weapon is in the sea.</summary>
    private const float WaterlineMetres = 2f;

    /// <summary>Seconds a descent takes at low level - long enough to be seen, short enough to end.</summary>
    private const float ShortestDive = 2.5f;

    /// <summary>Seconds a descent may take at any height. Forty is inside the minute a missile
    /// closing at six hundred knots spends inside ten miles.</summary>
    private const float LongestDive = 40f;

    /// <summary>Metres of height bought per second of the planned descent. 1200 m -> 40 s, capped.</summary>
    private const float DiveMetresPerSecond = 12f;

    /// <summary>Nose-down rate below a thousand metres, in degrees per second.</summary>
    private const float PitchNormal = 6f;

    /// <summary>Nose-down rate above a thousand metres: the way down is the point, so the nose gets
    /// there in five seconds rather than fifteen.</summary>
    private const float PitchSteep = 18f;

    /// <summary>The nose never goes past this, however long the dive runs. Straight down is the
    /// limit; a weapon that keeps rotating past vertical is pointed back at the sky.</summary>
    private const float PitchLimit = 90f;

    /// <summary>Height at which the steeper pitch rate starts.</summary>
    private const float HighMetres = 1000f;

    /// <summary>How far above the written height a read-back may land before the write is judged to
    /// have been undone. Two metres is noise; the game's skimming floor is an order more.</summary>
    private const float RefusedMetres = 2f;

    /// <summary>Read-backs in a row needed before the floor is believed.</summary>
    private const int RefusedBeforeFloor = 3;

    /// <summary>Seconds between trying one way of ending the flight and the next.</summary>
    private const float RetireGap = 1.5f;

    /// <summary>Seconds after which a weapon nothing has moved is declared to have been left.</summary>
    private const float GiveUpSeconds = 30f;

    private static bool reported;

    /// <summary>When <see cref="Sweep"/> last ran, so it can be called every tick for free.</summary>
    private static float sweptAt = -100f;

    internal static int Steered { get; private set; }
    internal static int SentToTheGame { get; private set; }
    internal static int Confirmed { get; private set; }
    internal static int Left { get; private set; }

    // --- the two steps ---------------------------------------------------------------------------

    /// <summary>Breaks the weapon's lock. True when a homing member was actually written.</summary>
    internal static bool BreakLock(object? weapon, out string note)
    {
        note = "";
        if (weapon == null) return false;

        Shape shape = ShapeOf(weapon);
        bool wrote = false;

        foreach (MemberInfo member in shape.Homing)
        {
            try { if (MemberProbe.Write(member, weapon, null)) wrote = true; }
            catch (Exception) { }
        }

        note = wrote ? "lock broken"
                     : shape.Homing.Count == 0
                         ? "no homing member found"
                         : "the homing member would not take null";

        return wrote;
    }

    /// <summary>
    /// One step of the descent. Returns true while the weapon is still being dealt with, and sets
    /// <paramref name="atSurface"/> once it has arrived - at the waterline, or at whatever height
    /// this build of the game refuses to let it go below.
    /// </summary>
    internal static bool Steer(object? weapon, out string note, out bool atSurface)
    {
        note = "";
        atSurface = false;

        if (weapon == null) return false;

        // The game has already removed it: nothing to steer, and nothing to report as a failure.
        if (weapon is UnityEngine.Object gone && gone == null)
        {
            if (Diving.TryGetValue(weapon, out Dive? finished))
            {
                Diving.Remove(weapon);
                Confirmed++;
                note = "gone - the game removed it" + FloorText(finished);
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

        // 1. The height. A straight line over the planned time, not a step per call: the descent is
        //    the same shape whether the game ticks at 4 Hz or 40, and a long frame cannot jump it.
        float planned = dive.StartHeight * (1f - Mathf.Clamp01(elapsed / dive.Seconds));
        float target = Math.Max(0f, planned);

        float read = ReadHeight(weapon, shape);
        if (!float.IsNaN(read))
        {
            dive.Height = read;

            // Did the game take the last write? A height handed back higher than the one written,
            // several times running, is the game holding the weapon at its own skimming floor - and
            // no amount of writing will get below it. The first moments are excluded: the game has
            // its own picture of the dive and needs a tick or two to agree.
            bool late = elapsed > ShortestDive;

            if (late && !float.IsNaN(dive.Written) && read > dive.Written + RefusedMetres) dive.Refused++;
            else dive.Refused = 0;

            if (late && dive.Refused >= RefusedBeforeFloor && float.IsNaN(dive.Floor))
            {
                dive.Floor = read;
                note = "the game holds it at " + read.ToString("0") + " m - that is its floor, and "
                       + "the waterline is not reachable from here";
                WriteReport();
            }
        }

        if (WriteHeight(weapon, shape, target))
        {
            dive.Written = target;
            if (shape.Route == 0) shape.Route = 1;
            if (note.Length == 0)
                note = "height " + dive.StartHeight.ToString("0") + " m at start, "
                       + target.ToString("0") + " m at " + elapsed.ToString("0") + " s";
        }

        // 2. A vertical speed or pitch, for a build whose height is derived from one.
        float rate = dive.Seconds > 0f ? dive.StartHeight / dive.Seconds : 0f;
        if (shape.Vertical != null && rate > 0f)
        {
            try
            {
                if (MemberProbe.Write(shape.Vertical, weapon, -rate))
                {
                    if (shape.Route == 0) shape.Route = 2;
                    if (note.Length == 0) note = "nose down at " + rate.ToString("0") + " m/s";
                }
            }
            catch (Exception) { }
        }

        // 3. The model. It is only ever brought *down to* the height the simulation reports - never
        //    below it. Dragging the model to sea level while the weapon is held at fifty feet is what
        //    produced a missile that looked sunk and stayed on the threat list.
        GameObject? scene = Scene(weapon);
        float modelTo = float.NaN;

        if (scene != null)
        {
            try
            {
                Transform model = scene.transform;

                float wanted = float.IsNaN(dive.Height) ? target : dive.Height;
                float pitchWanted = Math.Min(PitchLimit, elapsed * PitchRate(dive.StartHeight));
                float turn = pitchWanted - dive.Pitch;

                if (turn != 0f) model.Rotate(turn, 0f, 0f, Space.Self);
                dive.Pitch = pitchWanted;

                Vector3 position = model.position;
                if (position.y > wanted + 0.5f)
                {
                    position.y = Mathf.MoveTowards(position.y, wanted, Math.Max(1f, rate) * delta);
                    model.position = position;
                }
                else if (position.y < wanted - 0.5f && !float.IsNaN(dive.Height))
                {
                    // The simulation says it is higher than the model: follow the simulation up,
                    // rather than leaving the model in the water.
                    position.y = wanted;
                    model.position = position;
                }

                dive.Model = model.position.y;
                modelTo = dive.Model;

                if (shape.Route == 0 && !float.IsNaN(dive.Model)) shape.Route = 3;

                if (note.Length == 0)
                    note = "model at " + modelTo.ToString("0") + " m, nose "
                           + dive.Pitch.ToString("0") + " deg down";
            }
            catch (Exception) { }
        }

        // --- has it arrived? ---------------------------------------------------------------------

        float best = !float.IsNaN(dive.Height) ? dive.Height : modelTo;
        bool arrived = false;

        if (!float.IsNaN(best) && best <= WaterlineMetres) arrived = true;
        if (!float.IsNaN(dive.Floor)) arrived = true;
        if (elapsed >= dive.Seconds) arrived = true;

        if (!arrived) return true;

        // --- hand it to the game ----------------------------------------------------------------

        atSurface = true;

        if (!dive.AtSurface)
        {
            dive.AtSurface = true;
            dive.SurfaceAt = now;
            dive.TryAt = now;
        }

        string ending = Retire(weapon, shape, dive, now);

        if (ending.Length > 0) note = note.Length > 0 ? note + "; " + ending : ending;

        // Everything tried, nothing worked: stop touching it and say so once.
        if (dive.Done) return false;

        return true;
    }

    /// <summary>
    /// The game's own ways of ending a flight, tried one at a time: fuel gone, life expired, or the
    /// weapon's own state machine put into a state that means finished.
    ///
    /// One candidate per <see cref="RetireGap"/> seconds, because a state that needs a tick or two to
    /// act on is not a failure, and because several writes in one frame is how the warhead got set
    /// off once per tick in an earlier build. Nothing here is a warhead method and nothing here is
    /// <c>Destroy</c>.
    /// </summary>
    private static string Retire(object weapon, Shape shape, Dive dive, float now)
    {
        if (shape.Handlers.Count == 0)
        {
            if (!dive.Done)
            {
                dive.Done = true;
                Left++;
                return "no way to end its flight was found on this weapon type - it is at "
                       + (float.IsNaN(dive.Floor) ? dive.Height : dive.Floor).ToString("0")
                       + " m and this build does not remove it there";
            }
            return "";
        }

        if (now - dive.SurfaceAt > GiveUpSeconds && dive.Next >= shape.Handlers.Count)
        {
            dive.Done = true;
            Left++;
            return "every route was tried and the game kept it - see ecm-dive.txt";
        }

        if (now < dive.TryAt) return "";
        if (dive.Next >= shape.Handlers.Count)
        {
            // Wait out the give-up timer; the last attempt may still act.
            dive.TryAt = now + RetireGap;
            return "";
        }

        Handler handler = shape.Handlers[dive.Next];

        // Counted per weapon, not per attempt: a missile that needed three routes is still one
        // missile handed over.
        if (dive.Next == 0) SentToTheGame++;

        dive.Next++;
        dive.TryAt = now + RetireGap;

        handler.Tried = true;

        bool ok = Apply(handler, weapon);

        handler.Result = ok ? "written, waiting" : "the game would not take it";

        // The report is the record of what was tried, not a snapshot of the first guess.
        WriteReport();

        if (!ok)
            return "could not " + handler.Kind + " " + handler.Member.Name;

        return handler.Kind + " " + handler.Member.Name + " -> "
               + (handler.Value == null ? "?" : handler.Value.ToString());
    }

    private static bool Apply(Handler handler, object weapon)
    {
        try
        {
            // What it said before, so the report shows a change rather than an intention.
            handler.Was = MemberProbe.Read(handler.Member, weapon);

            if (handler.Kind == "method")
                return MemberProbe.Invoke(handler.Member, weapon);

            return MemberProbe.Write(handler.Member, weapon, handler.Value);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// True the first time this weapon is seen to have finished - the game removed it, or it is
    /// sitting wherever this build leaves it and there is nothing left to try. Never destroys.
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
            note = "taken by the game" + FloorText(dive);
            return true;
        }

        if (dive.AtSurface && dive.Done)
        {
            Diving.Remove(weapon);
            note = "left where this build puts it" + FloorText(dive);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Forgets weapons the game has thrown away without telling anybody.
    ///
    /// A weapon that the game drops from its own inbound list is never offered to <see cref="Steer"/>
    /// or <see cref="Finished"/> again, so without this the dictionary would hold a reference to every
    /// missile of the mission for ever. Cheap, and called once every couple of seconds.
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

            // A weapon still on a ship's list keeps being steered; one nothing has touched for a
            // long time after the dive was abandoned is dropped.
            bool stale = entry.Value.Done && now - entry.Value.SurfaceAt > GiveUpSeconds + 5f;

            if (destroyed || stale) gone.Add(entry.Key);
        }

        foreach (object weapon in gone)
        {
            bool destroyed = weapon is UnityEngine.Object asset && asset == null;
            Diving.Remove(weapon);
            if (destroyed) Confirmed++;
        }
    }

    /// <summary>Marks the moment a weapon was first seen at the surface, for <see cref="Finished"/>.</summary>
    internal static void AtSurface(object? weapon)
    {
        if (weapon == null || !Diving.TryGetValue(weapon, out Dive? dive)) return;

        if (!dive.AtSurface)
        {
            dive.AtSurface = true;
            dive.SurfaceAt = Time.unscaledTime;
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

    /// <summary>One line for the menu: what the close-in steering has done.</summary>
    internal static string Describe()
    {
        if (Steered == 0) return "no missile has had to be put down";

        string line = Steered + " missile(s) pointed at the sea";

        if (SentToTheGame > 0) line += ", " + SentToTheGame + " asked to end their flight by the game's own route";
        if (Confirmed > 0) line += ", " + Confirmed + " confirmed removed by the game";
        if (Left > 0) line += ", " + Left + " left where this build puts them";

        return line;
    }

    // --- helpers ---------------------------------------------------------------------------------

    /// <summary>
    /// Sets up one dive: what height it started at, and how long the descent will take. Chosen once,
    /// so that a slow frame cannot shorten it and a fast one cannot stretch it.
    /// </summary>
    private static Dive Begin(object weapon, Shape shape, float now)
    {
        float height = ReadHeight(weapon, shape);

        if (float.IsNaN(height))
        {
            GameObject? scene = Scene(weapon);
            if (scene != null)
            {
                try { height = scene.transform.position.y; } catch (Exception) { height = float.NaN; }
            }
        }

        if (float.IsNaN(height) || height < 0f) height = 0f;

        var dive = new Dive
        {
            Started = now,
            LastStep = now,
            StartHeight = height,
            Height = height,
            Seconds = Mathf.Clamp(height / DiveMetresPerSecond, ShortestDive, LongestDive),
            TryAt = now,
        };

        return dive;
    }

    /// <summary>Nose-down rate for a weapon found at this height.</summary>
    private static float PitchRate(float startHeight) =>
        startHeight > HighMetres ? PitchSteep : PitchNormal;

    private static string FloorText(Dive dive) =>
        float.IsNaN(dive.Floor) ? "" : " (this build held it at " + dive.Floor.ToString("0") + " m)";

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

    private static bool WriteHeight(object weapon, Shape shape, float metres)
    {
        if (shape.Altitude == null) return false;

        try { return MemberProbe.Write(shape.Altitude, weapon, metres); }
        catch (Exception) { return false; }
    }

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

    // --- what a weapon looks like ----------------------------------------------------------------

    private static Shape ShapeOf(object weapon)
    {
        Type type = weapon.GetType();
        if (Shapes.TryGetValue(type, out Shape? cached)) return cached;

        var shape = new Shape();
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (FieldInfo field in type.GetFields(all))
        {
            if (IsHoming(field.FieldType, field.Name)) { shape.Homing.Add(field); continue; }
            if (IsHeight(field.FieldType, field.Name)) shape.Altitude ??= field;
            if (IsVertical(field.FieldType, field.Name)) shape.Vertical ??= field;

            AddHandler(shape, field, field.FieldType, field.Name);
        }

        foreach (PropertyInfo property in type.GetProperties(all))
        {
            if (!property.CanRead) continue;

            if (property.CanWrite && IsHoming(property.PropertyType, property.Name))
            {
                shape.Homing.Add(property);
                continue;
            }

            if (property.CanWrite && IsHeight(property.PropertyType, property.Name))
                shape.Altitude ??= property;

            if (property.CanWrite && IsVertical(property.PropertyType, property.Name))
                shape.Vertical ??= property;

            if (property.CanWrite) AddHandler(shape, property, property.PropertyType, property.Name);
        }

        foreach (MethodInfo method in type.GetMethods(all))
        {
            if (method.GetParameters().Length != 0) continue;
            if (!IsQuietEnding(method.Name)) continue;

            shape.Handlers.Add(new Handler { Member = method, Kind = "method" });
        }

        shape.Transform = Scene(weapon) != null;

        // Member inventory, for the report. Sorted so two runs of the same type compare line by line.
        foreach (MemberInfo member in MemberProbe.All(type))
        {
            if (member is MethodInfo m && (m.IsSpecialName || m.GetParameters().Length > 0)) continue;
            if (member is not FieldInfo && member is not PropertyInfo && member is not MethodInfo) continue;

            string kind = member switch
            {
                FieldInfo f => f.FieldType.Name,
                PropertyInfo p => p.PropertyType.Name,
                _ => "method()",
            };

            shape.Members.Add(kind + " " + member.Name);
        }
        shape.Members.Sort(StringComparer.OrdinalIgnoreCase);

        Balance(shape);

        shape.Found.Add("type           " + type.Name);
        shape.Found.Add("homing member  " + Names(shape.Homing));
        shape.Found.Add("height member  " + (shape.Altitude?.Name ?? "<none found>"));
        shape.Found.Add("vertical       " + (shape.Vertical?.Name ?? "<none found>"));
        shape.Found.Add("transform      " + (shape.Transform ? "yes" : "no"));
        shape.Found.Add("end of flight  " + Handlers(shape));

        Shapes[type] = shape;

        if (!reported)
        {
            reported = true;
            WriteReport();
        }

        return shape;
    }

    /// <summary>
    /// Puts the end-of-flight candidates in the order they are safest to try: a fuel tank that is
    /// empty, then a life that has run out, then the weapon's own state, then a quiet ending method
    /// last. Warhead methods are never candidates at all.
    /// </summary>
    private static void Balance(Shape shape)
    {
        var ordered = new List<Handler>();

        foreach (Handler handler in shape.Handlers)
        {
            if (handler.Kind == "fuel") ordered.Add(handler);
        }
        foreach (Handler handler in shape.Handlers)
        {
            if (handler.Kind == "life") ordered.Add(handler);
        }
        foreach (Handler handler in shape.Handlers)
        {
            if (handler.Kind == "state") ordered.Add(handler);
        }
        foreach (Handler handler in shape.Handlers)
        {
            if (handler.Kind == "method") ordered.Add(handler);
        }

        shape.Handlers = ordered;
    }

    /// <summary>
    /// Classifies one member as a way of ending a flight, or leaves it alone.
    ///
    /// The values written are the smallest thing that means "finished": fuel is taken to almost
    /// nothing rather than exactly nothing, because a simulation that divides by it would otherwise
    /// be handed a division by zero, and that would be a crash of the mod's making.
    /// </summary>
    private static void AddHandler(Shape shape, MemberInfo member, Type type, string name)
    {
        string lower = name.ToLowerInvariant().TrimStart('_');

        // Fuel. Never exactly zero.
        if (lower.Contains("fuel") || lower.Contains("endurance"))
        {
            if (type == typeof(float)) shape.Handlers.Add(Numeric(member, "fuel", 0.01f));
            else if (type == typeof(double)) shape.Handlers.Add(Numeric(member, "fuel", 0.01d));
            else if (type == typeof(int)) shape.Handlers.Add(Numeric(member, "fuel", 1));
            return;
        }

        // A life, a time to live, a remaining time: zero of it is expired.
        if (lower.Contains("lifetime") || lower.Contains("life") || lower.Contains("ttl")
            || lower.Contains("timeout") || lower.Contains("remaining") || lower.Contains("duration")
            || lower.Contains("maxtime") || lower.Contains("flighttime"))
        {
            if (type == typeof(float)) shape.Handlers.Add(Numeric(member, "life", 0f));
            else if (type == typeof(double)) shape.Handlers.Add(Numeric(member, "life", 0d));
            else if (type == typeof(int)) shape.Handlers.Add(Numeric(member, "life", 0));
            return;
        }

        // An age or an elapsed time: past any limit it could be compared against.
        if (lower.Contains("age") || lower.Contains("elapsed"))
        {
            if (type == typeof(float)) shape.Handlers.Add(Numeric(member, "life", 1e6f));
            else if (type == typeof(double)) shape.Handlers.Add(Numeric(member, "life", 1e6d));
            return;
        }

        // A state machine: put it into a state whose own name says finished.
        if (type.IsEnum && (lower.Contains("state") || lower.Contains("phase")
                            || lower.Contains("mode") || lower.Contains("status")))
        {
            object? finished = FinishedValue(type);
            if (finished == null) return;

            shape.Handlers.Add(new Handler
            {
                Member = member,
                Kind = "state",
                Value = finished,
            });
        }
    }

    private static Handler Numeric(MemberInfo member, string kind, object value) =>
        new() { Member = member, Kind = kind, Value = value };

    /// <summary>
    /// The enum value that means "no longer flying", chosen by name. Deliberately narrow: a wrong
    /// guess here is written into the weapon's own state, so a value called <c>Hit</c> or
    /// <c>Detonate</c> is refused rather than tried.
    /// </summary>
    private static object? FinishedValue(Type type)
    {
        string[] wanted = { "expired", "dead", "lost", "finished", "ended", "done", "missed", "miss", "inactive", "none" };

        try
        {
            object? best = null;

            foreach (object value in Enum.GetValues(type))
            {
                string name = value.ToString() ?? "";
                string lower = name.ToLowerInvariant();

                if (lower.Contains("detonat") || lower.Contains("explod") || lower.Contains("warhead")
                    || lower.Contains("hit") || lower.Contains("impact") || lower.Contains("kill"))
                    continue;

                foreach (string word in wanted)
                {
                    if (lower == word || lower.EndsWith(word))
                    {
                        best = value;
                        break;
                    }
                }

                if (best != null) break;
            }

            return best;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Methods that end a flight quietly. Anything that could set a warhead off is excluded,
    /// by name, before anything is called.</summary>
    private static bool IsQuietEnding(string name)
    {
        string lower = name.ToLowerInvariant();

        if (lower.Contains("detonat") || lower.Contains("explod") || lower.Contains("warhead")
            || lower.Contains("selfdestruct") || lower.Contains("damage") || lower.Contains("hit")
            || lower.Contains("impact"))
            return false;

        return lower == "expire" || lower == "abort" || lower == "terminate" || lower == "end"
               || lower == "despawn" || lower == "lose";
    }

    /// <summary>
    /// The thing the weapon is homing on. Shape first - it has to hold null - then the name, so a
    /// member called "targets" (a list) is left alone: blanking it would take the whole weapon's
    /// bookkeeping with it.
    /// </summary>
    private static bool IsHoming(Type type, string name)
    {
        if (type.IsValueType || type == typeof(string)) return false;
        if (typeof(IEnumerable).IsAssignableFrom(type)) return false;

        string lower = name.ToLowerInvariant();

        bool named = lower.Contains("target") || lower.Contains("seek") || lower.Contains("victim")
                     || lower.Contains("homing") || lower.Contains("aimpoint");

        if (!named) return false;

        return !lower.Contains("count") && !lower.Contains("time") && !lower.Contains("range");
    }

    /// <summary>An altitude the mod can steer: a number whose name says height above the sea.</summary>
    private static bool IsHeight(Type type, string name)
    {
        if (type != typeof(float) && type != typeof(double) && type != typeof(int)) return false;

        string lower = name.ToLowerInvariant();

        return (lower.Contains("height") || lower.Contains("altitude") || lower == "_y")
               && !lower.Contains("target") && !lower.Contains("agl") && !lower.Contains("above");
    }

    /// <summary>A vertical speed or pitch member, for the builds whose height is derived from one.</summary>
    private static bool IsVertical(Type type, string name)
    {
        if (type != typeof(float) && type != typeof(double)) return false;

        string lower = name.ToLowerInvariant();

        return lower.Contains("verticalspeed") || lower.Contains("verticalvelocity")
               || lower.Contains("climbrate") || lower.Contains("climb")
               || lower.Contains("pitch");
    }

    private static string Names(List<MemberInfo> members)
    {
        if (members.Count == 0) return "<none found>";

        var names = new List<string>();
        foreach (MemberInfo member in members) names.Add(member.Name);

        return string.Join(", ", names);
    }

    private static string Handlers(Shape shape)
    {
        if (shape.Handlers.Count == 0) return "<none found - this build removes nothing by itself>";

        var names = new List<string>();
        foreach (Handler handler in shape.Handlers)
            names.Add(handler.Kind + " " + handler.Member.Name
                      + (handler.Value == null ? "" : "=" + handler.Value));

        return string.Join(", ", names);
    }

    // --- report ----------------------------------------------------------------------------------

    /// <summary>
    /// Writes <c>ecm-dive.txt</c>: what was read off each weapon type, its whole member list, which
    /// end-of-flight routes were found and what happened when each was tried, and which route moved
    /// it. Appended, so a mission's whole record is in one file.
    /// </summary>
    internal static void WriteReport()
    {
        try
        {
            var text = new StringBuilder();
            text.AppendLine();
            text.AppendLine("Sea Power Advanced - missiles pointed at the sea");
            text.AppendLine("----------------------------------------------");
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
                    text.AppendLine("  route used    " + RouteName(entry.Value.Route));

                    if (entry.Value.Handlers.Count == 0)
                    {
                        text.AppendLine("  end of flight  <nothing on this type by name - the report"
                                        + " below is the list to work from>");
                    }

                    foreach (Handler handler in entry.Value.Handlers)
                    {
                        text.AppendLine("  tried " + handler.Kind + " " + handler.Member.Name
                                        + (handler.Value == null ? "" : " = " + handler.Value)
                                        + (handler.Was == null ? "" : " (was " + handler.Was + ")")
                                        + " : " + (handler.Tried
                                            ? (handler.Result.Length > 0 ? handler.Result : "not reached")
                                            : "not tried yet"));
                    }

                    text.AppendLine("  members, as this build declares them");
                    foreach (string member in entry.Value.Members) text.AppendLine("    " + member);
                    text.AppendLine();
                }
            }

            text.AppendLine("Totals this session: " + Describe());
            text.AppendLine();
            text.AppendLine("What happens, in order");
            text.AppendLine("----------------------");
            text.AppendLine("  1. the lock is broken - the member the weapon is homing on is set to null;");
            text.AppendLine("  2. a descent time is chosen once from the height it was found at: at least");
            text.AppendLine("     2.5 s, never more than 40 s, and the height then follows a straight");
            text.AppendLine("     line over that time. A long frame cannot shorten it;");
            text.AppendLine("  3. the nose pitches down at 6 deg/s, or 18 deg/s above 1000 m, and stops at");
            text.AppendLine("     90 degrees - it never keeps turning past straight down;");
            text.AppendLine("  4. the model is only ever moved TO the height the simulation reports, never");
            text.AppendLine("     below it. Dragging it underneath is what produced a missile that looked");
            text.AppendLine("     sunk while the game still had it at 50 ft;");
            text.AppendLine("  5. at the waterline - or at the height this build refuses to let a weapon go");
            text.AppendLine("     below - the game's own end-of-flight routes are tried, one at a time,");
            text.AppendLine("     1.5 s apart: fuel exhausted, life expired, then its own state machine.");
            text.AppendLine();
            text.AppendLine("NOTHING HERE DESTROYS A WEAPON, and nothing here calls Detonate, Explode or");
            text.AppendLine("SelfDestruct. Two earlier builds did: the player saw a warhead go off two");
            text.AppendLine("miles off the bow once per tick, and then a NullReferenceException inside");
            text.AppendLine("GameMain.FixedUpdate with the missile left sitting on the water.");
            text.AppendLine();
            text.AppendLine("A weapon that reaches the surface is NOT removed by this build of the game.");
            text.AppendLine("That was 0.19.0's wrong assumption and the test settled it: the missile sat");
            text.AppendLine("at 50 ft and stayed. So the mod now hands it to the game's own end of");
            text.AppendLine("flight instead, and if every route is refused it says so in the totals rather");
            text.AppendLine("than removing the object behind the game's back.");

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
        1 => "the weapon's own height member",
        2 => "a vertical speed / pitch member",
        3 => "its transform",
        _ => "not used yet",
    };
}
