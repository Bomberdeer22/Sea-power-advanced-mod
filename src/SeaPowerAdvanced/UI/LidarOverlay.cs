using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Features;
using SeaPowerAdvanced.Interop;
using UnityEngine;

namespace SeaPowerAdvanced.UI;

/// <summary>
/// Draws the scanning LIDAR beams on the map, and flips the map into a dark "infrared" view so the
/// beams are the thing you see.
///
/// This is the visible half of the feature: a set that covers 360 degrees by turning is only
/// convincing if you can watch it turn. So each ship with the scanner running gets
///
/// <list type="bullet">
/// <item><b>a spoke</b> from the ship to the far edge of its range, in the direction the head is
///   pointing - it sweeps round at the configured rate, and it is the beam, not a heading;</item>
/// <item><b>a ring</b> at the range the set reaches, so 15 nm is visible rather than a number in a
///   config file;</item>
/// <item><b>a wedge</b> for the beam width, drawn as two thin spokes, so a 6-degree beam looks
///   narrower than a 30-degree one;</item>
/// <item><b>a bright mark</b> on whatever the beam is holding, labelled with the range - and the
///   line only lights up when the set has actually sighted something on this pass.</item>
/// </list>
///
/// <b>The infrared view</b> (F10 by default, or the menu entry) tints the whole screen, draws a
/// vignette, hides the beams' labels, and dims the ring - so what is left is the dark sea, the ships,
/// and a bright spoke turning on each one. It is a filter over what is already drawn, not a new
/// renderer: the mod cannot replace the game's own map materials, and pretending otherwise would put
/// a fake sensor picture over the real one.
///
/// Everything is IMGUI, like the ECM cones, and everything comes from
/// <see cref="MapProjection"/>: the same projection the cones use, with the same rule that nothing is
/// drawn where a position cannot be converted.
/// </summary>
public sealed class LidarOverlay : MonoBehaviour
{
    private static LidarOverlay? instance;

    private Texture2D? white;
    private bool reported;
    private bool disabled;

    // Rebuilt a few times a second, not every GUI event: collecting means walking the fleet.
    private List<Lidar.Beam> beams = new();
    private float nextRefresh;

    /// <summary>True while the map is being drawn in the infrared view.</summary>
    private static bool infrared;

    /// <summary>Installed once at startup; does nothing until a mission is running.</summary>
    internal static void Install()
    {
        if (instance != null) return;

        try
        {
            var host = new GameObject("SeaPowerAdvanced.LidarOverlay");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<LidarOverlay>();
        }
        catch (Exception error)
        {
            ModLog.Error("Could not start the LIDAR overlay", error);
        }
    }

    /// <summary>Flips the map into, or out of, the infrared view. Returns the new state.</summary>
    internal static bool ToggleInfrared()
    {
        infrared = !infrared;
        ModLog.Info("LIDAR: infrared view " + (infrared ? "on" : "off"));
        return infrared;
    }

    /// <summary>For the menu label: which view the map is in.</summary>
    internal static bool Infrared => infrared;

    private void Awake()
    {
        try
        {
            white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
        }
        catch (Exception error)
        {
            ModLog.Error("Could not create the LIDAR overlay texture", error);
            disabled = true;
        }
    }

    private void Update()
    {
        // The key is read here rather than in OnGUI because OnGUI runs several times a frame and a
        // held key would flip the view back and forth faster than a person can see.
        try
        {
            if (!ModSettings.LidarOverlay && !infrared) return;

            KeyCode key = ModSettings.LidarOverlayKey;
            if (key == KeyCode.None) return;
            if (!Input.GetKeyDown(key)) return;

            ToggleInfrared();
        }
        catch (Exception) { }
    }

