using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;
using SeapowerUI.ViewModels;

namespace SeaPowerAdvanced.Civilian;

/// <summary>
/// Where this build of the game keeps its civilian routes.
///
/// The generator used to assume one answer - <c>MissionManager._civilianRoutes</c> - and give up with
/// "no civilian route list" if it was null. In the mission editor that is very likely null: the
/// manager is what runs a mission, and the editor is holding the mission being edited. So the
/// container is looked for instead of assumed:
///
/// <list type="number">
/// <item>the mission manager's own civilian route list, if there is one;</item>
/// <item>a civilian route list anywhere on the mission editor's view model, or on the mission it is
///   editing - one level deep, which covers both shapes;</item>
/// <item>nothing, reported by name in <c>traffic-report.txt</c> so the next build can be exact.</item>
/// </list>
///
/// Every candidate it looks at is written to the report, found or not. That is the difference between
/// "the feature does not work" and "the feature was looking in the wrong place".
/// </summary>
internal static class RouteContainers
{
    /// <summary>The list the generator adds to, and what it was called, for the log.</summary>
    internal static IList? Find(out string description)
    {
        description = "";

        // 1. The mission manager's own list.
        try
        {
            MissionManager? manager = Singleton<MissionManager>.Instance;
            if (manager != null)
            {
                IList? direct = manager._civilianRoutes;

                if (direct != null)
                {
                    description = "MissionManager._civilianRoutes (" + direct.Count + " route(s))";
                    return direct;
                }

                IList? found = Search(manager, "MissionManager", out string name);
                if (found != null)
                {
                    description = name;
                    return found;
                }
            }
        }
        catch (Exception error)
        {
            ModLog.Error("Could not read the mission manager's civilian routes", error);
        }

        // 2. The mission editor: the view model, then the mission it is editing.
        try
        {
            MissionEditorViewModel? editor = MissionEditorViewModel.Instance;
            if (editor != null)
            {
                IList? found = Search(editor, "MissionEditorViewModel", out string name);
                if (found != null)
                {
                    description = name;
                    return found;
                }

                // One level deeper: whatever the editor calls its mission.
                foreach (MemberInfo member in MemberProbe.All(editor.GetType()))
                {
                    if (member is MethodInfo || member.Name.StartsWith("get_")) continue;

                    object? inner = MemberProbe.Read(member, editor);
                    if (inner == null || inner is string) continue;

                    IList? nested = Search(inner, "MissionEditorViewModel." + member.Name, out string innerName);
                    if (nested == null) continue;

                    description = innerName;
                    return nested;
                }
            }
        }
        catch (Exception error)
        {
            ModLog.Error("Could not read the mission editor's civilian routes", error);
        }

        description = "no civilian route list found (see traffic-report.txt)";
        return null;
    }

    /// <summary>
    /// A member on this object whose type is a list of <see cref="CivilianRoute"/>. The name is
    /// preferred but not required: a rename should not break the feature, only the log line.
    /// </summary>
    private static IList? Search(object owner, string ownerName, out string description)
    {
        description = "";

        List<MemberInfo> members;
        try { members = MemberProbe.All(owner.GetType()); }
        catch (Exception) { return null; }

        // Named members first, so the obvious answer wins over a coincidental one.
        foreach (bool namedOnly in new[] { true, false })
        {
            foreach (MemberInfo member in members)
            {
                if (member is MethodInfo) continue;

                string lowered = member.Name.ToLowerInvariant();
                if (namedOnly && !lowered.Contains("civilian")) continue;
                if (!namedOnly && !lowered.Contains("route")) continue;

                Type type = member is PropertyInfo property ? property.PropertyType
                            : ((FieldInfo)member).FieldType;

                if (!typeof(IList).IsAssignableFrom(type)) continue;

                try
                {
                    if (MemberProbe.Read(member, owner) is not IList list) continue;

                    description = ownerName + "." + member.Name + " (" + list.Count + " route(s))";
                    return list;
                }
                catch (Exception) { }
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the list can actually be added to. A fixed-size or read-only list would throw on Add,
    /// and reporting that here is clearer than an exception in the middle of a spawn.
    /// </summary>
    internal static bool CanAdd(IList list, out string why)
    {
        why = "";

        try
        {
            if (list.IsReadOnly) { why = "the list is read-only"; return false; }
            if (list.IsFixedSize) { why = "the list is fixed-size"; return false; }
            return true;
        }
        catch (Exception error)
        {
            why = error.GetType().Name;
            return false;
        }
    }
}
