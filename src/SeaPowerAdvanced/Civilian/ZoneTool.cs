using System;
using System.Collections.Generic;
using SeaPower;
using SeapowerUI.ViewModels;

namespace SeaPowerAdvanced.Civilian;

/// <summary>
/// The "draw a zone on the map" tool.
///
/// It does not reimplement drawing. The mission editor already owns a polygon tool
/// (<see cref="PolygonInteractionState"/>): left-click adds a point, middle-click leaves the mode,
/// points snap to markers and the polygon is drawn on the map's drawing layer as you go. Pushing
/// that state onto the editor's <see cref="InteractionStateManager"/> hands the mouse over to it
/// and gives the mod the finished polygon as geographic positions
/// (<see cref="PolygonDrawing.Locations"/>), which is exactly what the generator needs.
///
/// The mod only (a) starts it, (b) reads the points while it is active and (c) pops it again if
/// the user presses Finish before middle-clicking.
/// </summary>
internal static class ZoneTool
{
    private static PolygonInteractionState? state;
    private static InteractionStateManager? manager;
    private static MissionEditorMapViewModel? map;

    /// <summary>The last polygon the user finished, in click order.</summary>
    internal static List<GeoPosition> Polygon { get; } = new();

    internal static bool IsDrawing => state != null;

    internal static bool HasPolygon => Polygon.Count >= 3;

    /// <summary>Starts drawing. Returns an error message, or an empty string on success.</summary>
    internal static string Begin()
    {
        MissionEditorViewModel? editor = MissionEditorViewModel.Instance;
        if (editor == null) return "Open the mission editor first.";

        InteractionStateManager? states = editor.StateManager;
        MissionEditorMapViewModel? editorMap = editor.Map;

        if (states == null) return "The mission editor is not ready yet.";
        if (editorMap == null) return "The mission editor map is not ready yet.";

        try
        {
            if (state != null)
            {
                // Already drawing: do not stack a second state on the manager. If the game popped
                // the previous one (middle-click) it is stale, so start a fresh tool instead.
                if (IsActive(editorMap, state)) return string.Empty;
                Reset();
            }

            PolygonInteractionState created = new(states, editorMap.DrawingLayer);
            states.PushState(created);

            state = created;
            manager = states;
            map = editorMap;
            Polygon.Clear();

            ModLog.Info("Zone tool: polygon mode started. Left-click to add points, middle-click to finish.");
            return string.Empty;
        }
        catch (Exception error)
        {
            ModLog.Error("Zone tool could not start", error);
            Reset();
            return "Could not start the zone tool: " + error.Message;
        }
    }

    /// <summary>
    /// Reads whatever the user has clicked so far. Safe to call every frame; the game rebuilds
    /// <c>Locations</c> from the clicked points on each mouse action.
    /// </summary>
    internal static void Capture()
    {
        PolygonInteractionState? current = state;
        if (current == null) return;

        try
        {
            PolygonDrawing? drawing = current.Polygon;
            List<GeoPosition>? locations = drawing?.Locations;
            if (locations == null || locations.Count == 0) return;

            Polygon.Clear();
            foreach (GeoPosition position in locations) Polygon.Add(position);
        }
        catch (Exception error)
        {
            ModLog.Error("Zone tool could not read the polygon", error);
        }
    }

    /// <summary>Finishes drawing and keeps the polygon.</summary>
    internal static string Finish()
    {
        Capture();

        string result = HasPolygon
            ? Polygon.Count + " zone point(s) captured."
            : "No zone yet — click at least three points on the map.";

        Leave();
        return result;
    }

    /// <summary>Leaves the drawing mode without touching the captured polygon.</summary>
    internal static void Cancel()
    {
        Leave();
        ModLog.Info("Zone tool: cancelled.");
    }

    /// <summary>Forgets the captured polygon (used by the panel's Clear button).</summary>
    internal static void ClearPolygon()
    {
        Polygon.Clear();
    }

    private static void Leave()
    {
        PolygonInteractionState? current = state;
        InteractionStateManager? states = manager;
        MissionEditorMapViewModel? editorMap = map;

        Reset();

        if (current == null || states == null) return;

        try
        {
            // Middle-click pops the state inside the game; only pop when it is still the active one,
            // otherwise the pop would remove the editor's default state instead.
            if (IsActive(editorMap, current)) states.PopState();
        }
        catch (Exception error)
        {
            ModLog.Error("Zone tool could not leave polygon mode", error);
        }
    }

    /// <summary>
    /// True when the game's interaction manager is still running our state. Middle-click pops it,
    /// so this is what stops the panel popping a state that is no longer on the stack.
    /// </summary>
    private static bool IsActive(MissionEditorMapViewModel? editorMap, PolygonInteractionState? candidate)
    {
        if (editorMap == null || candidate == null) return true;

        try
        {
            return editorMap.InteractionState is { } property && ReferenceEquals(property.Value, candidate);
        }
        catch (Exception)
        {
            // If the property cannot be read, assume it is still active: the alternative is leaving
            // the user stuck in a dead drawing mode.
            return true;
        }
    }

    private static void Reset()
    {
        state = null;
        manager = null;
        map = null;
    }
}
