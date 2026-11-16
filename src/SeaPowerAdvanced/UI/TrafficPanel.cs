using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerAdvanced.Civilian;
using SeapowerUI.ViewModels;
using UnityEngine;

namespace SeaPowerAdvanced.UI;

/// <summary>
/// The civilian traffic panel, rebuilt.
///
/// What was wrong with the old one, and what this does instead:
///
/// <list type="number">
/// <item><b>Immediate-mode layout kept collapsing.</b> It was a GUILayout window whose height was
///   whatever the content happened to measure, inside a game that also draws its own UI. This uses
///   absolute rectangles: every control has a fixed position and size, so there is nothing to
///   collapse and nothing to reflow.</item>
/// <item><b>It was unreadable or enormous depending on the display.</b> Unity's IMGUI works in
///   pixels, so the same window is tiny at 4K and huge at 720p. The whole panel is now drawn through
///   <see cref="GUI.matrix"/> scaled from the actual screen height, so it is the same physical size
///   at every resolution.</item>
/// <item><b>Typing numbers into the game did not work.</b> Three text fields fought the game's own
///   input for keyboard focus. There are none left: every number has - and + buttons.</item>
/// <item><b>Nothing could be seen afterwards.</b> The result was a single line that the next click
///   overwrote, and everything else went to a file. The panel now keeps a visible log of the last
///   several things it did, in the window, at the top of the screen.</item>
/// <item><b>It could only work in the editor and said nothing when it could not.</b> Open it
///   anywhere and it now says so in one line instead of not appearing.</item>
/// </list>
///
/// It can also spawn traffic without a drawn zone at all: <b>Map area</b> builds one from the sea
/// the mission's own units are sitting in, so a broken draw tool is no longer a dead end.
/// </summary>
public sealed class TrafficPanel : MonoBehaviour
{
    private const int WindowId = 0x5EA7;
    private const float Width = 420f;
    private const float CollapsedHeight = 26f;
    private const float ExpandedHeight = 424f;

    private bool visible = ModSettings.TrafficShowPanel;
    private bool collapsed;
    private bool hotkeyBroken;

    private Rect window = new(60f, 120f, Width, ExpandedHeight);

    private int minShips = 2;
    private int maxShips = 6;
    private int minPlanes;
    private int maxPlanes = 2;

    /// <summary>The zone built by <b>Map area</b>, used when no polygon was drawn.</summary>
    private List<GeoPosition>? mapArea;

    private readonly List<string> lines = new();

    private GUIStyle? body;
    private GUIStyle? small;
    private GUIStyle? title;
    private GUIStyle? log;

    private bool announced;

    private void Awake()
    {
        try
        {
            minShips = ModSettings.TrafficMinShips;
            maxShips = ModSettings.TrafficMaxShips;
            minPlanes = ModSettings.TrafficMinPlanes;
            maxPlanes = ModSettings.TrafficMaxPlanes;
        }
        catch (Exception error)
        {
            ModLog.Error("Could not read the saved traffic numbers", error);
        }

        Say("Press F8 to close. Latest result appears here and in traffic-report.txt.");
        ModLog.Info("Civilian traffic panel ready (" + ModSettings.TrafficToggleKey
                    + " opens it; Map area makes a zone without drawing).");
    }

    private void Update()
    {
        PollHotkey();

        try
        {
            if (ZoneTool.IsDrawing) ZoneTool.Capture();
        }
        catch (Exception error)
        {
            ModLog.Error("Zone tool capture failed", error);
        }
    }

    private void PollHotkey()
    {
        if (hotkeyBroken) return;

        try
        {
            KeyCode key = ToggleKey();
            if (Input.GetKeyDown(key)) Toggle();
        }
        catch (Exception error)
        {
            hotkeyBroken = true;
            ModLog.Warn("Legacy Input unavailable (" + error.GetType().Name
                        + "); the panel key is read from the GUI event instead.");
        }
    }

    private void Toggle()
    {
        visible = !visible;
        if (visible) collapsed = false;
    }

