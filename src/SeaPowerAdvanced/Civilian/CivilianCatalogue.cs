using System;
using System.Collections.Generic;
using SeaPower;

namespace SeaPowerAdvanced.Civilian;

/// <summary>
/// The units a civilian route is allowed to spawn, read from the game's own unit database.
///
/// A route's <c>AllowedUnits</c> list holds unit INI names (<c>SeaPower.CivilianRoute.SpawnUnit</c>
/// looks each name up in <c>UnitMetadataProvider.UnitData</c> under the folder
/// <c>vessels</c> / <c>aircraft</c> / <c>land_units</c>). Rather than hard-code names that a game
/// patch could rename, this reads the database and keeps every class flagged
/// <c>SimpleObjectParameters._isCivilian</c>.
///
/// That flag is what produces the variety the feature is about: every civilian vessel and
/// aircraft class the install defines becomes a candidate.
/// </summary>
internal static class CivilianCatalogue
{
    private static readonly List<string> ships = new();
    private static readonly List<string> planes = new();
    private static bool tried;

    /// <summary>Civilian vessel INI names, alphabetical.</summary>
    internal static IReadOnlyList<string> Ships => ships;

    /// <summary>Civilian aircraft INI names, alphabetical.</summary>
    internal static IReadOnlyList<string> Planes => planes;

    /// <summary>True once the database has been read (even if it turned out to be empty).</summary>
    internal static bool Ready { get; private set; }

    internal static void Refresh(bool force = false)
    {
        if (tried && !force) return;
        tried = true;

        ships.Clear();
        planes.Clear();

        try
        {
            UnitMetadataProvider? provider = Singleton<UnitMetadataProvider>.Instance;
            if (provider == null)
            {
                ModLog.Warn("Civilian catalogue: the unit database is not available yet.");
                return;
            }

            // The database is filled on first use; this is the game's own entry point for that.
            provider.EnsureStart();

            Dictionary<ValueTuple<string, string>, ValueTuple<int, SimpleObjectParameters>>? data = provider.UnitData;
            if (data == null)
            {
                ModLog.Warn("Civilian catalogue: the unit database reported no data.");
                return;
            }

            HashSet<string> shipNames = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> planeNames = new(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<ValueTuple<string, string>, ValueTuple<int, SimpleObjectParameters>> entry in data)
            {
                SimpleObjectParameters? unit = entry.Value.Item2;
                if (unit == null || !unit._isCivilian) continue;

                string name = (unit._objectIniName ?? string.Empty).Trim();
                if (name.Length == 0) continue;

                switch (unit._objectType)
                {
                    case ObjectBase.ObjectType.Vessel:
                        shipNames.Add(name);
                        break;
                    case ObjectBase.ObjectType.Aircraft:
                    case ObjectBase.ObjectType.Helicopter:
                        planeNames.Add(name);
                        break;
                }
            }

            ships.AddRange(shipNames);
            planes.AddRange(planeNames);
            ships.Sort(StringComparer.OrdinalIgnoreCase);
            planes.Sort(StringComparer.OrdinalIgnoreCase);
            Ready = true;

            ModLog.Info("Civilian catalogue: " + ships.Count + " civilian vessel class(es), "
                        + planes.Count + " civilian aircraft class(es).");

            if (ships.Count == 0 && planes.Count == 0)
            {
                ModLog.Warn("Civilian catalogue is empty. Either nothing in this install is flagged "
                            + "civilian, or the database was not loaded yet; press Refresh in the panel later.");
            }
        }
        catch (Exception error)
        {
            ModLog.Error("Could not read the civilian unit catalogue", error);
        }
    }

    /// <summary>A short human-readable summary for the panel.</summary>
    internal static string Describe()
        => ships.Count + " ship class(es), " + planes.Count + " aircraft class(es)";
}
