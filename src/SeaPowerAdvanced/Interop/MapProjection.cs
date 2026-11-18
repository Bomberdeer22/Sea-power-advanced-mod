using System;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;
using UnityEngine;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// Turns a game position into a screen position, so the mod can draw on the map.
///
/// This is the one piece of the mod that has to guess, because there is no dump of the game's map
/// projection and no way to test it offline. So it guesses in order, and it writes down which guess
/// worked:
///
/// <list type="number">
/// <item><b>the map's own arithmetic.</b> The game lays the world out in tiles of 0.2 degrees at
///   330.71396 world units each, offset by the tile the map is currently centred on
///   (<c>Globals._currentCenterTile</c>). That mapping is linear and it is what the game itself
///   uses, so computing it here is exact - and it is the route that matters, because this build's
///   units are ECS entities: they have data, not scene objects, so no unit has a Unity transform to
///   fall back on.</item>
/// <item>a method anywhere in the loaded assemblies that takes a single <see cref="GeoPosition"/> and
///   returns a <c>Vector3</c> or <c>Vector2</c> - the game converting a position to its own world
///   space, if this build has such a helper.</item>
/// <item>the unit's own Unity transform: if the game's units are scene objects, their
///   <c>transform.position</c> is already world space.</item>
/// <item>nothing. The overlay is then switched off and says so, rather than drawing a cone in the
///   wrong place and pretending it is the game's geometry.</item>
/// </list>
///
/// The camera comes from <see cref="Camera.main"/>, falling back to the largest enabled camera, and
/// the result is converted from Unity's bottom-left origin to IMGUI's top-left one.
/// </summary>
internal static class MapProjection
{
    private static bool discovered;
    private static MethodInfo? geoToWorld;

    /// <summary>The tile the map is centred on. Read through reflection each frame, because it
    /// changes as the player pans and the world origin moves with it.</summary>
    private static MemberInfo? centerTile;

    /// <summary>Why the map arithmetic did not run, in words, for the report.</summary>
    private static string mapWhy = "not tried";

    // The game's map grid. Taken from the game's own world layout rather than guessed: 0.2 degrees
    // per tile, 330.71396 world units per tile, and a 1/120 degree edge offset. They are the
    // constants the simulation itself converts with, so a position computed here lands on the unit
    // the game draws, to well under a millimetre.
    private const double UnitsPerTile = 330.71396d;
    private const double DegPerTile = 0.2d;
    private const double TileEdgeDeg = 1d / 120d;

    private static string geoToWorldOwner = "";
    private static Camera? camera;
    private static bool failed;
    private static int misses;
    private static string report = "not yet tried";

    /// <summary>
    /// How many positions in a row may fail to project before the overlay gives up. A few misses are
    /// normal - a unit off the edge of the screen, a camera looking the other way - but dozens in a
    /// row means there is no projection at all, and saying so beats drawing nothing in silence.
    /// </summary>
    private const int MissesBeforeGivingUp = 40;

    /// <summary>What the mod found, for the log and for <c>ecm-cone.txt</c>.</summary>
    internal static string Report => report;

    /// <summary>
    /// The numbers behind the decision, one line each: the centre tile, the origin the arithmetic
    /// produces from it, where that projects to, and the last failure. Written to
    /// <c>ecm-cone.txt</c> so a report from a real install answers the question directly instead of
    /// needing another build to find out.
    /// </summary>
    internal static List<string> Diagnose()
    {
        var lines = new List<string>();
        Discover();

        try
        {
            lines.Add("centre tile       " + (CenterTile(out double x, out double y)
                ? x + " , " + y : "<unreadable> " + mapWhy));

            // The origin of the map's own world, which is where the centre tile lands. If this
            // projects off screen, the camera is not the map camera.
            if (TryMapWorld(new GeoPosition(0, 0, 0), out Vector3 world))
            {
                lines.Add("lat/lon 0,0 ->    " + world.x.ToString("0.0") + ", "
                          + world.y.ToString("0.0") + ", " + world.z.ToString("0.0") + "  (world)");

                Camera? view = ActiveCamera();
                if (view != null)
                {
                    Vector3 point = view.WorldToScreenPoint(world);
                    lines.Add("  -> screen       " + point.x.ToString("0") + ", " + point.y.ToString("0")
                              + "   (screen is " + Screen.width + "x" + Screen.height + ")");
                }
            }
            else
            {
                lines.Add("map arithmetic    did not run: " + mapWhy);
            }

            lines.Add("reflected route   " + (geoToWorld != null
                ? geoToWorldOwner + "." + geoToWorld.Name : "<none found>"));
            lines.Add("camera            " + (camera == null
                ? "<none>"
                : camera.name + (camera.orthographic ? " [ortho]" : "")
                  + " " + camera.pixelWidth + "x" + camera.pixelHeight));
            lines.Add("state             " + (failed ? "switched off" : "trying")
                      + ", " + misses + " miss(es) in a row");
        }
        catch (Exception error)
        {
            lines.Add("diagnostics failed: " + error.GetType().Name + " - " + error.Message);
        }

        return lines;
    }