    private void OnGUI()
    {
        try
        {
            // The route that survives a build running the new Input System only, where
            // Input.GetKeyDown throws.
            Event? current = Event.current;
            if (current != null && current.type == EventType.KeyDown && current.keyCode == ToggleKey())
            {
                Toggle();
                current.Use();
            }
        }
        catch (Exception) { }

        if (!visible) return;

        EnsureStyles();

        // One scale for the whole panel, from the height of the display: 1080p is the reference, and
        // the clamp stops a 4K panel from swallowing the screen or a 720p one from vanishing.
        float scale = Mathf.Clamp(Screen.height / 1080f, 0.8f, 2.2f);
        Matrix4x4 previous = GUI.matrix;

        try
        {
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            if (!InEditor(out string why))
            {
                DrawNotice(why);
                return;
            }

            if (!announced)
            {
                announced = true;
                ModLog.Info("Mission editor detected; the civilian traffic panel is available.");
            }

            DrawPanel();
        }
        catch (Exception error)
        {
            ModLog.Error("Traffic panel drawing failed", error);
        }
        finally
        {
            GUI.matrix = previous;
        }
    }

    private bool InEditor(out string why)
    {
        try
        {
            if (MissionEditorViewModel.Instance == null)
            {
                why = "Open a mission in the mission editor - traffic is written into the mission "
                      + "being edited, so this only works there.";
                return false;
            }
        }
        catch (Exception error)
        {
            why = "Reading the mission editor threw " + error.GetType().Name + ": " + error.Message;
            return false;
        }

        why = "";
        return true;
    }

    /// <summary>Outside the editor: one short window that says why, rather than nothing at all.</summary>
    private void DrawNotice(string why)
    {
        var box = new Rect(window.x, window.y, Width, 104f);

        GUI.Box(box, "", GUI.skin.box);
        GUI.Label(new Rect(box.x + 12f, box.y + 10f, box.width - 24f, 58f), why, body);

        if (GUI.Button(new Rect(box.x + 12f, box.y + 68f, 90f, 26f), "Close")) visible = false;
        if (GUI.Button(new Rect(box.x + 110f, box.y + 68f, 150f, 26f), "Check setup"))
        {
            TrafficDiagnostics.Write("panel opened outside the mission editor");
        }
    }

    private void DrawPanel()
    {
        float height = collapsed ? CollapsedHeight : ExpandedHeight;

        // GUIStyle.none: the window itself is only an id and a rectangle. The frame is drawn by
        // Draw(), so there is no second title bar on top of ours.
        window = GUI.Window(WindowId, new Rect(window.x, window.y, Width, height), Draw, "", GUIStyle.none);
    }

