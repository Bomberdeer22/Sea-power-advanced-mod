using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Features;
using SeaPowerAdvanced.Interop;
using UnityEngine;

namespace SeaPowerAdvanced.UI;

/// <summary>
/// Draws the scanning LIDAR on the map, in two views.
///
/// <b>The overlay view</b> (default) is a dark, faintly green sea with the scanner's own light on it:
/// a glow at the emitter, a beam reaching to the set's range, a ring, and a fading trail where the beam
/// has just been. That is the answer to "I want to see the lidar being used": a beam that only appears
/// once it has found something is a beam nobody can see, so the overlay says where the set is, which
/// way it is pointing, how far it reaches and how fast it turns, whether or not anything is on it.
///
/// <b>The infrared view</b> (<c>LidarOverlayKey</c>, F10 by default) drops the darkness and shows the
/// beams over the game's own map unchanged - the view to use when the point is the game's picture
/// rather than the scanner.
///
/// The two would have been one view had the game's map materials been replaceable. They are not: all
/// this can do is draw over the screen, so a "night" view is a tint, and what it buys is contrast.
/// The report says so rather than pretending the game itself changed.
///
/// <b>Nothing here guesses silently.</b> A position is only drawn when the projection converts it; when
/// that fails the reason is written to <c>lidar.txt</c>. And because a projection that is exactly right
/// in world space can still land in the wrong place on screen - the camera is a choice, and a
/// half-screen offset is invisible as a bug - the first beam is drawn once with a calibration reading:
/// where the projection puts a known position, where the game puts a hull's own label, and the
/// difference in pixels.
/// </summary>
public sealed class LidarOverlay : MonoBehaviour
{
    private static LidarOverlay? instance;

    private Texture2D? white;
    private Texture2D? beamTexture;
    private bool reported;
    private bool calibrated;
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

    /// <summary>Flips the map between the dark overlay view and the infrared view. Returns the new
    /// state.</summary>
    internal static bool ToggleInfrared()
    {
        infrared = !infrared;
        ModLog.Info("LIDAR: " + (infrared ? "infrared view (the game's map, lit beams)"
                                          : "overlay view (dark sea, the beam the only light)"));
        return infrared;
    }

    /// <summary>For the menu label: which view the map is in.</summary>
    internal static bool Infrared => infrared;

    /// <summary>
    /// Why the map is empty, in one sentence, for the menu entry that asks.
    ///
    /// There are only four answers and they are worth separating, because "nothing on the map" looks
    /// identical whichever one it is: no hull has the order; the settings forbid it; no beam could be
    /// projected; or the beams are being drawn somewhere off screen. The last is the subtle one, so it
    /// gets its own check rather than being lumped in with the others.
    /// </summary>
    internal static string Diagnose()
    {
        if (!ModSettings.FeaturesEnabled) return "Features/Enabled is off, so nothing the mod draws runs.";

        if (!ModSettings.LidarEnabled) return "Features/LidarEnabled is off, so no hull may scan.";

        List<Lidar.Beam> live;
        try { live = Lidar.Beams(); }
        catch (Exception error) { return "The fleet could not be read: " + error.GetType().Name + "."; }

        if (live.Count == 0)
        {
            return "No ship has the scanner order. Right-click a ship > Sensors > LIDAR: off - and the "
                   + "beam appears on it at once.";
        }

        if (!MapProjection.Available)
        {
            return "The map could not be projected on this build (" + MapProjection.Report + "), so no "
                   + "beam can be placed. lidar.txt has the numbers.";
        }

        // Does a beam on a hull that is certainly in front of the player land on the screen? If it
        // does not, the projection is fine and the camera is wrong - worth saying differently,
        // because the fix is different.
        try
        {
            ObjectBase ship = live[0].Ship;

            if (!MapProjection.TryScreen(ship.getGeoPosition(), out Vector2 at))
                return "A beam exists, but its ship's position could not be projected - the camera "
                       + "this build reports cannot see it. lidar.txt has the camera list.";

            if (at.x < -2000f || at.x > Screen.width + 2000f
                || at.y < -2000f || at.y > Screen.height + 2000f)
            {
                return "The projection puts the beam at " + at.x.ToString("0") + ", "
                       + at.y.ToString("0") + " on a " + Screen.width + "x" + Screen.height
                       + " screen - off the map. The camera choice is wrong, not the beam. lidar.txt "
                       + "has the reading to send back.";
            }

            return live.Count + " beam(s) drawing, first at " + at.x.ToString("0") + ", "
                   + at.y.ToString("0") + " on a " + Screen.width + "x" + Screen.height
                   + " screen, head " + (int)live[0].Bearing + " deg. If the map still looks empty, "
                   + "the beams are being drawn - check the overlay setting and the zoom level.";
        }
        catch (Exception error)
        {
            return "The diagnosis itself failed: " + error.GetType().Name + " - " + error.Message;
        }
    }

