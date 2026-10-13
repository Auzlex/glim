
#if UNITY_EDITOR

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Assigns shadowmask channels (0-3 = R,G,B,A) to lights so that lights whose
/// influence volumes overlap never share a channel (graph coloring, DSatur).
///
/// Result[i] belongs to lights[i]:
///   0..3 = assigned channel
///   -1   = not Mixed, unsupported light type, null light, or no free channel
///          (more than 4 mutually overlapping lights)
/// </summary>
public static class ShadowMaskChannelAssigner
{
    public const int ChannelCount = 4;

    // How many bounding spheres approximate a spot cone in the cone-vs-cone test.
    // More = tighter (fewer false overlaps), slightly slower.
    const int ConeSphereSegments = 12;

    enum Kind { Point, Spot, Directional }

    struct Volume
    {
        public Kind kind;
        public Vector3 pos;
        public Vector3 axis;        // spot only, normalized
        public float range;
        public float tanHalf;       // spot only, tan(outerAngle / 2)
        public Vector3 boundCenter; // bounding sphere (point + spot)
        public float boundRadius;
    }

    public static List<int> Assign(IList<Light> lights)
    {
        int n = lights.Count;
        var result = new List<int>(n);
        for (int i = 0; i < n; i++) result.Add(-1);

        // 1. Build volumes for every participating light
        var vols = new Volume[n];
        var active = new bool[n];
        int activeCount = 0;
        for (int i = 0; i < n; i++)
        {
            active[i] = TryBuildVolume(lights[i], out vols[i]);
            if (active[i]) activeCount++;
        }

        // 2. Conflict graph
        var adj = new List<int>[n];
        for (int i = 0; i < n; i++) adj[i] = new List<int>();

        for (int i = 0; i < n; i++)
        {
            if (!active[i]) continue;
            for (int j = i + 1; j < n; j++)
            {
                if (!active[j]) continue;
                if (Overlaps(vols[i], vols[j]))
                {
                    adj[i].Add(j);
                    adj[j].Add(i);
                }
            }
        }

        // 3. DSatur coloring (deterministic: ties go to higher degree, then lower index)
        var done = new bool[n];
        for (int step = 0; step < activeCount; step++)
        {
            int best = -1, bestSat = -1, bestDeg = -1;
            for (int i = 0; i < n; i++)
            {
                if (!active[i] || done[i]) continue;
                int sat = PopCount(UsedMask(adj[i], result));
                int deg = adj[i].Count;
                if (sat > bestSat || (sat == bestSat && deg > bestDeg))
                {
                    best = i; bestSat = sat; bestDeg = deg;
                }
            }

            int used = UsedMask(adj[best], result);
            int channel = -1;
            for (int c = 0; c < ChannelCount; c++)
            {
                if ((used & (1 << c)) == 0) { channel = c; break; }
            }

            result[best] = channel; // stays -1 if all 4 channels are taken by neighbors
            done[best] = true;
        }

        return result;
    }

    // ---------------------------------------------------------------- graph helpers

    static int UsedMask(List<int> neighbors, List<int> channels)
    {
        int mask = 0;
        for (int k = 0; k < neighbors.Count; k++)
        {
            int c = channels[neighbors[k]];
            if (c >= 0) mask |= 1 << c;
        }
        return mask;
    }

    static int PopCount(int v)
    {
        int c = 0;
        while (v != 0) { c += v & 1; v >>= 1; }
        return c;
    }

    // ---------------------------------------------------------------- volumes

    static LightmapBakeType GetBakeType(Light l)
    {
        return l.lightmapBakeType;
    }

    static bool TryBuildVolume(Light light, out Volume v)
    {
        v = default;
        if (light == null) return false;
        if (GetBakeType(light) != LightmapBakeType.Mixed) return false;

        if (light.shadows == LightShadows.None)
        {
            return false;
        }

        Transform t = light.transform;
        v.pos = t.position;
        v.range = light.range;

        switch (light.type)
        {
            case LightType.Directional:
                v.kind = Kind.Directional;
                return true;

            case LightType.Point:
                v.kind = Kind.Point;
                v.boundCenter = v.pos;
                v.boundRadius = v.range;
                return true;

            case LightType.Spot:
                {
                    v.kind = Kind.Spot;
                    v.axis = t.forward;
                    // spotAngle is the OUTER angle in Unity; inner angle is ignored on purpose
                    float halfRad = Mathf.Clamp(light.spotAngle, 1f, 179f) * 0.5f * Mathf.Deg2Rad;
                    v.tanHalf = Mathf.Tan(halfRad);

                    // Bounding sphere of the cone (apex + base circle)
                    float h = v.range;
                    float tan2 = v.tanHalf * v.tanHalf;
                    if (tan2 <= 1f) // half angle <= 45 deg: sphere through apex and base rim
                    {
                        float d = h * (1f + tan2) * 0.5f;
                        v.boundCenter = v.pos + v.axis * d;
                        v.boundRadius = d;
                    }
                    else            // wide cone: sphere centered on the base disc
                    {
                        v.boundCenter = v.pos + v.axis * h;
                        v.boundRadius = h * v.tanHalf;
                    }
                    return true;
                }

            default:
                // Area / Rectangle / Disc / Pyramid / Box etc.
                return false;
        }
    }