    private void Draw(int id)
    {
        GUI.Box(new Rect(0f, 0f, Width, collapsed ? CollapsedHeight : ExpandedHeight), "", GUI.skin.box);

        // --- title bar ---------------------------------------------------------------------------
        GUI.Label(new Rect(10f, 4f, 240f, 20f), "Civilian traffic", title);

        // Words rather than glyphs: the game's default GUI font is not guaranteed to carry arrows or
        // a multiplication sign, and a missing glyph in a title bar looks like a broken panel.
        if (GUI.Button(new Rect(Width - 96f, 3f, 48f, 20f), collapsed ? "Show" : "Hide"))
            collapsed = !collapsed;

        if (GUI.Button(new Rect(Width - 44f, 3f, 40f, 20f), "Close")) visible = false;

        if (collapsed)
        {
            // Collapsed: everything left of the two buttons is the handle.
            DragArea(Width - 100f);
            return;
        }

        float y = 30f;

        // --- zone ---------------------------------------------------------------------------------
        TrafficZone? zone = CurrentZone(out string zoneText);

        GUI.Label(new Rect(10f, y, Width - 20f, 18f), "Zone: " + zoneText, small);
        y += 21f;

        if (!ZoneTool.IsDrawing)
        {
            if (GUI.Button(new Rect(10f, y, 92f, 24f), "Draw zone"))
            {
                string error = ZoneTool.Begin();
                Say(error.Length == 0
                    ? "Click points on the map. Middle-click or Finish when done."
                    : error);
            }
        }
        else if (GUI.Button(new Rect(10f, y, 92f, 24f), "Finish"))
        {
            Say(ZoneTool.Finish());
        }

        if (GUI.Button(new Rect(108f, y, 92f, 24f), "Map area"))
        {
            mapArea = MapArea.AroundUnits(out string note);
            Say(mapArea.Count >= 3
                ? "Zone from the map: " + note + "."
                : "No zone built: " + note + ".");
            if (mapArea.Count < 3) mapArea = null;
        }

        if (GUI.Button(new Rect(206f, y, 92f, 24f), "Clear zone"))
        {
            ZoneTool.ClearPolygon();
            mapArea = null;
            Say("Zone cleared.");
        }

        if (GUI.Button(new Rect(304f, y, 106f, 24f), "Check setup"))
        {
            TrafficDiagnostics.Write(lines.Count > 0 ? lines[lines.Count - 1] : null);
            Say("Wrote traffic-report.txt into the mod folder.");
        }

        y += 30f;

        // --- counts -------------------------------------------------------------------------------
        GUI.Label(new Rect(10f, y, 200f, 18f), "Ship lanes (units alive at once)", small);
        y += 20f;

        minShips = Stepper(10f, y, "min", minShips, 0, 200);
        maxShips = Stepper(216f, y, "max", maxShips, 0, 200);
        y += 28f;

        GUI.Label(new Rect(10f, y, 200f, 18f), "Aircraft lanes (units alive at once)", small);
        y += 20f;

        minPlanes = Stepper(10f, y, "min", minPlanes, 0, 200);
        maxPlanes = Stepper(216f, y, "max", maxPlanes, 0, 200);
        y += 30f;

        // --- actions ------------------------------------------------------------------------------
        if (GUI.Button(new Rect(10f, y, 296f, 30f), "Spawn traffic in this zone"))
            SpawnTraffic(zone);

        if (GUI.Button(new Rect(312f, y, 98f, 30f), "Remove"))
        {
            int removed = TrafficGenerator.RemoveGenerated();
            Say(removed == 0
                ? "Nothing to remove - no routes were generated in this session."
                : "Removed " + removed + " generated route(s).");
        }

        y += 36f;

        GUI.Label(new Rect(10f, y, Width - 130f, 18f), "Classes: " + CivilianCatalogue.Describe(), small);

        if (GUI.Button(new Rect(Width - 112f, y - 3f, 102f, 22f), "Reload classes"))
        {
            CivilianCatalogue.Refresh(true);
            Say("Classes: " + CivilianCatalogue.Describe() + ".");
        }

        y += 22f;

        // --- the log ------------------------------------------------------------------------------
        GUI.Box(new Rect(10f, y, Width - 20f, ExpandedHeight - y - 12f), "", GUI.skin.box);

        float lineY = y + 6f;
        foreach (string line in lines)
        {
            GUI.Label(new Rect(16f, lineY, Width - 32f, 16f), line, log);
            lineY += 16f;
        }

        DragArea(Width - 100f);
    }

    /// <summary>
    /// The title bar is the handle. Its width is passed in so the buttons in it stay pressable: a
    /// DragWindow rect that covers a button makes the button move the window instead.
    /// </summary>
    private static void DragArea(float width) => GUI.DragWindow(new Rect(0f, 0f, width, CollapsedHeight));

    /// <summary>A labelled number with - and + . Returns the new value.</summary>
    private int Stepper(float x, float y, string caption, int value, int minimum, int maximum)
    {
        GUI.Label(new Rect(x, y + 3f, 30f, 20f), caption, small);

        if (GUI.Button(new Rect(x + 32f, y, 24f, 24f), "-"))
            return Math.Max(minimum, value - Step(value));

        GUI.Label(new Rect(x + 60f, y + 3f, 40f, 20f), value.ToString(), body);

        if (GUI.Button(new Rect(x + 100f, y, 24f, 24f), "+"))
            return Math.Min(maximum, value + Step(value));

        return value;
    }