    private void Awake()
    {
        try
        {
            white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            white.SetPixel(0, 0, Color.white);
            white.Apply();

            // A soft dot rather than a square: an emitter and a sighting both read as light, and a
            // square reads as a debug marker. Built once, 32x32, so the cost is nothing.
            beamTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            beamTexture.wrapMode = TextureWrapMode.Clamp;

            for (int y = 0; y < 32; y++)
            {
                for (int x = 0; x < 32; x++)
                {
                    float dx = (x - 15.5f) / 15.5f;
                    float dy = (y - 15.5f) / 15.5f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(1f - distance);
                    beamTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * alpha));
                }
            }

            beamTexture.Apply();
        }
        catch (Exception error)
        {
            ModLog.Error("Could not create the LIDAR overlay textures", error);
            disabled = true;
        }
    }

    private void Update()
    {
        // The key is read here, not in OnGUI: OnGUI runs several times a frame and a held key would
        // flip the view back and forth faster than a person can see. It is ALSO read from the GUI
        // event, below, because a build that runs the new Input System only throws from
        // Input.GetKeyDown - the mod learned that on the civilian-traffic panel, and there is no
        // reason for the LIDAR to relearn it.
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

    /// <summary>The other way in: the key event as the interface sees it, for builds where reading
    /// <c>Input</c> throws. Returns true when this event was the key.</summary>
    private static bool KeyFromEvent(Event current)
    {
        try
        {
            KeyCode key = ModSettings.LidarOverlayKey;
            if (key == KeyCode.None) return false;
            if (current == null || current.type != EventType.KeyDown) return false;
            if (current.keyCode != key) return false;

            ToggleInfrared();
            return true;
        }
        catch (Exception) { return false; }
    }

    private void OnGUI()
    {
        if (disabled || white == null || beamTexture == null) return;
        if (!ModSettings.FeaturesEnabled) return;

        // The key, before the early return: a person pressing the key while the overlay is off is
        // asking for it on, and reading the key only after the "is it on" test would ignore them.
        bool flipped = KeyFromEvent(Event.current);

        // The view just changed, so rebuild on this frame rather than up to a tenth of a second
        // later - a key press that appears to do nothing for a moment reads as one that does nothing.
        if (flipped) nextRefresh = 0f;

        if (!ModSettings.LidarOverlay && !infrared) return;

        // Draw once per frame, not once per event.
        if (Event.current == null || Event.current.type != EventType.Repaint) return;

        try
        {
            // Behind the game's own interface, above the map. A full-screen tint at the default depth
            // would sit over the game's menus and dim them, which is how an overlay comes to feel like
            // it has taken the screen over. A high depth is drawn first, so the game's panels win.
            GUI.depth = 1000;

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.1f;
                beams = Lidar.Beams();
            }

            // Nothing to show is not the same as nothing drawn: the note below says which it is, so
            // "I see nothing" on the screen has an answer in the log and in lidar.txt.
            if (beams.Count == 0)
            {
                if (!infrared) DrawIdle();
                return;
            }

            if (!MapProjection.Available)
            {
                DrawNote();
                return;
            }

            // The dark sea first: everything else is drawn on top of it.
            if (!infrared) DrawNight();

            foreach (Lidar.Beam beam in beams) DrawBeam(beam);

            if (!calibrated) Calibrate(beams[0].Ship);

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

    // --- the two views -------------------------------------------------------------------------

    /// <summary>
    /// The overlay view: a dark, slightly green sea, a vignette at the edges, and a legend. Not a
    /// replacement for the game's map - a tint over it - but dark enough that a 3-pixel beam is the
    /// brightest thing on the screen, which is the only way a beam that thin can be seen at all.
    /// </summary>
    private void DrawNight()
    {
        Color previous = GUI.color;

        // How dark is a setting, not a decision made here: a fixed value is a guess about somebody
        // else's monitor, and at 0 the same code gives a "beams only, nothing dimmed" mode.
        float shade = Mathf.Clamp01(ModSettings.LidarShade);

        GUI.color = new Color(0.01f, 0.04f, 0.02f, shade);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), white);

        // Vignette: four bars round the edge. Cheaper than a texture, and it needs no shader the
        // game's interface may not have.
        float edge = Mathf.Min(Screen.width, Screen.height) * 0.07f;

        GUI.color = new Color(0f, 0f, 0f, 0.5f * shade);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, edge), white);
        GUI.DrawTexture(new Rect(0f, Screen.height - edge, Screen.width, edge), white);
        GUI.DrawTexture(new Rect(0f, 0f, edge, Screen.height), white);
        GUI.DrawTexture(new Rect(Screen.width - edge, 0f, edge, Screen.height), white);

        GUI.color = new Color(0.3f, 1f, 0.42f, 0.9f);
        GUI.Label(new Rect(edge + 10f, edge + 6f, 900f, 20f),
                  "LIDAR - " + beams.Count + " set(s) turning   ("
                  + ModSettings.LidarOverlayKey + " for the game's map, brighter beams)");

        GUI.color = previous;
    }

    /// <summary>Nothing is drawn because nothing is running: say so on the map, once, instead of
    /// leaving a blank screen that looks like a broken overlay.</summary>
    private void DrawIdle()
    {
        Color previous = GUI.color;
        GUI.color = new Color(0.55f, 0.95f, 0.65f, 0.75f);
        GUI.Label(new Rect(14f, Screen.height - 76f, 1400f, 18f),
                  "LIDAR: no ship has the scanner running - right-click a ship > Sensors > LIDAR");

        if (!reported)
        {
            reported = true;
            ModLog.Info("LIDAR overlay: nothing to draw - no hull has the scanner order, so no beam "
                        + "exists yet. The order is in a ship's Sensors submenu.");
        }

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

        bool lit = beam.Active;
        bool dark = !infrared;

        // 1. The ring: how far the set reaches.
        GUI.color = lit
            ? new Color(0.35f, 1f, 0.5f, dark ? 0.3f : 0.24f)
            : new Color(0.25f, 0.8f, 0.45f, dark ? 0.22f : 0.16f);
        DrawCircle(centre.Value, far);

        // 2. The trail: where the beam has just been. This is what makes it read as turning rather
        //    than as a line that happens to be there - motion is legible in a fading arc long before
        //    a lone line moving is.
        if (dark) DrawTrail(beam, centre.Value, far);

        // 3. The beam itself, to the far edge of the range.
        Vector2 tip = Beyond(beam, centre.Value, far);

        if (tip != Vector2.zero)
        {
            float width = lit ? 4f : 3f;

            GUI.color = dark
                ? (lit ? new Color(0.6f, 1f, 0.7f, 0.95f) : new Color(0.35f, 1f, 0.55f, 0.85f))
                : (lit ? new Color(0.9f, 1f, 0.95f, 1f) : new Color(0.55f, 0.9f, 1f, 0.75f));

            Line(from, tip, width);

            // A second, wider and fainter line under the first: the glow that lifts a thin beam off a
            // map instead of leaving a hairline nobody notices.
            GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, GUI.color.a * 0.18f);
            Line(from, tip, width * 3f);

            // The beam's width, as two faint spokes: a 6-degree beam visibly differs from a 30-degree
            // one, which a single line cannot show.
            float half = Mathf.Max(1f, ModSettings.LidarBeamDeg) / 2f;
            Vector2 left = Beyond(beam, centre.Value, far, -half);
            Vector2 right = Beyond(beam, centre.Value, far, half);

            GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, 0.3f);
            if (left != Vector2.zero) Line(from, left, 1f);
            if (right != Vector2.zero) Line(from, right, 1f);
        }

        // 4. The emitter: a soft dot with a ring, so the ship carrying the set is findable at once.
        GUI.color = dark ? new Color(0.5f, 1f, 0.65f, 0.95f) : new Color(0.65f, 0.95f, 1f, 0.9f);
        Glow(from, 22f);
        DrawRing(from, 14f);

        // 5. What the beam is holding, or just where it points.
        if (lit)
        {
            GUI.color = new Color(0.75f, 1f, 0.8f, 1f);
            if (tip != Vector2.zero) Glow(tip, 26f);
            GUI.Label(new Rect(from.x + 16f, from.y + 6f, 320f, 18f), "LIDAR: " + beam.Holding);
        }
        else
        {
            GUI.color = new Color(0.45f, 0.95f, 0.6f, 0.8f);
            GUI.Label(new Rect(from.x + 16f, from.y + 6f, 320f, 18f),
                      "LIDAR " + (int)beam.Bearing + " deg");
        }

        // 6. The numbers, so the screen agrees with the config file.
        GUI.color = new Color(0.45f, 0.9f, 0.6f, 0.6f);
        GUI.Label(new Rect(from.x + 16f, from.y + 24f, 320f, 18f),
                  (int)beam.RangeNm + " nm, " + (int)ModSettings.LidarSpinRpm + " rpm, beam "
                  + (int)ModSettings.LidarBeamDeg + " deg");

        GUI.color = previous;
    }

    /// <summary>A fading arc behind the head: dashes at the bearings it has just passed, dimmer the
    /// further back they are, and shorter too, so the trail thins out instead of ending in a hard
    /// edge.</summary>
    private void DrawTrail(Lidar.Beam beam, GeoPosition centre, double degrees)
    {
        const int dashes = 14;
        const double spanBack = 40.0;     // degrees of arc behind the head

        Color previous = GUI.color;

        for (int i = 1; i <= dashes; i++)
        {
            double bearing = beam.Bearing - spanBack * i / dashes;

            // Only the outer part of each dash, so the trail marks the rim rather than filling the
            // disc: the point is motion, not a painted sector.
            double inner = degrees * (0.35 + 0.6 * (1.0 - (double)i / dashes));

            Vector2 start = OnScreen(Beyond(centre, bearing, inner));
            Vector2 end = OnScreen(Beyond(centre, bearing, degrees));

            if (start == Vector2.zero || end == Vector2.zero) continue;

            float fade = 1f - (float)i / (dashes + 1);
            GUI.color = new Color(0.3f, 1f, 0.5f, 0.28f * fade * fade);
            Line(start, end, 2f);
        }

        GUI.color = previous;
    }

    private static void DrawCircle(GeoPosition centre, double degrees)
    {
        const int segments = 64;
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

    // --- calibration ---------------------------------------------------------------------------

    /// <summary>
    /// Draw the beam once with the projection's own numbers written down. This exists because the one
    /// thing that cannot be checked offline is where the game's map actually is on the screen: the
    /// arithmetic can be exact and still land in the wrong place. The block names the position it
    /// used, where it landed, and the difference from the game's own label for the same hull - so a
    /// report answers "is the beam in the right place" with arithmetic rather than a screenshot to
    /// squint at.
    /// </summary>
    private void Calibrate(ObjectBase anchor)
    {
        calibrated = true;

        try
        {
            List<string> lines = MapProjection.Calibrate(anchor);

            foreach (string line in lines) ModLog.Info("LIDAR calibration: " + line);

            Lidar.WriteCalibration(lines);
        }
        catch (Exception error)
        {
            ModLog.Error("LIDAR: the calibration reading failed", error);
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

    /// <summary>The beam's far end on screen: the set's own bearing, at the set's range. Zero when it
    /// cannot be projected, which the caller treats as "do not draw".</summary>
    private Vector2 Beyond(Lidar.Beam beam, GeoPosition centre, double degrees,
                           float extraDegrees = 0f)
    {
        return OnScreen(Beyond(centre, beam.Bearing + extraDegrees, degrees));
    }

    /// <summary>
    /// A position on screen, or zero when it cannot be projected.
    ///
    /// Named <c>OnScreen</c>, not <c>Screen</c>: a member called Screen hides UnityEngine's own
    /// Screen type inside this class, and every <c>Screen.width</c> in the file then fails to
    /// compile - CS0119, and thirteen errors from one unlucky name.
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

    /// <summary>A soft dot, for an emitter or a sighting.</summary>
    private void Glow(Vector2 at, float size)
    {
        if (beamTexture == null) return;
        GUI.DrawTexture(new Rect(at.x - size / 2f, at.y - size / 2f, size, size), beamTexture);
    }

    /// <summary>A straight line, drawn as a rotated rectangle: the same trick the ECM cones use.</summary>
    private static void Line(Vector2 from, Vector2 to, float width)
    {
        if (instance == null || instance.white == null) return;

        Vector2 delta = to - from;
        float length = delta.magnitude;
        if (length < 2f) return;

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        Matrix4x4 saved = GUI.matrix;
        try
        {
            GUIUtility.RotateAroundPivot(angle, from);
            GUI.DrawTexture(new Rect(from.x, from.y - width / 2f, length, width), instance.white);
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
        GUI.Label(new Rect(14f, Screen.height - 58f, 1400f, 20f),
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
        if (beamTexture != null) Destroy(beamTexture);
        instance = null;
    }
}
