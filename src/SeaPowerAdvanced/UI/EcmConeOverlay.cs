using System;
using System.Collections.Generic;
using System.Text;
using SeaPower;
using SeaPowerAdvanced.Features;
using SeaPowerAdvanced.Interop;
using UnityEngine;

namespace SeaPowerAdvanced.UI;

/// <summary>
/// Draws the ECM cones on the map.
///
/// A jammer is directional, and a menu cannot show you which way it is pointing. So the overlay puts
/// a beam on the map for every set that is aimed at something:
///
/// <list type="bullet">
/// <item><b>offensive, locked</b> - a long, narrow cyan beam from the jammer to the ship it is
///   locked on, thin while it is only locked, bright once it is transmitting;</item>
/// <item><b>defensive</b> - an amber beam to the missile the set is tracking, with a marker on the
///   missile itself;</item>
/// <item><b>selected, no target</b> - a small ring where the set is, so a ship carrying a jammer is
///   visible before it is used.</item>
/// </list>
///
/// Everything is drawn in IMGUI, which needs no assets and no XAML. The two ends of each beam come
/// from <see cref="MapProjection"/>, which finds
/// the game's own position conversion or the unit's transform. If neither exists the overlay draws
/// nothing, says so once in the log and in <c>ecm-cone.txt</c>, and never guesses a screen position.
/// </summary>
public sealed class EcmConeOverlay : MonoBehaviour
{
    private static EcmConeOverlay? instance;

    private Texture2D? white;
    private bool reported;

    // The beam list and the selected unit are rebuilt a few times a second, not every GUI event:
    // collecting them means walking the fleet, and OnGUI runs at least twice a frame.
    private List<EcmBeam> beams = new();
    private ObjectBase? selectedUnit;
    private float nextRefresh;
    private bool disabled;
    private float nextDiagLogged;