    /// <summary>False when no projection exists; the overlay stops trying.</summary>
    internal static bool Available
    {
        get
        {
            Discover();
            return !failed;
        }
    }

    private static void Discover()
    {
        if (discovered) return;
        discovered = true;

        try
        {
            geoToWorld = FindGeoToWorld();
        }
        catch (Exception error)
        {
            ModLog.Error("Could not look for a position-to-world method", error);
        }

        try
        {
            camera = FindCamera();
        }
        catch (Exception error)
        {
            ModLog.Error("Could not find a camera to project through", error);
        }

        report = "map grid: " + MapGrid()
                 + "; reflected conversion: " + (geoToWorld != null
                     ? geoToWorldOwner + "." + geoToWorld.Name : "none")
                 + "; camera: " + (camera == null
                     ? "none"
                     : camera.name + (camera.orthographic ? " [ortho]" : "")
                       + " (" + camera.pixelWidth + "x" + camera.pixelHeight + ")")
                 + "; cameras in the scene: " + Cameras();

        ModLog.Info("ECM cone projection: " + report);
    }

    /// <summary>
    /// The centre tile, or a sentence saying why it could not be read. Cached by name on first use,
    /// read every time it is needed after that.
    /// </summary>
    private static bool CenterTile(out double x, out double y)
    {
        x = 0d;
        y = 0d;

        try
        {
            if (centerTile == null)
            {
                // Found by name on the game assembly rather than named in code: the tile member is
                // the only thing needed from Globals here, and a build that renames the class should
                // report that instead of failing to compile.
                Type? globals = typeof(ObjectBase).Assembly.GetType("SeaPower.Globals", false);

                if (globals == null)
                {
                    mapWhy = "this build has no SeaPower.Globals";
                    return false;
                }

                foreach (MemberInfo member in MemberProbe.All(globals))
                {
                    if (member is not FieldInfo && member is not PropertyInfo) continue;

                    string name = member.Name.ToLowerInvariant();
                    if (!name.Contains("centertile") && !name.Contains("currentcenter")) continue;

                    centerTile = member;
                    break;
                }
            }

            if (centerTile == null)
            {
                mapWhy = "Globals has no centre-tile member on this build";
                return false;
            }

            object? value = MemberProbe.Read(centerTile, null);
            if (value == null)
            {
                mapWhy = "the centre tile read as null";
                return false;
            }

            // Vector2, Vector2Int, or the game's own struct: any of them has x and y.
            if (!Component(value, "x", out x) || !Component(value, "y", out y))
            {
                mapWhy = "the centre tile has no readable x/y";
                return false;
            }

            mapWhy = "";
            return true;
        }
        catch (Exception error)
        {
            mapWhy = "the centre tile threw " + error.GetType().Name;
            return false;
        }
    }