    // ---------------------------------------------------------------- overlap tests

    static bool Overlaps(in Volume a, in Volume b)
    {
        // Directional lights reach everything
        if (a.kind == Kind.Directional || b.kind == Kind.Directional) return true;

        if (a.kind == Kind.Point && b.kind == Kind.Point)
            return SpheresOverlap(a.pos, a.range, b.pos, b.range);

        if (a.kind == Kind.Point && b.kind == Kind.Spot)
            return ConeIntersectsSphere(b, a.pos, a.range);

        if (a.kind == Kind.Spot && b.kind == Kind.Point)
            return ConeIntersectsSphere(a, b.pos, b.range);

        // Spot vs spot
        if (!SpheresOverlap(a.boundCenter, a.boundRadius, b.boundCenter, b.boundRadius))
            return false;

        // Each test is conservative (can report false overlaps, never misses a real one),
        // so requiring both to pass keeps it safe while tightening the result.
        return ConeIntersectsConeSpheres(a, b) && ConeIntersectsConeSpheres(b, a);
    }

    static bool SpheresOverlap(Vector3 c0, float r0, Vector3 c1, float r1)
    {
        float r = r0 + r1;
        return (c0 - c1).sqrMagnitude < r * r;
    }

    /// <summary>
    /// Exact cone (apex, axis, outer half-angle, height = range) vs sphere test.
    /// Reduces to 2D (distance along axis, distance from axis) where the cone is the
    /// triangle (0,0) - (h,0) - (h, h*tan) and the sphere is a circle.
    /// </summary>
    static bool ConeIntersectsSphere(in Volume cone, Vector3 center, float radius)
    {
        Vector3 v = center - cone.pos;
        float a = Vector3.Dot(v, cone.axis);
        float b = Mathf.Sqrt(Mathf.Max(0f, v.sqrMagnitude - a * a));

        float h = cone.range;
        float tanH = cone.tanHalf;

        // Center inside the cone
        if (a >= 0f && a <= h && b <= a * tanH) return true;

        // Otherwise, closest distance from the center to the triangle boundary
        Vector2 p = new Vector2(a, b);
        Vector2 o = Vector2.zero;
        Vector2 bs = new Vector2(h, 0f);
        Vector2 rim = new Vector2(h, h * tanH);

        float r2 = radius * radius;
        return SqrDistToSegment(p, o, bs) <= r2
            || SqrDistToSegment(p, bs, rim) <= r2
            || SqrDistToSegment(p, rim, o) <= r2;
    }

    /// <summary>
    /// Cone A vs cone B, where B is covered by a chain of bounding spheres
    /// (one per frustum slice along its axis).
    /// </summary>
    static bool ConeIntersectsConeSpheres(in Volume a, in Volume b)
    {
        float h = b.range;
        for (int i = 0; i < ConeSphereSegments; i++)
        {
            float t0 = h * i / ConeSphereSegments;
            float t1 = h * (i + 1) / ConeSphereSegments;
            float halfLen = (t1 - t0) * 0.5f;
            float rimRadius = t1 * b.tanHalf; // larger rim of this slice

            Vector3 center = b.pos + b.axis * (t0 + halfLen);
            float radius = Mathf.Sqrt(halfLen * halfLen + rimRadius * rimRadius);

            if (ConeIntersectsSphere(a, center, radius)) return true;
        }
        return false;
    }

    static float SqrDistToSegment(Vector2 p, Vector2 s0, Vector2 s1)
    {
        Vector2 d = s1 - s0;
        float len2 = d.sqrMagnitude;
        float t = len2 > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - s0, d) / len2) : 0f;
        return (p - (s0 + d * t)).sqrMagnitude;
    }
}

#endif