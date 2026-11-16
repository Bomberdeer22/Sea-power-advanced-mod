using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;
using SeaPowerAdvanced.Interop;
using SeapowerUI.ViewModels;

namespace SeaPowerAdvanced.Civilian;

/// <summary>
/// A zone without drawing one: the sea the mission's own units are sitting in.
///
/// This exists because drawing a polygon depends on the editor's interaction state, the drawing
/// layer and the mouse being handed over cleanly - three things this mod does not own. When any of
/// them misbehaves the feature looks completely dead, and there is no way to see past it. A zone
/// built from where the mission's ships already are needs none of that: no drawing mode, no mouse,
/// no state push.
///
/// The area is the bounding box of every unit the game knows about plus a margin, which is where
/// traffic belongs anyway - around the shipping the player can see.
/// </summary>
internal static class MapArea
{
    /// <summary>How much larger than the units' own box the zone is. 30% keeps traffic off the
    /// hulls themselves while staying on the same stretch of sea.</summary>
    private const double Margin = 0.30;

    /// <summary>A zone this small takes no traffic; about 12 nm across is the floor.</summary>
    private const double MinimumDegrees = 0.10;

    /// <summary>
    /// Builds the zone. Returns an empty list when the mission holds no units to build one from,
    /// with the reason in <paramref name="note"/>.
    /// </summary>
    internal static List<GeoPosition> AroundUnits(out string note)
    {
        var polygon = new List<GeoPosition>();

        List<ObjectBase> units = PlacedUnits();
        if (units.Count == 0)
        {
            note = "no units placed yet - place a ship in the editor, or draw a zone";
            return polygon;
        }

        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLon = double.MaxValue, maxLon = double.MinValue;
        int used = 0;

        foreach (ObjectBase unit in units)
        {
            try
            {
                GeoPosition position = unit.getGeoPosition();

                if (double.IsNaN(position.Latitude) || double.IsNaN(position.Longitude)) continue;

                minLat = Math.Min(minLat, position.Latitude);
                maxLat = Math.Max(maxLat, position.Latitude);
                minLon = Math.Min(minLon, position.Longitude);
                maxLon = Math.Max(maxLon, position.Longitude);
                used++;
            }
            catch (Exception) { }
        }

        if (used == 0)
        {
            note = "the units in this mission have no position yet";
            return polygon;
        }

        double latSpan = Math.Max(maxLat - minLat, MinimumDegrees);
        double lonSpan = Math.Max(maxLon - minLon, MinimumDegrees);

        double midLat = (minLat + maxLat) / 2.0;
        double midLon = (minLon + maxLon) / 2.0;

        double halfLat = latSpan * (1.0 + Margin) / 2.0;
        double halfLon = lonSpan * (1.0 + Margin) / 2.0;

        polygon.Add(new GeoPosition(midLat - halfLat, midLon - halfLon, 0));
        polygon.Add(new GeoPosition(midLat - halfLat, midLon + halfLon, 0));
        polygon.Add(new GeoPosition(midLat + halfLat, midLon + halfLon, 0));
        polygon.Add(new GeoPosition(midLat + halfLat, midLon - halfLon, 0));

        note = used + " unit(s) placed; zone is their sea plus " + (int)(Margin * 100) + "%";
        return polygon;
    }

    /// <summary>
    /// Every unit the mod can find, from the running game if there is one and from the mission
    /// editor's own model otherwise. The editor keeps what has been placed in its mission rather
    /// than in GameMain, so both are needed.
    /// </summary>
    private static List<ObjectBase> PlacedUnits()
    {
        var units = new List<ObjectBase>();
        var seen = new HashSet<ObjectBase>();

        void Take(IEnumerable? sequence)
        {
            if (sequence == null) return;

            try
            {
                int guard = 0;
                foreach (object? item in sequence)
                {
                    if (++guard > 5000) return;
                    if (item is ObjectBase unit && seen.Add(unit)) units.Add(unit);
                }
            }
            catch (Exception) { }
        }

        try { Take(GameWorld.AllUnits()); }
        catch (Exception) { }

        try
        {
            MissionEditorViewModel? editor = MissionEditorViewModel.Instance;
            if (editor != null)
            {
                Take(Members(editor));

                foreach (MemberInfo member in MemberProbe.All(editor.GetType()))
                {
                    if (member is MethodInfo) continue;

                    try
                    {
                        object? inner = MemberProbe.Read(member, editor);
                        if (inner == null || inner is string || inner is IEnumerable) continue;
                        Take(Members(inner));
                    }
                    catch (Exception) { }
                }
            }
        }
        catch (Exception error)
        {
            ModLog.Warn("Map area: could not read the mission editor's units - " + error.Message);
        }

        return units;
    }

    /// <summary>Every enumerable member of one object, as sequences of things that might be units.</summary>
    private static IEnumerable<IEnumerable?> Members(object owner)
    {
        var found = new List<IEnumerable?>();

        try
        {
            foreach (MemberInfo member in MemberProbe.All(owner.GetType()))
            {
                if (member is MethodInfo) continue;
                if (member is PropertyInfo property && !property.CanRead) continue;

                Type type = member is PropertyInfo pi ? pi.PropertyType : ((FieldInfo)member).FieldType;
                if (!typeof(IEnumerable).IsAssignableFrom(type) || type == typeof(string)) continue;

                if (MemberProbe.Read(member, owner) is IEnumerable sequence) found.Add(sequence);
            }
        }
        catch (Exception) { }

        return found;
    }
}
