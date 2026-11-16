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
/// <item>a method anywhere in the loaded assemblies that takes a single <see cref="GeoPosition"/> and
///   returns a <c>Vector3</c> or <c>Vector2</c> - the game converting a position to its own world
///   space. If one exists, that is exact and nothing else is needed.</item>
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

        report = (geoToWorld != null
                     ? "position method: " + geoToWorldOwner + "." + geoToWorld.Name
                     : "position method: none found - falling back to each unit's transform")
                 + "; camera: " + (camera == null ? "none" : camera.name + " (" + camera.pixelWidth + "x"
                                                   + camera.pixelHeight + ")");

        ModLog.Info("ECM cone projection: " + report);
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

    private static Camera? FindCamera()
    {
        try
        {
            if (Camera.main != null) return Camera.main;

            Camera? best = null;
            foreach (Camera candidate in Camera.allCameras)
            {
                if (candidate == null || !candidate.isActiveAndEnabled) continue;
                if (best == null || candidate.pixelWidth * candidate.pixelHeight
                    > best.pixelWidth * best.pixelHeight)
                    best = candidate;
            }
            return best;
        }
        catch (Exception) { return null; }
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
                // No conversion available and no transform on a raw position: this route needs a
                // unit, which TryScreen(object) handles. Not a fault on its own.
                return false;
            }

            if (camera == null) return Fail("no active camera");

            Vector3 projected = camera.WorldToScreenPoint(world);
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

        // 1. The game's own conversion, if it exists, using the unit's position.
        if (geoToWorld != null && TryPosition(subject, out GeoPosition position)
            && TryScreen(position, out screen))
            return true;

        // 2. The unit's transform. Only works if the game's units are scene objects, which is what
        //    this branch is here to find out.
        try
        {
            object? transform = MemberProbe.Read(MemberProbe.Value(subject.GetType(), "transform"), subject);

            if (transform is Transform sceneTransform)
            {
                if (camera == null) return Fail("no active camera");

                Vector3 projected = camera.WorldToScreenPoint(sceneTransform.position);

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

        return Fail("nothing projected: this build exposes neither a GeoPosition-to-world method nor "
                    + "a Unity transform on its units; reported camera: "
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
