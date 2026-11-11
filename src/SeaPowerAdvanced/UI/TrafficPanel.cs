using System;
using SeaPowerAdvanced.Civilian;
using SeapowerUI.ViewModels;
using UnityEngine;

namespace SeaPowerAdvanced.UI;

/// <summary>
/// The civilian traffic panel: draw a zone, type how many ships and aircraft you want in it.
///
/// Drawn with Unity's immediate-mode GUI rather than the game's Noesis views. That is deliberate:
/// a Noesis panel would mean shipping XAML and binding to the editor's data context, which breaks
/// on every UI refactor, while four number fields and three buttons do not need the game's styling.
/// The panel only appears while a mission editor view model exists, so it cannot end up over the
/// main menu or during a mission.
/// </summary>
public sealed class TrafficPanel : MonoBehaviour
{
    private const int WindowId = 0x5EA7;

    private bool visible = ModSettings.TrafficShowPanel;
    private KeyCode toggleKey = KeyCode.F8;
    private bool hotkeyBroken;

    private Rect window = new(70f, 150f, 360f, 0f);

    private string minShips = "2";
    private string maxShips = "6";
    private string minPlanes = "0";
    private string maxPlanes = "2";
    private string label = "Zone traffic";

    private string status = "Ready.";
    private float statusTime;

    private GUIStyle? labelStyle;
    private GUIStyle? statusStyle;

    private bool editorSeen;
    private bool catalogueChecked;

    private void Awake()
    {
        try
        {
            toggleKey = ParseKey(ModSettings.TrafficToggleKey, KeyCode.F8);
        }
        catch (Exception error)
        {
            ModLog.Error("Could not read the traffic panel key binding", error);
        }

        minShips = ModSettings.TrafficMinShips.ToString();
        maxShips = ModSettings.TrafficMaxShips.ToString();
        minPlanes = ModSettings.TrafficMinPlanes.ToString();
        maxPlanes = ModSettings.TrafficMaxPlanes;

        ModLog.Info("Civilian traffic panel ready (" + toggleKey + " toggles it inside the mission editor).");
    }

    private void Update()
    {
        PollHotkey();

        if (ZoneTool.IsDrawing)
        {
            ZoneTool.Capture();
        }
    }

    private void PollHotkey()
    {
        if (hotkeyBroken) return;

        try
        {
            // The game may be configured for the new Input System only, where the legacy Input
            // class throws. One failure is enough to stop asking.
            if (Input.GetKeyDown(toggleKey)) visible = !visible;
        }
        catch (Exception error)
        {
            hotkeyBroken = true;
            ModLog.Warn("Hotkey unavailable (" + error.GetType().Name + "); use the config file to set "
                        + "Traffic/ShowPanel. The panel itself still works.");
        }
    }

    private void OnGUI()
    {
        if (!visible) return;

        try
        {
            if (MissionEditorViewModel.Instance == null) return;

            if (!editorSeen)
            {
                editorSeen = true;
                ModLog.Info("Mission editor detected; the civilian traffic panel is available.");
            }

            if (!catalogueChecked)
            {
                catalogueChecked = true;
                CivilianCatalogue.Refresh();
            }
        }
        catch (Exception)
        {
            return;
        }

        EnsureStyles();

        window = GUILayout.Window(WindowId, window, DrawWindow, "Civilian traffic");
    }

