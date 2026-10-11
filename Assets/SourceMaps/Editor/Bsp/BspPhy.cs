using System;
using System.Collections.Generic;
using System.Numerics;

namespace SourceMaps.Bsp
{
    /// <summary>
    /// A model's collision shape from its .phy file (VPhysics / IVP compact surfaces): the convex hulls the game collides
    /// with, in model space (Source units). Static props with "vphysics" collision (solid 6) use this in CS:S, not their
    /// render mesh: a tree collides with its trunk only, a fence with one box.
    /// </summary>
    public static class BspPhy
    {
        const float MetersToUnits = 39.37007874f; // IVP stores metres, axes (x, -z, y)

        /// <summary>The convex hulls of every solid as triangles (3 points each), and how many solids there are (one for
        /// a static prop; ragdolls have one per bone, in that bone's space). Empty if it isn't a compact-surface .phy.</summary>
        public static List<Vector3[]> Hulls(byte[] phy, out int solids)
        {
            var hulls = new List<Vector3[]>();
            solids = 0;
            if (phy == null || phy.Length < 16) return hulls;
            int headerSize = BitConverter.ToInt32(phy, 0);
            solids = BitConverter.ToInt32(phy, 8);
            int pos = headerSize;
            for (int s = 0; s < solids && pos + 4 <= phy.Length; s++)
            {
                int size = BitConverter.ToInt32(phy, pos);
                int start = pos + 4, surface = start;
                pos = start + size;
                if (phy[start] == 'V' && phy[start + 1] == 'P' && phy[start + 2] == 'H' && phy[start + 3] == 'Y')
                {
                    if (BitConverter.ToInt16(phy, start + 6) != 0) continue; // model type: only compact surfaces (not MOPP)
                    surface = start + 28;
                }
                // The ledge tree: inner nodes (28 bytes) have their left child right after them and their right child at
                // an offset; leaves point at a ledge, one convex hull.
                var nodes = new Stack<int>();
                nodes.Push(surface + BitConverter.ToInt32(phy, surface + 32));
                while (nodes.Count > 0)
                {
                    int node = nodes.Pop();
                    if (node < 0 || node + 8 > phy.Length) break;
                    int right = BitConverter.ToInt32(phy, node), ledgeOffset = BitConverter.ToInt32(phy, node + 4);
                    if (right != 0) { nodes.Push(node + 28); nodes.Push(node + right); continue; }
                    int ledge = node + ledgeOffset;
                    int pointOffset = BitConverter.ToInt32(phy, ledge);
                    int triangles = BitConverter.ToInt16(phy, ledge + 12);
                    var points = new List<Vector3>();
                    for (int t = 0; t < triangles; t++)
                        for (int e = 0; e < 3; e++)
                        {
                            int i = BitConverter.ToUInt16(phy, ledge + 16 + t * 16 + 4 + e * 4); // edge: its start point
                            int p = ledge + pointOffset + i * 16;
                            if (p < 0 || p + 12 > phy.Length) { points.Clear(); t = triangles; break; }
                            float x = BitConverter.ToSingle(phy, p), y = BitConverter.ToSingle(phy, p + 4), z = BitConverter.ToSingle(phy, p + 8);
                            points.Add(new Vector3(x, z, -y) * MetersToUnits);
                        }
                    if (points.Count >= 12) hulls.Add(points.ToArray());
                }
            }
            return hulls;
        }

        /// <summary>A .mdl's hull box (studiohdr hull_min / hull_max, model space): what "bounding box" collision uses.</summary>
        public static bool ModelHull(byte[] mdl, out Vector3 min, out Vector3 max)
        {
            min = max = Vector3.Zero;
            if (mdl == null || mdl.Length < 128 || mdl[0] != 'I' || mdl[1] != 'D' || mdl[2] != 'S' || mdl[3] != 'T') return false;
            min = new Vector3(BitConverter.ToSingle(mdl, 104), BitConverter.ToSingle(mdl, 108), BitConverter.ToSingle(mdl, 112));
            max = new Vector3(BitConverter.ToSingle(mdl, 116), BitConverter.ToSingle(mdl, 120), BitConverter.ToSingle(mdl, 124));
            return true;
        }
    }
}