    private void OnGUI()
    {
        if (disabled || white == null) return;
        if (!ModSettings.FeaturesEnabled) return;
        if (!ModSettings.LidarOverlay && !infrared) return;

        // Draw once per frame, not once per event.
        if (Event.current == null || Event.current.type != EventType.Repaint) return;

        try
        {
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.1f;
                beams = Lidar.Beams();
            }

            if (infrared) DrawInfrared();

            if (!ModSettings.LidarOverlay) return;
            if (beams.Count == 0) return;

            if (!MapProjection.Available)
            {
                DrawNote();
                return;
            }

            foreach (Lidar.Beam beam in beams) DrawBeam(beam);

            if (!reported)
            {
                reported = true;
                ModLog.Info("LIDAR overlay: drawing " + beams.Count + " beam(s). "
                            + MapProjection.Report);
            }
        }
        catch (Exception error)
        {
            disabled = true;
            ModLog.Error("The LIDAR overlay stopped after an error", error);
        }
    }

    // --- the infrared view ---------------------------------------------------------------------

    /// <summary>
    /// The filter itself: a dark tint over the whole screen, a vignette to pull the eye to the
    /// middle, and a strip of text naming the view so a screenshot is self-explanatory. Deliberately
    /// faint enough that the game's own map is still readable - the point is to see the beams, not to
    /// replace the display.
    /// </summary>
    private void DrawInfrared()
    {
        Color previous = GUI.color;

        GUI.color = new Color(0.02f, 0.05f, 0.02f, 0.45f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), white);

        // Vignette: four bars round the edge, darker than the centre tint. Cheaper than a texture and
        // it does not need a shader the game's UI may not have.
        float edge = Mathf.Min(Screen.width, Screen.height) * 0.06f;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, edge), white);
        GUI.DrawTexture(new Rect(0f, Screen.height - edge, Screen.width, edge), white);
        GUI.DrawTexture(new Rect(0f, 0f, edge, Screen.height), white);
        GUI.DrawTexture(new Rect(Screen.width - edge, 0f, edge, Screen.height), white);

        GUI.color = new Color(0.35f, 1f, 0.45f, 0.95f);
        GUI.Label(new Rect(edge + 8f, edge + 6f, 700f, 20f),
                  "INFRARED - LIDAR scanning   (" + ModSettings.LidarOverlayKey + " to return to the map)");

        GUI.color = previous;
    }

    // --- the beams -----------------------------------------------------------------------------

    private void DrawBeam(Lidar.Beam beam)
    {
        if (!MapProjection.TryScreen(beam.Ship, out Vector2 from)) return;

        GeoPosition? centre = Position(beam.Ship);
        if (!centre.HasValue) return;

        // Both ends of the spoke are computed in the game's own coordinates and then projected, so
        // the beam is the range it says it is rather than a fixed number of pixels. A pixel-length
        // beam would look right at one zoom level and lie at every other.
        double far = Math.Max(0.5, beam.RangeNm) / 60.0;   // nm -> degrees of latitude

        Color previous = GUI.color;

        // The ring: how far the set reaches.
        GUI.color = infrared ? new Color(0.2f, 0.9f, 0.35f, 0.22f) : new Color(0.25f, 0.95f, 1f, 0.18f);
        DrawCircle(beam, centre.Value, far);

        // The beam: a spoke to the far edge, brighter when it is holding something.
        Vector2 tip = Beyond(from, beam, centre.Value, far);

        if (tip != Vector2.zero)
        {
            float width = beam.Active ? 3f : 2f;

            GUI.color = infrared
                ? (beam.Active ? new Color(0.45f, 1f, 0.5f, 0.95f) : new Color(0.3f, 0.95f, 0.4f, 0.8f))
                : (beam.Active ? new Color(0.75f, 1f, 0.9f, 0.95f) : new Color(0.3f, 0.9f, 1f, 0.6f));

            Line(from, tip, width);

            // The beam's width, as two thin spokes: a 6-degree beam visibly differs from a 30-degree
            // one, which a single line cannot show.
            float half = Mathf.Max(1f, ModSettings.LidarBeamDeg) / 2f;
            Vector2 left = Beyond(from, beam, centre.Value, far, -half);
            Vector2 right = Beyond(from, beam, centre.Value, far, half);

            GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, GUI.color.a * 0.45f);
            if (left != Vector2.zero) Line(from, left, 1f);
            if (right != Vector2.zero) Line(from, right, 1f);
        }

        // The far end of the beam, and what it is holding.
        if (beam.Active)
        {
            GUI.color = infrared ? new Color(0.5f, 1f, 0.55f, 1f) : new Color(0.8f, 1f, 0.95f, 1f);

            if (tip != Vector2.zero)
            {
                const float box = 6f;
                GUI.DrawTexture(new Rect(tip.x - box / 2f, tip.y - box / 2f, box, box), white);
            }

            if (!infrared)
            {
                GUI.Label(new Rect(tip.x + 8f, tip.y - 8f, 260f, 18f), "LIDAR: " + beam.Holding);
            }
        }

        // Where the set is mounted, so a ship with the order is findable before it sights anything.
        GUI.color = infrared ? new Color(0.4f, 1f, 0.5f, 0.9f) : new Color(0.5f, 0.95f, 1f, 0.7f);
        DrawRing(from, 10f);

        if (!infrared && beam.Active)
        {
            GUI.Label(new Rect(from.x + 12f, from.y + 4f, 280f, 18f), "LIDAR scanning " + (int)beam.RangeNm + " nm");
        }

        GUI.color = previous;
    }

    /// <summary>A ring of short segments at the set's range: an ellipse on screen, because the map is
    /// a projection and a circle of constant range is not a circle of constant pixels.</summary>
    private void DrawCircle(Lidar.Beam beam, GeoPosition centre, double degrees)
    {
        const int segments = 48;
        Vector2 previousPoint = Vector2.zero;

        for (int i = 0; i <= segments; i++)
        {
            double bearing = i * 360.0 / segments;

            Vector2 point = OnScreen(Beyond(centre, bearing, degrees));

            if (point == Vector2.zero) { previousPoint = Vector2.zero; continue; }

            if (previousPoint != Vector2.zero) Line(previousPoint, point, 1f);
            previousPoint = point;
        }
    }

    // --- geometry ------------------------------------------------------------------------------

    /// <summary>The point <paramref name="degrees"/> of latitude away from a position, on a bearing.
    /// Small distances, so a flat approximation is exact to well under a pixel.</summary>
    private static GeoPosition Beyond(GeoPosition from, double bearingDegrees, double degrees)
    {
        double radians = bearingDegrees * Math.PI / 180.0;

        double lat = from.Latitude + degrees * Math.Cos(radians);
        double lon = from.Longitude
                     + (degrees * Math.Sin(radians))
                       / Math.Max(0.05, Math.Cos(from.Latitude * Math.PI / 180.0));

        // GeoPosition is (latitude, longitude, height); this build exposes no Height property,
        // and a track on the sea does not need one.
        return new GeoPosition(lat, lon, 0);
    }

    /// <summary>The beam's far end on screen: the ship's own bearing from the set, at the set's
    /// range. Zero when it cannot be projected, which the caller treats as "do not draw".</summary>
    private Vector2 Beyond(Vector2 from, Lidar.Beam beam, GeoPosition centre, double degrees,
                           float extraDegrees = 0f)
    {
        return OnScreen(Beyond(centre, beam.Bearing + extraDegrees, degrees));
    }

    /// <summary>
    /// A position on screen, or zero when it cannot be projected.
    ///
    /// Named <c>OnScreen</c>, not <c>Screen</c>: a member called Screen hides UnityEngine's own
    /// Screen type inside this class, and every <c>Screen.width</c> in the file then fails to
    /// compile - CS0119, sixteen errors from one unlucky name.
    /// </summary>
    private static Vector2 OnScreen(GeoPosition position) =>
        MapProjection.TryScreen(position, out Vector2 point) ? point : Vector2.zero;

    /// <summary>The ship's position, or null when it cannot be read. Null is "do not draw".</summary>
    private static GeoPosition? Position(ObjectBase unit)
    {
        try { return unit.getGeoPosition(); }
        catch (Exception) { return null; }
    }

    // --- primitives ----------------------------------------------------------------------------

    /// <summary>A straight line, drawn as a rotated rectangle: the same trick the ECM cones use.</summary>
    private void Line(Vector2 from, Vector2 to, float width)
    {
        Vector2 delta = to - from;
        float length = delta.magnitude;
        if (length < 2f) return;

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        Matrix4x4 saved = GUI.matrix;
        try
        {
            GUIUtility.RotateAroundPivot(angle, from);
            GUI.DrawTexture(new Rect(from.x, from.y - width / 2f, length, width), white);
        }
        finally { GUI.matrix = saved; }
    }

    private void DrawRing(Vector2 at, float size)
    {
        float half = size / 2f;
        const float thickness = 1.5f;

        GUI.DrawTexture(new Rect(at.x - half, at.y - half, size, thickness), white);
        GUI.DrawTexture(new Rect(at.x - half, at.y + half - thickness, size, thickness), white);
        GUI.DrawTexture(new Rect(at.x - half, at.y - half, thickness, size), white);
        GUI.DrawTexture(new Rect(at.x + half - thickness, at.y - half, thickness, size), white);
    }

    private void DrawNote()
    {
        Color previous = GUI.color;
        GUI.color = new Color(1f, 0.85f, 0.4f, 0.9f);
        GUI.Label(new Rect(12f, Screen.height - 58f, 1100f, 20f),
                  "Sea Power Advanced: LIDAR beams cannot be drawn - see lidar.txt in the mod folder "
                  + "(" + MapProjection.Report + ")");
        GUI.color = previous;

        if (!reported)
        {
            reported = true;
            ModLog.Warn("LIDAR overlay: cannot draw - " + MapProjection.Report);
            Lidar.WriteReport("the overlay could not project a position");
        }
    }

    private void OnDestroy()
    {
        if (white != null) Destroy(white);
        instance = null;
    }
}
