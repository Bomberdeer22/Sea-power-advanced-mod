using System;
using System.Collections.Generic;
using SeaPower;

namespace SeaPowerAdvanced.Civilian;

/// <summary>
/// A closed polygon on the map plus the small amount of geometry the generator needs:
/// "is this point inside?" and "give me a random point inside".
///
/// Longitude degrees shrink with latitude, so x is scaled by cos(latitude) — without that a zone
/// in the North Atlantic would test as a stretched parallelogram and points would drift outside it.
/// </summary>
internal sealed class TrafficZone
{
    private readonly List<GeoPosition> points;
    private readonly double minLatitude;
    private readonly double maxLatitude;
    private readonly double minLongitude;
    private readonly double maxLongitude;
    private readonly double xScale;
    private readonly double centreLatitude;
    private readonly double centreLongitude;

    private TrafficZone(List<GeoPosition> points)
    {
        this.points = points;

        double sumLatitude = 0;
        double sumLongitude = 0;
        double minLat = double.MaxValue;
        double maxLat = double.MinValue;
        double minLon = double.MaxValue;
        double maxLon = double.MinValue;

        foreach (GeoPosition point in points)
        {
            sumLatitude += point.Latitude;
            sumLongitude += point.Longitude;
            minLat = Math.Min(minLat, point.Latitude);
            maxLat = Math.Max(maxLat, point.Latitude);
            minLon = Math.Min(minLon, point.Longitude);
            maxLon = Math.Max(maxLon, point.Longitude);
        }

        centreLatitude = sumLatitude / points.Count;
        centreLongitude = sumLongitude / points.Count;
        minLatitude = minLat;
        maxLatitude = maxLat;
        minLongitude = minLon;
        maxLongitude = maxLon;

        double cosine = Math.Cos(centreLatitude * Math.PI / 180.0);
        xScale = Math.Abs(cosine) < 0.01 ? 0.01 : cosine;
    }

    internal int PointCount => points.Count;

    internal IReadOnlyList<GeoPosition> Points => points;

    internal GeoPosition Centre => new(centreLatitude, centreLongitude, 0);

    /// <summary>Rough area in square nautical miles, only used for the status line.</summary>
    internal double AreaSquareNauticalMiles
    {
        get
        {
            double widthDegrees = (maxLongitude - minLongitude) * xScale;
            double heightDegrees = maxLatitude - minLatitude;
            const double NauticalMilesPerDegree = 60.0;
            return widthDegrees * NauticalMilesPerDegree * heightDegrees * NauticalMilesPerDegree;
        }
    }

    /// <summary>Builds a zone from at least three points; returns null otherwise.</summary>
    internal static TrafficZone? Create(IReadOnlyList<GeoPosition>? polygon)
    {
        if (polygon == null || polygon.Count < 3) return null;

        List<GeoPosition> copy = new(polygon.Count);
        // GeoPosition is a struct: it cannot be null, so the only useless points are the ones
        // carrying NaN coordinates.
        foreach (GeoPosition point in polygon)
        {
            if (double.IsNaN(point.Latitude) || double.IsNaN(point.Longitude)) continue;
            copy.Add(point);
        }

        if (copy.Count < 3) return null;

        TrafficZone zone = new(copy);

        // A degenerate zone (a straight line drawn by accident) has no area to scatter units in.
        if (zone.maxLatitude - zone.minLatitude < 1e-4 && zone.maxLongitude - zone.minLongitude < 1e-4)
            return null;

        return zone;
    }

    /// <summary>Ray casting on the scaled plane.</summary>
    internal bool Contains(GeoPosition point)
    {
        double x = (point.Longitude - centreLongitude) * xScale;
        double y = point.Latitude - centreLatitude;

        bool inside = false;
        int count = points.Count;

        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            double xi = (points[i].Longitude - centreLongitude) * xScale;
            double yi = points[i].Latitude - centreLatitude;
            double xj = (points[j].Longitude - centreLongitude) * xScale;
            double yj = points[j].Latitude - centreLatitude;

            bool straddles = (yi > y) != (yj > y);
            if (!straddles) continue;

            double crossing = xi + (y - yi) * (xj - xi) / (yj - yi);
            if (x < crossing) inside = !inside;
        }

        return inside;
    }

    /// <summary>
    /// A random point inside the polygon. Rejection sampling inside the bounding box: correct for
    /// any polygon shape (concave zones from a hand click are common) and fast enough at these
    /// counts, with the centroid as a guaranteed fallback.
    /// </summary>
    internal GeoPosition RandomPoint(Random random, GeoPosition? avoid = null, double minimumDegrees = 0.0)
    {
        for (int attempt = 0; attempt < 240; attempt++)
        {
            double latitude = minLatitude + random.NextDouble() * (maxLatitude - minLatitude);
            double longitude = minLongitude + random.NextDouble() * (maxLongitude - minLongitude);
            GeoPosition candidate = new(latitude, longitude, 0);

            if (!Contains(candidate)) continue;

            // avoid is GeoPosition? - HasValue plus .Value, not a null comparison.
            if (minimumDegrees > 0 && avoid.HasValue)
            {
                GeoPosition anchor = avoid.Value;
                double dLat = candidate.Latitude - anchor.Latitude;
                double dLon = (candidate.Longitude - anchor.Longitude) * xScale;
                if (Math.Sqrt(dLat * dLat + dLon * dLon) < minimumDegrees) continue;
            }

            return candidate;
        }

        return Contains(Centre) ? Centre : points[0];
    }

    /// <summary>
    /// A route through the zone: a spawn point, then 2-4 further points ordered so the path reads
    /// as one course rather than a scribble.
    /// </summary>
    internal List<GeoPosition> RandomPath(Random random, int extraPoints)
    {
        GeoPosition spawn = RandomPoint(random);

        List<GeoPosition> candidates = new();
        for (int i = 0; i < extraPoints * 6 && candidates.Count < extraPoints; i++)
        {
            GeoPosition candidate = RandomPoint(random, spawn, 0.05);
            bool tooClose = false;

            foreach (GeoPosition other in candidates)
            {
                double dLat = candidate.Latitude - other.Latitude;
                double dLon = (candidate.Longitude - other.Longitude) * xScale;
                if (Math.Sqrt(dLat * dLat + dLon * dLon) < 0.05)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose) continue;

            // A very small zone can fall back to the same point; repeating a waypoint would give the
            // route a zero-length leg.
            double stepLat = candidate.Latitude - spawn.Latitude;
            double stepLon = (candidate.Longitude - spawn.Longitude) * xScale;
            if (Math.Sqrt(stepLat * stepLat + stepLon * stepLon) < 1e-5) continue;

            candidates.Add(candidate);
        }

        List<GeoPosition> path = new() { spawn };

        GeoPosition last = spawn;
        while (candidates.Count > 0)
        {
            int best = 0;
            double bestDistance = double.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                double dLat = candidates[i].Latitude - last.Latitude;
                double dLon = (candidates[i].Longitude - last.Longitude) * xScale;
                double distance = dLat * dLat + dLon * dLon;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            last = candidates[best];
            path.Add(last);
            candidates.RemoveAt(best);
        }

        return path;
    }
}