    /// <summary>1 for small counts, 2 above ten: a lane count of forty is not worth forty clicks.</summary>
    private static int Step(int value) => value >= 10 ? 2 : 1;

    private TrafficZone? CurrentZone(out string text)
    {
        if (ZoneTool.HasPolygon)
        {
            TrafficZone? drawn = TrafficZone.Create(ZoneTool.Polygon);
            if (drawn != null)
            {
                text = ZoneTool.Polygon.Count + " point(s) drawn, ~"
                       + Math.Round(drawn.AreaSquareNauticalMiles, 0) + " nm²";
                return drawn;
            }

            text = ZoneTool.Polygon.Count + " point(s) - too small to use";
            return null;
        }

        if (mapArea != null)
        {
            TrafficZone? area = TrafficZone.Create(mapArea);
            if (area != null)
            {
                text = "map area, ~" + Math.Round(area.AreaSquareNauticalMiles, 0) + " nm²";
                return area;
            }
        }

        text = "none - draw one, or press Map area";
        return null;
    }

    private void SpawnTraffic(TrafficZone? zone)
    {
        if (zone == null)
        {
            Say("No zone yet: press Draw zone and click three points, or press Map area.");
            return;
        }

        TrafficRequest request = new()
        {
            MinShips = Math.Min(minShips, maxShips),
            MaxShips = Math.Max(minShips, maxShips),
            MinPlanes = Math.Min(minPlanes, maxPlanes),
            MaxPlanes = Math.Max(minPlanes, maxPlanes),
            MaxLanes = ModSettings.TrafficMaxLanes,
            ClassesPerRoute = ModSettings.TrafficClassesPerRoute,
            Label = "Zone traffic"
        };

        try
        {
            ModSettings.TrafficMinShips = request.MinShips;
            ModSettings.TrafficMaxShips = request.MaxShips;
            ModSettings.TrafficMinPlanes = request.MinPlanes;
            ModSettings.TrafficMaxPlanes = request.MaxPlanes;
        }
        catch (Exception error)
        {
            ModLog.Error("Could not save the traffic numbers", error);
        }

        string report;

        try
        {
            TrafficGenerator.Generate(zone, request, out report);
        }
        catch (Exception error)
        {
            ModLog.Error("Civilian traffic generation failed", error);
            report = "Generation failed: " + error.GetType().Name + ": " + error.Message;
        }

        TrafficDiagnostics.Note(report);
        Say(report);
    }

    /// <summary>Adds one line to the on-screen log and keeps the newest first.</summary>
    private void Say(string message)
    {
        try
        {
            string stamp = "[" + DateTime.Now.ToString("HH:mm:ss") + "] ";

            lines.Insert(0, stamp + Shorten(message));

            while (lines.Count > 6) lines.RemoveAt(lines.Count - 1);
        }
        catch (Exception) { }
    }

    /// <summary>
    /// One line for the panel. The full sentence always goes to the log file, so nothing is lost by
    /// trimming it here - and an untrimmed one would run off the side of the window, which is how the
    /// old panel managed to look broken while working.
    /// </summary>
    private static string Shorten(string message)
    {
        string single = message.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return single.Length <= 62 ? single : single.Substring(0, 59) + "...";
    }

    private void EnsureStyles()
    {
        if (body != null) return;

        body = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
        small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        title = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        log = new GUIStyle(GUI.skin.label) { fontSize = 11 };
    }

    private static KeyCode ToggleKey()
    {
        try
        {
            string text = ModSettings.TrafficToggleKey;
            if (!string.IsNullOrWhiteSpace(text) && Enum.TryParse(text.Trim(), true, out KeyCode key))
                return key;
        }
        catch (Exception) { }

        return KeyCode.F8;
    }

    internal static void Install()
    {
        try
        {
            if (FindObjectOfType<TrafficPanel>() != null) return;

            GameObject host = new("SeaPowerAdvanced.TrafficPanel");
            DontDestroyOnLoad(host);
            host.AddComponent<TrafficPanel>();
        }
        catch (Exception error)
        {
            ModLog.Error("Could not create the civilian traffic panel", error);
        }
    }
}