    private void DrawWindow(int id)
    {
        try
        {
            GUILayout.Label("Draw a zone on the map, then choose how much traffic it gets.", labelStyle);

            DrawZoneRow();
            GUILayout.Space(4f);

            GUILayout.Label("Ships (vessels, alive at once)", labelStyle);
            minShips = NumberRow("min", minShips, "max", ref maxShips);

            GUILayout.Label("Planes (aircraft, alive at once)", labelStyle);
            minPlanes = NumberRow("min", minPlanes, "max", ref maxPlanes);

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", GUILayout.Width(46f));
            label = GUILayout.TextField(label ?? string.Empty);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Spawn traffic", GUILayout.Height(26f)))
            {
                SpawnTraffic();
            }

            if (GUILayout.Button("Remove generated", GUILayout.Height(26f)))
            {
                int removed = TrafficGenerator.RemoveGenerated();
                SetStatus(removed == 0
                    ? "Nothing to remove — no routes were generated in this session."
                    : "Removed " + removed + " generated route(s).");
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();

            GUILayout.Label("Classes: " + CivilianCatalogue.Describe(), labelStyle);

            if (GUILayout.Button("Refresh", GUILayout.Width(70f)))
            {
                CivilianCatalogue.Refresh(true);
                SetStatus("Catalogue: " + CivilianCatalogue.Describe() + ".");
            }

            GUILayout.EndHorizontal();

            GUILayout.Box(status ?? string.Empty, statusStyle);
        }
        catch (Exception error)
        {
            ModLog.Error("Traffic panel drawing failed", error);
            SetStatus("Panel error: " + error.Message);
        }

        GUI.DragWindow();
    }

    private void DrawZoneRow()
    {
        GUILayout.BeginHorizontal();

        string zone = ZoneTool.Polygon.Count == 0
            ? "No zone"
            : ZoneTool.Polygon.Count + " points";

        if (ZoneTool.HasPolygon)
        {
            TrafficZone? area = TrafficZone.Create(ZoneTool.Polygon);
            if (area != null) zone += "  ~" + Math.Round(area.AreaSquareNauticalMiles, 0) + " nm²";
        }

        GUILayout.Label("Zone: " + zone, labelStyle);

        if (!ZoneTool.IsDrawing)
        {
            if (GUILayout.Button("Draw zone", GUILayout.Width(90f)))
            {
                string error = ZoneTool.Begin();
                SetStatus(error.Length == 0
                    ? "Click points on the map. Middle-click or Finish when done."
                    : error);
            }
        }
        else if (GUILayout.Button("Finish", GUILayout.Width(90f)))
        {
            SetStatus(ZoneTool.Finish());
        }

        GUILayout.EndHorizontal();
    }

    private string NumberRow(string firstCaption, string firstValue, string secondCaption, ref string secondValue)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(firstCaption, GUILayout.Width(34f));
        firstValue = GUILayout.TextField(firstValue ?? "0", GUILayout.Width(46f));
        GUILayout.Label(secondCaption, GUILayout.Width(34f));
        secondValue = GUILayout.TextField(secondValue ?? "0", GUILayout.Width(46f));
        GUILayout.Label("units", labelStyle);
        GUILayout.EndHorizontal();
        return firstValue;
    }

    private void SpawnTraffic()
    {
        if (!ZoneTool.HasPolygon)
        {
            SetStatus("Draw a zone first: press Draw zone, click three or more points, then Finish.");
            return;
        }

        ZoneTool.Capture();

        TrafficZone? zone = TrafficZone.Create(ZoneTool.Polygon);
        if (zone == null)
        {
            SetStatus("That zone is too small to place traffic in. Draw a bigger area.");
            return;
        }

        TrafficRequest request = new()
        {
            MinShips = Parse(minShips, ModSettings.TrafficMinShips),
            MaxShips = Parse(maxShips, ModSettings.TrafficMaxShips),
            MinPlanes = Parse(minPlanes, ModSettings.TrafficMinPlanes),
            MaxPlanes = Parse(maxPlanes, ModSettings.TrafficMaxPlanes),
            MaxLanes = ModSettings.TrafficMaxLanes,
            ClassesPerRoute = ModSettings.TrafficClassesPerRoute,
            Label = string.IsNullOrWhiteSpace(label) ? "Zone traffic" : label.Trim()
        };

        RememberNumbers(request);

        string report;
        try
        {
            TrafficGenerator.Generate(zone, request, out report);
        }
        catch (Exception error)
        {
            ModLog.Error("Civilian traffic generation failed", error);
            report = "Generation failed: " + error.Message;
        }

        SetStatus(report);
    }

    private void RememberNumbers(TrafficRequest request)
    {
        try
        {
            ModSettings.TrafficMinShips = request.MinShips;
            ModSettings.TrafficMaxShips = request.MaxShips;
            ModSettings.TrafficMinPlanes = request.MinPlanes;
            ModSettings.TrafficMaxPlanes = request.MaxPlanes;
        }
        catch (Exception error)
        {
            ModLog.Error("Could not save the traffic numbers to the config", error);
        }
    }

    private void SetStatus(string message)
    {
        status = message;
        statusTime = Time.unscaledTime;
    }

    private void EnsureStyles()
    {
        if (labelStyle != null) return;

        labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
        statusStyle = new GUIStyle(GUI.skin.box)
        {
            wordWrap = true,
            alignment = TextAnchor.UpperLeft
        };
    }

    private static int Parse(string? text, int fallback)
        => int.TryParse((text ?? string.Empty).Trim(), out int value) ? Math.Max(0, Math.Min(200, value)) : fallback;

    private static KeyCode ParseKey(string? text, KeyCode fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        try
        {
            return Enum.TryParse(text.Trim(), true, out KeyCode parsed) ? parsed : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    /// <summary>Creates the panel once, owned by a scene object that survives scene loads.</summary>
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
