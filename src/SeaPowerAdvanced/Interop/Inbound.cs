using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SeaPower;

namespace SeaPowerAdvanced.Interop;

/// <summary>
/// The vampires: weapons the game is currently tracking against a unit, and against the units
/// around it.
///
/// Two features now need this answer - the anti-radiation shutoff and the defensive ECM - and they
/// must agree about what "inbound" means. One definition, here, is better than two that drift.
///
/// The weapon objects are returned as <see cref="object"/> on purpose. Their real types
/// (<c>Missile</c>, <c>WeaponBase</c>) are not names this project can rely on: every game type that
/// has been named in code so far, apart from a handful, failed to compile. What matters is the
/// instance, because the jammer has to be pointed at the missile itself, and an instance needs no
/// type name.
/// </summary>
internal static class Inbound
{
    /// <summary>Every weapon the game is tracking against this unit. Empty when nothing is inbound.</summary>
    internal static List<object> WeaponsAgainst(ObjectBase? unit)
    {
        var found = new List<object>();
        if (unit == null) return found;

        try
        {
            Collect(unit._dangerousMissiles, found);
            Collect(unit._incomingWeapons, found);
        }
        catch (Exception) { }

        try
        {
            MemberInfo? member = MemberProbe.Value(typeof(ObjectBase), "ThreateningMissile");
            if (member != null && IsReference(member) &&
                MemberProbe.Read(member, unit) is object single)
            {
                Add(single, found);
            }
        }
        catch (Exception) { }

        return found;
    }

    /// <summary>
    /// Only the weapons the game itself has flagged as dangerous to this unit.
    ///
    /// <c>_dangerousMissiles</c> is the game's own "this one is coming for you" list, and it is the
    /// honest trigger for a hard kill: the mod is not deciding that a missile three hundred miles
    /// away is about to hit, the game is saying this one is a threat now.
    /// </summary>
    internal static List<object> DangerousAgainst(ObjectBase? unit)
    {
        var found = new List<object>();
        if (unit == null) return found;

        try { Collect(unit._dangerousMissiles, found); }
        catch (Exception) { }

        return found;
    }

    /// <summary>True when something is inbound to this unit.</summary>
    internal static bool Any(ObjectBase? unit) => WeaponsAgainst(unit).Count > 0;

    /// <summary>
    /// True when a weapon is inbound to this unit, or to a friendly unit within
    /// <paramref name="radiusNm"/>. The second half is what makes the answer useful to a formation:
    /// a missile aimed at the carrier crosses the frigate's airspace too, and the frigate's jammer
    /// can be pointed at it.
    /// </summary>
    internal static bool AnyNearby(ObjectBase? unit, List<ObjectBase> fleet, float radiusNm,
                                   out object? weapon, out ObjectBase? threatened)
    {
        weapon = null;
        threatened = null;

        if (unit == null) return false;

        List<object> own = WeaponsAgainst(unit);
        if (own.Count > 0)
        {
            weapon = own[0];
            threatened = unit;
            return true;
        }

        foreach (ObjectBase other in fleet)
        {
            if (other == null || ReferenceEquals(other, unit)) continue;
            if (RangeNm(unit, other) is not double range || range > radiusNm) continue;

            List<object> theirs = WeaponsAgainst(other);
            if (theirs.Count == 0) continue;

            weapon = theirs[0];
            threatened = other;
            return true;
        }

        return false;
    }

    /// <summary>Distance between two units in nautical miles, or null when either position is unreadable.</summary>
    internal static double? RangeNm(ObjectBase? a, ObjectBase? b)
    {
        if (a == null || b == null) return null;

        try
        {
            GeoPosition one = a.getGeoPosition();
            GeoPosition two = b.getGeoPosition();

            double dLat = one.Latitude - two.Latitude;
            double dLon = (one.Longitude - two.Longitude)
                          * Math.Cos(two.Latitude * Math.PI / 180.0);

            return Math.Sqrt(dLat * dLat + dLon * dLon) * 60.0;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Whether a member's type can hold null - a value type there is never "a missile".</summary>
    private static bool IsReference(MemberInfo member)
    {
        Type type = member is PropertyInfo property ? property.PropertyType
                    : member is FieldInfo field ? field.FieldType
                    : typeof(object);

        return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
    }

    private static void Collect(object? source, List<object> into)
    {
        if (source is not IEnumerable items) return;

        foreach (object? item in items)
        {
            if (item != null) Add(item, into);
        }
    }

    private static void Add(object weapon, List<object> into)
    {
        foreach (object present in into)
        {
            if (ReferenceEquals(present, weapon)) return;
        }
        into.Add(weapon);
    }
}