    /// <summary>Installed once at startup; does nothing at all until a mission is running.</summary>
    internal static void Install()
    {
        if (instance != null) return;

        try
        {
            var host = new GameObject("SeaPowerAdvanced.EcmConeOverlay");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<EcmConeOverlay>();
        }
        catch (Exception error)
        {
            ModLog.Error("Could not start the ECM cone overlay", error);
        }
    }

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
            ModLog.Error("Could not create the overlay texture", error);
            disabled = true;
        }
    }

    private void OnGUI()
    {
        if (disabled || white == null) return;
        if (!ModSettings.FeaturesEnabled || !ModSettings.ShowEcmCone) return;

        // Draw once per frame, not once per event.
        if (Event.current == null || Event.current.type != EventType.Repaint) return;

        try
        {
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.2f;
                beams = Collect();
                selectedUnit = Selection.Current();
            }

            ObjectBase? selected = selectedUnit;

            if (beams.Count == 0 && selected == null) return;

            if (!MapProjection.Available)
            {
                DrawNote();
                return;
            }

            foreach (EcmBeam beam in beams) DrawBeam(beam);
            if (selected != null) DrawSelected(selected, beams);

            if (!reported)
            {
                reported = true;
                ModLog.Info("ECM cone: drawing " + beams.Count + " beam(s). " + MapProjection.Report);
            }

            if (ModSettings.VerboseLog && Time.unscaledTime > nextDiagLogged)
            {
                nextDiagLogged = Time.unscaledTime + 30f;
                ModLog.Info("ECM cone: " + beams.Count + " beam(s), projection: "
                            + MapProjection.Report);
            }
        }
        catch (Exception error)
        {
            disabled = true;
            ModLog.Error("The ECM cone overlay stopped after an error", error);
        }
    }

    /// <summary>Every set that is aimed at something, offensive and defensive together.</summary>
    private static List<EcmBeam> Collect()
    {
        var beams = new List<EcmBeam>();

        try { beams.AddRange(DefensiveEcm.Beams()); }
        catch (Exception error) { ModLog.Error("Could not read the defensive ECM beams", error); }

        try { beams.AddRange(OffensiveEcm.Beams()); }
        catch (Exception error) { ModLog.Error("Could not read the offensive ECM beams", error); }

        return beams;
    }

    // --- drawing -----------------------------------------------------------------------------

    private void DrawBeam(EcmBeam beam)
    {
        if (!MapProjection.TryScreen(beam.Jammer, out Vector2 from)) return;
        if (!MapProjection.TryScreen(beam.Target, out Vector2 to)) return;

        Vector2 delta = to - from;
        float length = delta.magnitude;
        if (length < 8f) return;   // effectively on top of each other: nothing to show

        Color colour = beam.Offensive
            ? (beam.Transmitting ? new Color(0.25f, 0.95f, 1f, 0.85f) : new Color(0.25f, 0.95f, 1f, 0.35f))
            : new Color(1f, 0.72f, 0.2f, 0.8f);

        // Long and narrow: the beam is a bearing, not an area. Two pixels locked, three transmitting,
        // so the difference between "aimed" and "radiating" is visible at a glance.
        float width = beam.Transmitting ? 3f : 1.5f;

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        Color previous = GUI.color;
        GUI.color = colour;

        Matrix4x4 saved = GUI.matrix;
        try
        {
            GUIUtility.RotateAroundPivot(angle, from);
            GUI.DrawTexture(new Rect(from.x, from.y - width / 2f, length, width), white);
        }
        finally
        {
            GUI.matrix = saved;
            GUI.color = previous;
        }

        // The far end: a small box on the missile or ship the beam is holding.
        float box = beam.Transmitting ? 6f : 4f;
        GUI.color = colour;
        GUI.DrawTexture(new Rect(to.x - box / 2f, to.y - box / 2f, box, box), white);
        GUI.color = previous;

        // A short label, only when the set is actually transmitting - otherwise the map fills up
        // with text nobody asked for.
        if (beam.Transmitting)
        {
            GUI.color = colour;
            GUI.Label(new Rect(to.x + 8f, to.y - 8f, 220f, 18f),
                      beam.Offensive ? "ECM JAMMING" : "ECM vs missile");
            GUI.color = previous;
        }
    }

    /// <summary>A ring where a selected ship with a jammer is, so the set is findable before use.</summary>
    private void DrawSelected(ObjectBase selected, List<EcmBeam> beams)
    {
        foreach (EcmBeam beam in beams)
        {
            if (ReferenceEquals(beam.Jammer, selected)) return;   // already drawn as a beam
        }

        if (!UnitFit.HasEcm(selected)) return;
        if (!MapProjection.TryScreen(selected, out Vector2 at)) return;

        Color previous = GUI.color;
        GUI.color = new Color(0.25f, 0.95f, 1f, 0.55f);

        const float size = 14f;
        DrawRing(at, size);

        GUI.Label(new Rect(at.x + size, at.y - 9f, 240f, 18f),
                  OffensiveEcm.ModeOf(selected) == OffensiveEcm.Mode.SignatureLock
                      ? "ECM: " + OffensiveEcm.Describe(selected)
                      : "ECM ready");

        GUI.color = previous;
    }

    /// <summary>A square outline, drawn as four thin rectangles: no textures, no rotation maths.</summary>
    private void DrawRing(Vector2 at, float size)
    {
        float half = size / 2f;
        const float thickness = 1.5f;

        GUI.DrawTexture(new Rect(at.x - half, at.y - half, size, thickness), white);
        GUI.DrawTexture(new Rect(at.x - half, at.y + half - thickness, size, thickness), white);
        GUI.DrawTexture(new Rect(at.x - half, at.y - half, thickness, size), white);
        GUI.DrawTexture(new Rect(at.x + half - thickness, at.y - half, thickness, size), white);
    }

    /// <summary>
    /// What the player sees when the cone cannot be drawn: one line, in the corner, saying so. Better
    /// than a silent absence, which reads as "the feature is broken" when the truth is "the game does
    /// not expose a position conversion".
    /// </summary>
    private void DrawNote()
    {
        Color previous = GUI.color;
        GUI.color = new Color(1f, 0.85f, 0.4f, 0.9f);
        GUI.Label(new Rect(12f, Screen.height - 34f, 1100f, 20f),
                  "Sea Power Advanced: ECM cones cannot be drawn - see ecm-cone.txt in the mod "
                  + "folder, beside seapoweradvanced.log (" + MapProjection.Report + ")");
        GUI.color = previous;

        if (!reported)
        {
            reported = true;
            ModLog.Warn("ECM cone: cannot draw - " + MapProjection.Report);
            WriteReport();
        }
    }

    /// <summary>Writes <c>ecm-cone.txt</c>: how the mod projects positions, and what it found.</summary>
    internal static void WriteReport()
    {
        try
        {
            var text = new StringBuilder();
            text.AppendLine("Sea Power Advanced - ECM cone diagnostics");
            text.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            + "   mod " + ModInfo.Version);
            text.AppendLine();
            text.AppendLine("Projection");
            text.AppendLine("----------");
            text.AppendLine("  " + MapProjection.Report);
            text.AppendLine();
            foreach (string line in MapProjection.Diagnose()) text.AppendLine("  " + line);
            text.AppendLine();
            text.AppendLine("The overlay draws a long, narrow beam from a jammer to the missile or ship");
            text.AppendLine("it is holding. It needs to turn a game position into a screen position, in");
            text.AppendLine("one of two ways: a method in the game that converts a GeoPosition to its");
            text.AppendLine("world space, or a Unity transform on the unit. If neither is available the");
            text.AppendLine("overlay draws nothing rather than putting a cone in the wrong place.");
            text.AppendLine();
            text.AppendLine("How the mod gets there");
            text.AppendLine("----------------------");
            text.AppendLine("  1. the map's own arithmetic - 0.2 degree tiles, 330.71396 world units");
            text.AppendLine("     each, offset by Globals._currentCenterTile. Exact, and the only route");
            text.AppendLine("     that works on this build, because its units are ECS data and have no");
            text.AppendLine("     Unity transform;");
            text.AppendLine("  2. any method in the game that converts a GeoPosition to a Vector3;");
            text.AppendLine("  3. a Unity transform on the unit, if one ever exists.");
            text.AppendLine();
            text.AppendLine("Then a camera projects world to screen. If the beams are in the wrong place,");
            text.AppendLine("the camera line above is the one to look at first - the grid line tells you");
            text.AppendLine("whether the arithmetic ran at all, and the numbers beside the beams say where");
            text.AppendLine("it thinks they are.");

            string path = ModFolders.WriteText("ecm-cone.txt", text.ToString());
            ModLog.Info("ECM cone report written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("Could not write ecm-cone.txt", error);
        }
    }

    private void OnDestroy()
    {
        try { if (white != null) Destroy(white); }
        catch (Exception) { }
    }
}
