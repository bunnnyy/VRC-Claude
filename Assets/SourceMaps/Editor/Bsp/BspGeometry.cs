using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SourceMaps.Bsp
{
    /// <summary>Triangle mesh in Unity space (meters, left-handed, y up). Front faces wind clockwise, like Unity.</summary>
    public class MeshData
    {
        public List<Vector3> Vertices = new List<Vector3>();
        public List<int> Triangles = new List<int>();

        /// <summary>Adds a triangle and makes its front face point along `outward` (Unity space).</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            int i = Vertices.Count;
            Vertices.Add(a);
            // Unity's face normal is cross(b - a, c - a); swap b and c if it points the wrong way.
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) >= 0) { Vertices.Add(b); Vertices.Add(c); }
            else { Vertices.Add(c); Vertices.Add(b); }
            Triangles.Add(i); Triangles.Add(i + 1); Triangles.Add(i + 2);
        }
    }

    /// <summary>
    /// Builds collision geometry from a BspFile: brushes (convex volumes bounded by planes) and displacements.
    /// Source to Unity: (x, y, z) -> (-y, z, x) * scale, the same mapping uSource uses, so converted visuals line up.
    /// </summary>
    public static class BspGeometry
    {
        /// <summary>Source units to meters at CS:S player scale (72 units = 1.37 m), same as SourceMovement.</summary>
        public const float DefaultScale = 0.01905f;

        public static Vector3 ToUnity(Vector3 v, float scale) { return new Vector3(-v.Y, v.Z, v.X) * scale; }
        public static Vector3 DirectionToUnity(Vector3 v) { return new Vector3(-v.Y, v.Z, v.X); }

        /// <summary>
        /// Faces of one brush as polygons (Source space): each non-bevel side's plane, clipped by all other sides.
        /// Side normals point out of the brush; inside is dot(n, p) &lt;= dist.
        /// raiseTop moves upward-facing sides out by that many units, grow the sides out sideways (a box that much
        /// wider each way, like Source's hull touching it) (both used to fit triggers to VRChat's player, see AddBrush).
        /// </summary>
        public static List<Vector3[]> BrushPolygons(BspFile bsp, int brushIndex, float raiseTop = 0f, float grow = 0f)
        {
            return BrushFaces(bsp, brushIndex, raiseTop, grow).Select(f => f.Key).ToList();
        }

        /// <summary>BrushPolygons with each face's plane, exact from the BSP (the clipped corners carry float error).</summary>
        public static List<KeyValuePair<Vector3[], BspFile.Plane>> BrushFaces(BspFile bsp, int brushIndex, float raiseTop = 0f, float grow = 0f)
        {
            var brush = bsp.Brushes[brushIndex];
            var result = new List<KeyValuePair<Vector3[], BspFile.Plane>>();
            for (int s = 0; s < brush.NumSides; s++)
            {
                var side = bsp.BrushSides[brush.FirstSide + s];
                if (side.Bevel) continue;
                var plane = bsp.Planes[side.Plane];
                var poly = BasePolygon(plane.Normal, Dist(plane, raiseTop, grow));
                for (int o = 0; o < brush.NumSides && poly.Count >= 3; o++)
                {
                    if (o == s) continue;
                    var other = bsp.Planes[bsp.BrushSides[brush.FirstSide + o].Plane];
                    if (bsp.BrushSides[brush.FirstSide + o].Plane == side.Plane) continue;
                    poly = Clip(poly, other.Normal, Dist(other, raiseTop, grow));
                }
                if (poly.Count >= 3) result.Add(new KeyValuePair<Vector3[], BspFile.Plane>(poly.ToArray(), new BspFile.Plane { Normal = plane.Normal, Dist = Dist(plane, raiseTop, grow) }));
            }
            return result;
        }

        static float Dist(BspFile.Plane plane, float raiseTop, float grow)
        {
            // Out along the normal, so sloped tops get just as much thicker as flat ones; sideways as a square of
            // half-width `grow` swept around the brush would.
            float d = plane.Dist + grow * (System.Math.Abs(plane.Normal.X) + System.Math.Abs(plane.Normal.Y));
            return plane.Normal.Z > 0.7f ? d + raiseTop : d;
        }

        /// <summary>
        /// Adds a brush's faces to a mesh (Unity space). raiseTop (units) lifts its top: Source touches triggers
        /// with a flat-bottomed box, VRChat's player is a rounded capsule floating a few cm above the ground, so a
        /// thin trigger lying on a floor would never fire without it. grow (units) widens it the same way: the
        /// capsule is thinner than Source's 32 unit box.
        /// </summary>
        public static void AddBrush(MeshData mesh, BspFile bsp, int brushIndex, float scale, float raiseTop = 0f, float grow = 0f)
        {
            foreach (var poly in BrushPolygons(bsp, brushIndex, raiseTop, grow))
            {
                Vector3 outward = DirectionToUnity(PlaneOf(poly));
                for (int i = 1; i + 1 < poly.Length; i++)
                    mesh.AddTriangle(ToUnity(poly[0], scale), ToUnity(poly[i], scale), ToUnity(poly[i + 1], scale), outward);
            }
        }

        /// <summary>
        /// Adds solid brushes to a mesh without the faces (or parts of faces) pressed against an opposite face of another
        /// brush in the set: those lie inside the solid. Source collides with whole brushes and never meets them, but in
        /// a triangle mesh their edges sit exactly in the surface where two brushes meet (a ramp built from several
        /// brushes), and a player sliding along it catches on them. Returns how many faces lost a covered part.
        /// </summary>
        public static int AddSolidBrushes(MeshData mesh, BspFile bsp, IEnumerable<int> brushes, float scale)
        {
            var faces = new List<(Vector3[] poly, Vector3 n, float d)>();
            var byPlane = new Dictionary<(int, int, int, int), List<int>>();
            foreach (int b in brushes)
                foreach (var face in BrushFaces(bsp, b))
                {
                    var poly = face.Key;
                    Vector3 n = face.Value.Normal;
                    float d = face.Value.Dist; // exact: corners can be ~0.1 unit off, which split touching faces apart
                    var key = PlaneKey(n, d);
                    if (!byPlane.TryGetValue(key, out var list)) byPlane[key] = list = new List<int>();
                    list.Add(faces.Count);
                    faces.Add((poly, n, d));
                }
            int trimmed = 0;
            foreach (var f in faces)
            {
                var pieces = new List<List<Vector3>> { new List<Vector3>(f.poly) };
                if (byPlane.TryGetValue(PlaneKey(-f.n, -f.d), out var opposite))
                {
                    foreach (int j in opposite) pieces = pieces.SelectMany(p => Subtract(p, faces[j].poly, f.n)).ToList();
                    if (pieces.Count != 1 || Math.Abs(Area(pieces[0]) - Area(f.poly)) > 0.01f) trimmed++;
                }
                Vector3 outward = DirectionToUnity(f.n);
                foreach (var piece in pieces)
                {
                    // Clipping repeats corners: drop repeats and zero-area triangles (their normal would be NaN).
                    var poly = piece.Where((v, i) => (v - piece[(i + 1) % piece.Count]).Length() > 0.01f).ToList();
                    for (int i = 1; i + 1 < poly.Count; i++)
                        if (Vector3.Cross(poly[i] - poly[0], poly[i + 1] - poly[0]).Length() > 0.01f)
                            mesh.AddTriangle(ToUnity(poly[0], scale), ToUnity(poly[i], scale), ToUnity(poly[i + 1], scale), outward);
                }
            }
            return trimmed;
        }

        static (int, int, int, int) PlaneKey(Vector3 n, float d)
        {
            return ((int)Math.Round(n.X * 1000), (int)Math.Round(n.Y * 1000), (int)Math.Round(n.Z * 1000), (int)Math.Round(d * 10));
        }

        /// <summary>The parts of convex polygon p (on a plane with normal n) outside convex polygon c, as convex pieces.</summary>
        static List<List<Vector3>> Subtract(List<Vector3> p, IList<Vector3> c, Vector3 n)
        {
            var result = new List<List<Vector3>>();
            Vector3 pMin = p.Aggregate(Vector3.Min), pMax = p.Aggregate(Vector3.Max);
            Vector3 cMin = c.Aggregate(Vector3.Min), cMax = c.Aggregate(Vector3.Max);
            Vector3 overlap = Vector3.Min(pMax, cMax) - Vector3.Max(pMin, cMin);
            if (Math.Min(overlap.X, Math.Min(overlap.Y, overlap.Z)) < -0.05f) { result.Add(p); return result; } // apart
            Vector3 centre = Vector3.Zero;
            foreach (var v in c) centre += v / c.Count;
            for (int k = 0; k < c.Count && p.Count >= 3; k++)
            {
                Vector3 a = c[k], b = c[(k + 1) % c.Count];
                if ((b - a).Length() < 0.01f) continue;
                Vector3 e = Vector3.Normalize(Vector3.Cross(b - a, n));
                if (Vector3.Dot(e, centre - a) > 0) e = -e; // e points out of c
                float ed = Vector3.Dot(e, a);
                var outside = Clip(p, -e, -ed - 0.02f); // strictly outside this edge (Clip keeps 0.01 extra)
                if (Area(outside) > 0.01f) result.Add(outside);
                p = Clip(p, e, ed);
            }
            return result; // what is left of p is covered by c
        }

        static float Area(IList<Vector3> poly)
        {
            if (poly.Count < 3) return 0f;
            Vector3 n = Vector3.Zero;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector3 a = poly[i], b = poly[(i + 1) % poly.Count];
                n += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
            }
            return n.Length() * 0.5f;
        }

        /// <summary>Newell normal of a polygon; Source polygons from Clip keep the plane's outward winding.</summary>
        static Vector3 PlaneOf(IList<Vector3> poly)
        {
            Vector3 n = Vector3.Zero;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector3 a = poly[i], b = poly[(i + 1) % poly.Count];
                n += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
            }
            return Vector3.Normalize(n);
        }

        /// <summary>Displacement surface as triangles (Unity space). Front faces point along the base face's normal.</summary>
        public static void AddDisplacement(MeshData mesh, BspFile bsp, int dispIndex, float scale)
        {
            var disp = bsp.DispInfos[dispIndex];
            var corners = bsp.FaceVertices(disp.MapFace);
            if (corners.Length != 4) return;

            // Rotate the corners so corner 0 is the one at StartPosition.
            int first = 0;
            float best = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                float d = Vector3.DistanceSquared(corners[i], disp.StartPosition);
                if (d < best) { best = d; first = i; }
            }
            var p = new Vector3[4];
            for (int i = 0; i < 4; i++) p[i] = corners[(first + i) % 4];

            var face = bsp.Faces[disp.MapFace];
            var plane = bsp.Planes[face.Plane];
            Vector3 up = face.Side != 0 ? -plane.Normal : plane.Normal;

            int n = (1 << disp.Power) + 1;
            var grid = new Vector3[n * n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                Vector3 left = Vector3.Lerp(p[0], p[1], t);
                Vector3 right = Vector3.Lerp(p[3], p[2], t);
                for (int j = 0; j < n; j++)
                {
                    var dv = bsp.DispVerts[disp.DispVertStart + i * n + j];
                    grid[i * n + j] = ToUnity(Vector3.Lerp(left, right, j / (float)(n - 1)) + dv.Vec * dv.Dist, scale);
                }
            }

            Vector3 outward = DirectionToUnity(up);
            for (int i = 0; i < n - 1; i++)
                for (int j = 0; j < n - 1; j++)
                {
                    int a = i * n + j, b = a + 1, c = a + n, d = c + 1;
                    // Alternate the diagonal like Source does so the surface matches the game.
                    if ((a % 2) == 1) { mesh.AddTriangle(grid[a], grid[b], grid[c], outward); mesh.AddTriangle(grid[b], grid[d], grid[c], outward); }
                    else { mesh.AddTriangle(grid[a], grid[d], grid[c], outward); mesh.AddTriangle(grid[a], grid[b], grid[d], outward); }
                }
        }

        /// <summary>Large square on a plane, wound so its Newell normal equals the plane normal.</summary>
        static List<Vector3> BasePolygon(Vector3 normal, float dist)
        {
            Vector3 axis = Math.Abs(normal.Z) > 0.9f ? Vector3.UnitX : Vector3.UnitZ;
            Vector3 u = Vector3.Normalize(Vector3.Cross(axis, normal)) * 65536f;
            Vector3 v = Vector3.Cross(normal, u);
            Vector3 c = normal * dist;
            // Counter-clockwise seen from the normal side: Newell normal = +normal.
            return new List<Vector3> { c - u - v, c + u - v, c + u + v, c - u + v };
        }

        /// <summary>Keeps the part of a polygon with dot(n, p) &lt;= dist (Sutherland-Hodgman).</summary>
        static List<Vector3> Clip(List<Vector3> poly, Vector3 n, float dist)
        {
            const float Epsilon = 0.01f;
            var result = new List<Vector3>(poly.Count + 1);
            for (int i = 0; i < poly.Count; i++)
            {
                Vector3 a = poly[i], b = poly[(i + 1) % poly.Count];
                float da = Vector3.Dot(n, a) - dist, db = Vector3.Dot(n, b) - dist;
                bool inA = da <= Epsilon, inB = db <= Epsilon;
                if (inA) result.Add(a);
                if (inA != inB && Math.Abs(da - db) > 1e-6f) result.Add(Vector3.Lerp(a, b, da / (da - db)));
            }
            return result;
        }

        /// <summary>
        /// Rotation of a Source "angles" key (pitch yaw roll, degrees) applied to a Source-space vector.
        /// Matches AngleMatrix in mathlib: yaw around z, pitch around y, roll around x.
        /// </summary>
        public static Vector3 RotateSource(Vector3 v, Vector3 angles)
        {
            if (angles == Vector3.Zero) return v;
            float d = (float)(Math.PI / 180);
            float sp = (float)Math.Sin(angles.X * d), cp = (float)Math.Cos(angles.X * d);
            float sy = (float)Math.Sin(angles.Y * d), cy = (float)Math.Cos(angles.Y * d);
            float sr = (float)Math.Sin(angles.Z * d), cr = (float)Math.Cos(angles.Z * d);
            // Columns: forward, left, up
            var f = new Vector3(cp * cy, cp * sy, -sp);
            var l = new Vector3(sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, sr * cp);
            var u = new Vector3(cr * sp * cy + sr * sy, cr * sp * sy - sr * cy, cr * cp);
            return f * v.X + l * v.Y + u * v.Z;
        }
    }
}
