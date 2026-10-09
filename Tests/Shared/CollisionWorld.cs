using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Test collision world made of oriented boxes (like Source brushes), in metres.
/// Sweep is an exact swept axis-aligned box vs oriented box test using all 15 separating axes,
/// standing in for Unity's Physics.BoxCast. Like BoxCast, boxes overlapping at the start are ignored.
/// </summary>
public static class CollisionWorld
{
    public class Box
    {
        public Vector3 center, half;
        public Quaternion rotation;
        public int layer;
    }

    public static readonly List<Box> boxes = new List<Box>();
    public static int castCount;

    public static void Clear()
    {
        boxes.Clear();
        castCount = 0;
    }

    public static void Add(Vector3 center, Vector3 size, Quaternion rotation, int layer = 0) =>
        boxes.Add(new Box { center = center, half = size * 0.5f, rotation = rotation, layer = layer });

    /// <summary>Unity-style Euler rotation (Z, then X, then Y) without calling into the engine.</summary>
    public static Quaternion Euler(float x, float y, float z) => Mul(Mul(Axis(y, 0, 1, 0), Axis(x, 1, 0, 0)), Axis(z, 0, 0, 1));

    static Quaternion Axis(float degrees, float ax, float ay, float az)
    {
        double h = degrees * Math.PI / 360.0, s = Math.Sin(h);
        return new Quaternion((float)(ax * s), (float)(ay * s), (float)(az * s), (float)Math.Cos(h));
    }

    static Quaternion Mul(Quaternion a, Quaternion b) => new Quaternion(
        a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
        a.w * b.y + a.y * b.w + a.z * b.x - a.x * b.z,
        a.w * b.z + a.z * b.w + a.x * b.y - a.y * b.x,
        a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

    public static Vector3 Rotate(Quaternion q, Vector3 v)
    {
        Vector3 u = new Vector3(q.x, q.y, q.z);
        Vector3 t = 2f * Vector3.Cross(u, v);
        return v + q.w * t + Vector3.Cross(u, t);
    }

    /// <summary>Unity-style Euler angles in degrees (yaw and pitch only, roll is 0 in these tests).</summary>
    public static Vector3 EulerAngles(Quaternion q)
    {
        Vector3 f = Rotate(q, new Vector3(0, 0, 1));
        double yaw = Math.Atan2(f.x, f.z) * 180.0 / Math.PI;
        double pitch = Math.Asin(Math.Max(-1, Math.Min(1, -f.y))) * 180.0 / Math.PI;
        return new Vector3((float)((pitch + 360) % 360), (float)((yaw + 360) % 360), 0);
    }

    public static bool BoxCast(Vector3 center, Vector3 half, Vector3 dir, float maxDistance, out float distance, out Vector3 normal,
        int layerMask = ~0)
    {
        castCount++;
        distance = float.MaxValue;
        normal = Vector3.zero;
        foreach (Box box in boxes)
        {
            if ((layerMask & (1 << box.layer)) == 0) continue;
            if (Sweep(box, center, half, dir, maxDistance, out float t, out Vector3 n) && t < distance)
            {
                distance = t;
                normal = n;
            }
        }
        return distance != float.MaxValue;
    }

    /// <summary>Physics.CheckBox: does the axis-aligned box overlap any box on these layers?</summary>
    public static bool CheckBox(Vector3 center, Vector3 half, int layerMask)
    {
        castCount++;
        foreach (Box box in boxes)
            if ((layerMask & (1 << box.layer)) != 0 && Overlaps(box, center, half)) return true;
        return false;
    }

    static bool Overlaps(Box box, Vector3 c, Vector3 h)
    {
        // Separating axis test on the 15 axes, the same as Sweep with no motion.
        foreach (Vector3 axis in Axes(box))
        {
            float r = box.half.x * Math.Abs(Vector3.Dot(axis, Rotate(box.rotation, new Vector3(1, 0, 0))))
                + box.half.y * Math.Abs(Vector3.Dot(axis, Rotate(box.rotation, new Vector3(0, 1, 0))))
                + box.half.z * Math.Abs(Vector3.Dot(axis, Rotate(box.rotation, new Vector3(0, 0, 1))))
                + h.x * Math.Abs(axis.x) + h.y * Math.Abs(axis.y) + h.z * Math.Abs(axis.z);
            if (Math.Abs(Vector3.Dot(axis, c - box.center)) >= r) return false;
        }
        return true;
    }

    static List<Vector3> Axes(Box box)
    {
        Vector3[] obbAxes = { Rotate(box.rotation, new Vector3(1, 0, 0)), Rotate(box.rotation, new Vector3(0, 1, 0)), Rotate(box.rotation, new Vector3(0, 0, 1)) };
        Vector3[] worldAxes = { new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1) };
        var axes = new List<Vector3>(obbAxes);
        axes.AddRange(worldAxes);
        foreach (Vector3 a in obbAxes)
            foreach (Vector3 w in worldAxes)
            {
                Vector3 cr = Vector3.Cross(a, w);
                if (cr.sqrMagnitude > 1e-6f) axes.Add(cr / cr.magnitude);
            }
        return axes;
    }

    static bool Sweep(Box box, Vector3 c, Vector3 h, Vector3 dir, float maxDist, out float tHit, out Vector3 normal)
    {
        tHit = 0;
        normal = Vector3.zero;
        Vector3 bx = Rotate(box.rotation, new Vector3(1, 0, 0));
        Vector3 by = Rotate(box.rotation, new Vector3(0, 1, 0));
        Vector3 bz = Rotate(box.rotation, new Vector3(0, 0, 1));
        Vector3[] obbAxes = { bx, by, bz };
        Vector3[] worldAxes = { new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1) };
        var axes = new List<Vector3>(obbAxes);
        axes.AddRange(worldAxes);
        foreach (Vector3 a in obbAxes)
            foreach (Vector3 w in worldAxes)
            {
                Vector3 cr = Vector3.Cross(a, w);
                if (cr.sqrMagnitude > 1e-6f) axes.Add(cr / cr.magnitude);
            }

        float tEnter = float.MinValue, tExit = float.MaxValue;
        Vector3 enterNormal = Vector3.zero;
        foreach (Vector3 axis in axes)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 n = axis * s;
                // Plane of the Minkowski sum (box expanded by the moving box) along n.
                float d = Vector3.Dot(n, box.center)
                    + box.half.x * Math.Abs(Vector3.Dot(n, bx)) + box.half.y * Math.Abs(Vector3.Dot(n, by)) + box.half.z * Math.Abs(Vector3.Dot(n, bz))
                    + h.x * Math.Abs(n.x) + h.y * Math.Abs(n.y) + h.z * Math.Abs(n.z);
                float dist0 = Vector3.Dot(n, c) - d;
                float denom = Vector3.Dot(n, dir);
                if (Math.Abs(denom) < 1e-9f)
                {
                    if (dist0 > 0) return false;
                    continue;
                }
                float t = -dist0 / denom;
                if (denom < 0) { if (t > tEnter) { tEnter = t; enterNormal = n; } }
                else if (t < tExit) tExit = t;
            }
        }
        if (tEnter > tExit || tExit < 0) return false;
        if (tEnter < 1e-5f) return false; // started overlapping; PhysX counts exactly touching as overlapping too
        if (tEnter > maxDist) return false;
        tHit = tEnter;
        normal = enterNormal;
        return true;
    }
}
