using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SeaPower;
using SeapowerUI.ViewModels;

namespace SeaPowerAdvanced.Civilian;

/// <summary>
/// Writes <c>traffic-report.txt</c>: every prerequisite the civilian traffic feature needs, whether
/// it is there, and what happened on the last attempt.
///
/// This exists because the feature never worked in game and the report could only ever say "it does
/// not work". A checklist with a verdict per line turns that into one file that says which link is
/// missing - the editor, the interaction state manager, the drawing layer, the unit database, the
/// neutral taskforce, or the route list.
///
/// Written on every spawn attempt, and on demand from the panel's <b>Check setup</b> button.
/// </summary>
internal static class TrafficDiagnostics
{
    private static string lastAttempt = "nothing has been spawned yet this session";

    /// <summary>Records what the last spawn produced, so the report carries the outcome too.</summary>
    internal static void Note(string outcome) => lastAttempt = outcome;

    internal static void Write(string? outcome = null)
    {
        if (outcome != null) lastAttempt = outcome;

        try
        {
            var text = new StringBuilder();
            text.AppendLine("Sea Power Advanced - civilian traffic report");
            text.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            + "   mod " + ModInfo.Version);
            text.AppendLine();
            text.AppendLine("ok   = present and usable");
            text.AppendLine("--   = missing; the line says which");
            text.AppendLine();
            text.AppendLine("What the feature needs, in order");
            text.AppendLine("--------------------------------");

            text.AppendLine(Line("mission editor open", EditorState()));
            text.AppendLine(Line("editor state manager", ManagerState()));
            text.AppendLine(Line("editor drawing layer", DrawingLayerState()));
            text.AppendLine(Line("unit database", CatalogueState()));
            text.AppendLine(Line("civilian classes", ClassesState()));
            text.AppendLine(Line("neutral taskforce", TaskforceState()));
            text.AppendLine(Line("civilian route list", RoutesState()));
            text.AppendLine();

            text.AppendLine("The zone");
            text.AppendLine("--------");
            text.AppendLine("  points captured: " + ZoneTool.Polygon.Count
                            + (ZoneTool.HasPolygon ? " (enough)" : " (needs 3 or more)"));
            text.AppendLine("  drawing mode:    " + (ZoneTool.IsDrawing ? "active" : "not active"));
            text.AppendLine();

            text.AppendLine("Last attempt");
            text.AppendLine("------------");
            text.AppendLine("  " + lastAttempt);
            text.AppendLine();
            text.AppendLine("How to read this");
            text.AppendLine("----------------");
            text.AppendLine("  If 'mission editor open' is -- the panel cannot do anything: open a mission in");
            text.AppendLine("  the editor first. If 'civilian route list' is -- the mod could not find where");
            text.AppendLine("  this build keeps its routes; that line is the one to send. If every line is ok");
            text.AppendLine("  and the spawn still adds nothing, 'Last attempt' carries the game's own reason.");

            string path = ModFolders.WriteText("traffic-report.txt", text.ToString());
            ModLog.Info("Civilian traffic report written to " + path);
        }
        catch (Exception error)
        {
            ModLog.Error("Could not write traffic-report.txt", error);
        }
    }

    // --- the individual checks --------------------------------------------------------------------

    private static string EditorState()
    {
        try
        {
            object? editor = MissionEditorViewModel.Instance;
            return editor == null ? "no mission editor view model" : "open";
        }
        catch (Exception error)
        {
            return "MissionEditorViewModel.Instance threw " + error.GetType().Name;
        }
    }

    private static string ManagerState()
    {
        try
        {
            MissionEditorViewModel? editor = MissionEditorViewModel.Instance;
            if (editor == null) return "no editor to ask";
            return editor.StateManager == null ? "the editor reports no InteractionStateManager" : "present";
        }
        catch (Exception error)
        {
            return "StateManager threw " + error.GetType().Name;
        }
    }

    private static string DrawingLayerState()
    {
        try
        {
            MissionEditorViewModel? editor = MissionEditorViewModel.Instance;
            if (editor?.Map == null) return "the editor reports no map view model";
            return editor.Map.DrawingLayer == null ? "the map reports no drawing layer" : "present";
        }
        catch (Exception error)
        {
            return "Map.DrawingLayer threw " + error.GetType().Name;
        }
    }

    private static string CatalogueState()
    {
        try
        {
            CivilianCatalogue.Refresh();
            return CivilianCatalogue.Ready ? "read" : "the unit database is not available yet";
        }
        catch (Exception error)
        {
            return "the catalogue threw " + error.GetType().Name;
        }
    }

    private static string ClassesState() =>
        CivilianCatalogue.Ships.Count == 0 && CivilianCatalogue.Planes.Count == 0
            ? "none found - nothing in this install is flagged civilian"
            : CivilianCatalogue.Describe();

    private static string TaskforceState()
    {
        try
        {
            bool found = RouteFactory.ResolveTaskforce(out string source) != null;

            // A null taskforce does not stop a route being created any more - it is reported, and
            // the route is written anyway - but it is still the reason traffic would not appear, so
            // it keeps its own line.
            return found ? source : "none found - traffic may not spawn (routes are still created)";
        }
        catch (Exception error) { return "threw " + error.GetType().Name; }
    }

    private static string RoutesState()
    {
        try
        {
            IList? list = RouteContainers.Find(out string description);
            if (list == null) return description;

            // The type matters: the old generator demanded List<CivilianRoute> exactly, which a
            // mission holding an observable collection is not. Naming it here is what makes that
            // visible instead of looking like an empty list.
            string kind = RouteContainers.Describe(list);

            return RouteContainers.CanAdd(list, out string why)
                ? description + " [" + kind + "]"
                : description + " [" + kind + "] - " + why;
        }
        catch (Exception error)
        {
            return "threw " + error.GetType().Name;
        }
    }

    /// <summary>
    /// One checklist line. The verdict is decided by an explicit list of "bad" prefixes rather than
    /// by checking for the word "no", because a detail like "no mission editor view model" and one
    /// like "none found" must both read as missing while "open" must not be caught by anything.
    /// </summary>
    private static string Line(string label, string detail)
    {
        bool missing =
            detail.StartsWith("no ", StringComparison.OrdinalIgnoreCase)
            || detail.StartsWith("none", StringComparison.OrdinalIgnoreCase)
            || detail.StartsWith("threw", StringComparison.OrdinalIgnoreCase)
            || detail.StartsWith("the ", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("not available")
            || detail.Contains("reports no")
            || detail.Contains("could not");

        return "  " + (missing ? "--" : "ok") + "   " + label.PadRight(22) + detail;
    }
}