    /// <summary>One component of a small position struct, by name or by field position.</summary>
    private static bool Component(object value, string name, out double number)
    {
        number = 0d;

        try
        {
            MemberInfo? member = MemberProbe.Value(value.GetType(), name);
            object? raw = MemberProbe.Read(member, value);

            if (raw == null)
            {
                // A Vector2Int exposes x/y as properties, a Vector2 as fields - MemberProbe covers
                // both, but a struct with different names does not. Fall back to the field order.
                Type type = value.GetType();
                string[] order = name == "x" ? new[] { "X", "_x" } : new[] { "Y", "_y" };

                member = MemberProbe.Value(type, order);
                raw = MemberProbe.Read(member, value);
            }

            if (raw == null) return false;

            number = Convert.ToDouble(raw);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Whether the map arithmetic can run at all, for the report.</summary>
    private static string MapGrid()
    {
        return CenterTile(out double x, out double y)
            ? "centre tile " + x + "," + y
            : mapWhy;
    }

    /// <summary>
    /// The game's own placement of a position in world space: tiles of 0.2 degrees at 330.71396
    /// units, offset by the tile the map is centred on.
    ///
    /// This is the route that makes the cones drawable. The game's units are ECS entities and have
    /// no Unity transform, so the only way to put anything on the map is to do this arithmetic; it
    /// is the same arithmetic the game does, so the result is exact rather than approximate.
    /// </summary>
    private static bool TryMapWorld(GeoPosition position, out Vector3 world)
    {
        world = Vector3.zero;

        if (!CenterTile(out double centreX, out double centreY)) return false;

        try
        {
            double lat = position.Latitude;
            double lon = position.Longitude;
            double height = HeightOf(position);

            double lonTiles = (lon + 180d - TileEdgeDeg) / DegPerTile - Math.Floor(centreX);

            // The antimeridian: the tile index jumps by a whole world when the map is centred the
            // other side of 180 degrees, exactly as the game corrects for it.
            if (Math.Abs(lonTiles) > 1795d) lonTiles += lonTiles > 1795d ? -1800d : 1800d;

            double latTiles = (90d - lat - TileEdgeDeg) / DegPerTile - Math.Floor(centreY);

            world = new Vector3(
                (float)(UnitsPerTile * (lonTiles - 0.5d)),
                (float)height,
                (float)(-UnitsPerTile * (latTiles - 0.5d)));

            return true;
        }
        catch (Exception error)
        {
            mapWhy = "the map arithmetic threw " + error.GetType().Name;
            return false;
        }
    }

    /// <summary>A position's height above the sea, or zero when this build does not expose it.</summary>
    private static double HeightOf(GeoPosition position)
    {
        try
        {
            MemberInfo? member = MemberProbe.Value(typeof(GeoPosition), "_height");
            object? raw = MemberProbe.Read(member, position);
            return raw == null ? 0d : Convert.ToDouble(raw);
        }
        catch (Exception) { return 0d; }
    }

    /// <summary>
    /// A method that takes one GeoPosition and returns a Vector3 or Vector2. Searched across every
    /// loaded assembly because the mod cannot reference the game's map code, and the type that owns
    /// the conversion is not in any dump this project has.
    /// </summary>
    private static MethodInfo? FindGeoToWorld()
    {
        Assembly[] assemblies;
        try { assemblies = AppDomain.CurrentDomain.GetAssemblies(); }
        catch (Exception) { return null; }

        foreach (Assembly assembly in assemblies)
        {
            string name;
            try { name = assembly.GetName().Name ?? ""; }
            catch (Exception) { continue; }

            if (name.StartsWith("System") || name.StartsWith("Unity") || name.StartsWith("mscorlib")
                || name.StartsWith("netstandard") || name.Contains("Harmony")
                || name.Contains("BepInEx") || name.Contains("0Harmony")
                || name.Contains("SeaPowerAdvanced"))
                continue;

            Type?[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException partial) { types = partial.Types ?? Array.Empty<Type?>(); }
            catch (Exception) { continue; }

            foreach (Type? type in types)
            {
                if (type == null) continue;

                MethodInfo?[] methods;
                try { methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                | BindingFlags.Static | BindingFlags.Instance
                                                | BindingFlags.DeclaredOnly); }
                catch (Exception) { continue; }

                foreach (MethodInfo? method in methods)
                {
                    if (method == null) continue;

                    try
                    {
                        if (method.ReturnType != typeof(Vector3) && method.ReturnType != typeof(Vector2))
                            continue;

                        ParameterInfo[] parameters = method.GetParameters();
                        if (parameters.Length != 1) continue;
                        if (parameters[0].ParameterType != typeof(GeoPosition)) continue;

                        geoToWorldOwner = type.FullName ?? type.Name;
                        return method;
                    }
                    catch (Exception) { }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The camera that renders the map.
    ///
    /// <c>Camera.main</c> is a tag, not a promise, and a game with a UI camera will happily return
    /// the UI one. So the candidates are scored on the only evidence available: a point at the
    /// world origin is where the map is centred, so the camera that puts it on screen is the map
    /// camera. Camera.main wins ties, and the largest camera is the last resort.
    /// </summary>
    /// <summary>The camera to project through, looked for again when a mission change removed it.</summary>
    private static Camera? ActiveCamera()
    {
        if (camera != null) return camera;

        camera = FindCamera();
        return camera;
    }

    private static Camera? FindCamera()
    {
        try
        {
            Camera? best = null;
            int bestScore = int.MinValue;

            foreach (Camera candidate in Camera.allCameras)
            {
                if (candidate == null || !candidate.isActiveAndEnabled) continue;

                int score = 0;
                if (candidate == Camera.main) score += 2;

                try
                {
                    Vector3 middle = candidate.WorldToScreenPoint(Vector3.zero);
                    if (middle.z > 0f
                        && middle.x >= -candidate.pixelWidth && middle.x <= candidate.pixelWidth * 2f
                        && middle.y >= -candidate.pixelHeight && middle.y <= candidate.pixelHeight * 2f)
                        score += 4;
                }
                catch (Exception) { }

                score += (candidate.pixelWidth * candidate.pixelHeight) / 100000;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best ?? Camera.main;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Every camera in the scene, so the report names what was available to choose from.</summary>
    private static string Cameras()
    {
        try
        {
            var names = new List<string>();
            foreach (Camera candidate in Camera.allCameras)
            {
                if (candidate == null) continue;
                names.Add(candidate.name + (candidate.orthographic ? " [ortho]" : "")
                          + (candidate == Camera.main ? " [main]" : ""));
            }
            return names.Count == 0 ? "none" : string.Join(", ", names);
        }
        catch (Exception) { return "unreadable"; }
    }

    /// <summary>
    /// Screen position (IMGUI coordinates, origin top-left) for a game position. False when it
    /// cannot be computed, which the caller must treat as "do not draw", never as "draw at zero".
    /// </summary>
    internal static bool TryScreen(GeoPosition position, out Vector2 screen)
    {
        screen = Vector2.zero;

        Discover();
        if (failed) return false;

        try
        {
            Vector3 world;

            // 1. The map's own arithmetic. Exact, and the only route that works on units which are
            //    data rather than scene objects.
            if (TryMapWorld(position, out world))
            {
                // A camera can be destroyed and rebuilt between missions, so a missing one is a
                // moment to look again, never a reason to switch the overlay off for good.
                Camera? view = ActiveCamera();
                if (view == null) return false;

                Vector3 grid = view.WorldToScreenPoint(world);
                if (grid.z < 0f) return false;    // behind the camera: not visible, not a fault

                screen = new Vector2(grid.x, Screen.height - grid.y);
                misses = 0;
                return true;
            }

            // 2. A conversion method the game exposes, if this build has one.
            if (geoToWorld != null)
            {
                object? owner = null;
                if (!geoToWorld.IsStatic) owner = FindOwnerInstance(geoToWorld.DeclaringType!);

                object? result = geoToWorld.Invoke(owner, new object[] { position });
                if (result is Vector3 three) world = three;
                else if (result is Vector2 two) world = new Vector3(two.x, two.y, 0f);
                else return Fail("the position method returned nothing usable");
            }
            else
            {
                // Nothing on this build converts a position: the caller needs a unit, which
                // TryScreen(object) handles. Not a fault on its own.
                return false;
            }

            Camera? projectionCamera = ActiveCamera();
            if (projectionCamera == null) return false;

            Vector3 projected = projectionCamera.WorldToScreenPoint(world);
            if (projected.z < 0f) return false;   // behind the camera: not an error, just not visible

            screen = new Vector2(projected.x, Screen.height - projected.y);
            return true;
        }
        catch (Exception error)
        {
            return Fail("projection failed: " + error.GetType().Name);
        }
    }

    /// <summary>Screen position for a unit, trying its own transform when there is no conversion.</summary>
    internal static bool TryScreen(object? subject, out Vector2 screen)
    {
        screen = Vector2.zero;

        if (subject == null) return false;

        Discover();
        if (failed) return false;

        // 1. By position: the map arithmetic, then any conversion method the game exposes.
        if (TryPosition(subject, out GeoPosition position) && TryScreen(position, out screen))
            return true;

        // 2. The unit's transform. Only works if the game's units are scene objects, which is what
        //    this branch is here to find out.
        try
        {
            object? transform = MemberProbe.Read(MemberProbe.Value(subject.GetType(), "transform"), subject);

            if (transform is Transform sceneTransform)
            {
                Camera? view = ActiveCamera();
                if (view == null) return false;

                Vector3 projected = view.WorldToScreenPoint(sceneTransform.position);

                if (projected.z < 0f) return false;   // off screen, not a fault

                screen = new Vector2(projected.x, Screen.height - projected.y);
                misses = 0;
                return true;
            }
        }
        catch (Exception error)
        {
            ModLog.Error("Transform projection failed", error);
        }

        // Nothing could be projected for this object. Let the overlay try a few more before
        // concluding there is no projection in this build at all.
        if (++misses < MissesBeforeGivingUp) return false;

        return Fail("nothing projected: " + mapWhy + "; camera: "
                    + (camera == null ? "none" : camera.name));
    }

    private static bool TryPosition(object subject, out GeoPosition position)
    {
        position = new GeoPosition(0, 0, 0);

        try
        {
            MethodInfo? method = MemberProbe.Method(subject.GetType(), 0, "getGeoPosition");
            if (method == null) return false;

            object? result = method.Invoke(subject, null);
            if (result is GeoPosition found)
            {
                position = found;
                return true;
            }
        }
        catch (Exception) { }

        return false;
    }

    private static object? FindOwnerInstance(Type type)
    {
        try
        {
            object? scene = UnityEngine.Object.FindFirstObjectByType(type);
            if (scene != null) return scene;

            UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(type);
            return all.Length > 0 ? all[0] : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Gives up for the rest of the session and explains why. Called rather than throwing: a missing
    /// projection is a feature that cannot be drawn, not a fault.
    /// </summary>
    private static bool Fail(string why)
    {
        failed = true;
        report = why;
        ModLog.Warn("ECM cone switched off: " + why);
        return false;
    }
}
