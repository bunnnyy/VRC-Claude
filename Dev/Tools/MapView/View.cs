using SourceMaps.Bsp; using System; using System.IO; using System.Linq; using System.Numerics; using System.Collections.Generic;
// Map tools for writing RouteRunner routes (Source units throughout):
//   dotnet run -- map.bsp out.ppm x0 y0 x1 y1 [unitsPerPixel [zlo zhi [x1 y1 s|e x2 y2 0]...]]
//       top-down view: floors (with open air above) shaded by height, fail teleports red outlines, small (exit)
//       teleports green, destinations and spawns blue, push/gravity triggers magenta, zones from the args
//   dotnet run -- route map.bsp zlo zhi sx sy ex ey [exitRadius [grid]]
//       a path from (sx, sy) to the exit over safe floors in the height band (not over teleports), jumping gaps up to
//       224 units, simplified: prints "x y z" lines for a route file (env ROUTE_OUTLINE=1: thin floors count too)
//   dotnet run -- blocks map.bsp zlo zhi sx sy ex ey [reach]
//       the bhop blocks (func_door) in the band chained from the start to the exit: from each, the nearest unvisited
//       block within reach (default 420) that gets closer to the exit; prints "x y z" (block tops) for a route file
//   dotnet run -- brushes map.bsp x y z r        solid brushes with sloped faces near a point, and displacements
//   dotnet run -- props map.bsp x0 y0 x1 y1      static props in an area
static class View
{
    static void Main(string[] a)
    {
        if (a[0] == "brushes") { Brushes(a); return; }
        if (a[0] == "route") { RouteFind(a); return; }
        if (a[0] == "blocks") { Blocks(a); return; }
        if (a[0] == "props")
        {
            var b = BspFile.Load(a[1]); float px0 = float.Parse(a[2]), py0 = float.Parse(a[3]), px1 = float.Parse(a[4]), py1 = float.Parse(a[5]);
            foreach (var g in b.StaticProps.Where(p => p.Origin.X >= px0 && p.Origin.X <= px1 && p.Origin.Y >= py0 && p.Origin.Y <= py1).GroupBy(p => p.Model))
                Console.WriteLine($"{g.Count()}x {g.Key} solid {g.First().Solid} z {g.Min(p => p.Origin.Z):F0}..{g.Max(p => p.Origin.Z):F0}");
            return;
        }
        var bsp = BspFile.Load(a[0]); string outp = a[1]; float px = 16f;
        float x0 = float.Parse(a[2]), y0 = float.Parse(a[3]), x1 = float.Parse(a[4]), y1 = float.Parse(a[5]);
        if (a.Length > 6) px = float.Parse(a[6]);
        float zlo = a.Length > 8 ? float.Parse(a[7]) : -1e9f, zhi = a.Length > 8 ? float.Parse(a[8]) : 1e9f;
        int W = (int)((x1 - x0) / px), H = (int)((y1 - y0) / px);
        var top = new float[W, H]; for (int i = 0; i < W; i++) for (int j = 0; j < H; j++) top[i, j] = float.NaN;
        var img = new byte[W * H * 3];
        float zmin = float.MaxValue, zmax = float.MinValue;
        var floors = new List<Vector3[]>();
        void AddModel(int model, Vector3 off = default)
        {
            foreach (int b in bsp.ModelBrushes(model))
            {
                if ((bsp.Brushes[b].Contents & BspFile.MaskPlayerSolid) == 0) continue;
                foreach (var poly in BspGeometry.BrushPolygons(bsp, b))
                {
                    var n = Vector3.Normalize(Vector3.Cross(poly[1] - poly[0], poly[2] - poly[0]));
                    if (n.Z < -0.7f) n = -n;
                    if (n.Z <= 0.7f) continue;
                    var c = new Vector3(poly.Average(p => p.X), poly.Average(p => p.Y), poly.Max(p => p.Z) + 16);
                    int leaf = bsp.LeafAt(c);
                    if (leaf < 0 || (bsp.Leafs[leaf].Contents & BspFile.ContentsSolid) != 0) continue; // roof: void above
                    if (poly.Max(p => p.Z) < zlo || poly.Max(p => p.Z) > zhi) continue;
                    floors.Add(poly.Select(p => p + off).ToArray());
                }
            }
        }
        AddModel(0);
        foreach (var e in bsp.Entities) if (e.ClassName.StartsWith("func_") && e.BrushModel > 0 && e.ClassName != "func_illusionary") AddModel(e.BrushModel, e.GetVector("origin"));
        foreach (var poly in floors)
        {
            float mx = poly.Min(p => p.X), Mx = poly.Max(p => p.X), my = poly.Min(p => p.Y), My = poly.Max(p => p.Y), z = poly.Max(p => p.Z);
            for (int i = Math.Max(0, (int)((mx - x0) / px)); i <= Math.Min(W - 1, (int)((Mx - x0) / px)); i++)
            for (int j = Math.Max(0, (int)((my - y0) / px)); j <= Math.Min(H - 1, (int)((My - y0) / px)); j++)
            {
                var q = new Vector2(x0 + (i + 0.5f) * px, y0 + (j + 0.5f) * px);
                if (!Inside(poly, q)) continue;
                if (float.IsNaN(top[i, j]) || z > top[i, j]) top[i, j] = z;
            }
        }
        foreach (var v in top) if (!float.IsNaN(v)) { zmin = Math.Min(zmin, v); zmax = Math.Max(zmax, v); }
        for (int i = 0; i < W; i++) for (int j = 0; j < H; j++)
        {
            int o = ((H - 1 - j) * W + i) * 3;
            if (float.IsNaN(top[i, j])) { img[o] = img[o + 1] = img[o + 2] = 20; continue; }
            float t = (top[i, j] - zmin) / Math.Max(1, zmax - zmin);
            img[o] = (byte)(60 + 150 * t); img[o + 1] = (byte)(60 + 150 * t); img[o + 2] = (byte)(60 + 150 * t);
        }
        void Box(Vector3 mn, Vector3 mxv, byte r, byte g, byte b, bool fill)
        {
            for (int i = Math.Max(0, (int)((mn.X - x0) / px)); i <= Math.Min(W - 1, (int)((mxv.X - x0) / px)); i++)
            for (int j = Math.Max(0, (int)((mn.Y - y0) / px)); j <= Math.Min(H - 1, (int)((mxv.Y - y0) / px)); j++)
            {
                bool edge = i == (int)((mn.X - x0) / px) || i == (int)((mxv.X - x0) / px) || j == (int)((mn.Y - y0) / px) || j == (int)((mxv.Y - y0) / px);
                if (!fill && !edge) continue;
                int o = ((H - 1 - j) * W + i) * 3;
                if (fill && !edge) { img[o] = (byte)((img[o] + r) / 2); img[o + 1] = (byte)((img[o + 1] + g) / 2); img[o + 2] = (byte)((img[o + 2] + b) / 2); }
                else { img[o] = r; img[o + 1] = g; img[o + 2] = b; }
            }
        }
        foreach (var e in bsp.Entities)
        {
            var o = e.GetVector("origin");
            bool inZ(float z) => z >= zlo - 200 && z <= zhi + 200;
            if (e.ClassName == "trigger_teleport" && e.BrushModel > 0) { var m = bsp.Models[e.BrushModel]; if (!inZ(m.Mins.Z + o.Z)) continue; var sz = m.Maxs - m.Mins; bool exit = Math.Min(sz.X, sz.Y) <= 24 || sz.X * sz.Y < 20000; Box(m.Mins + o, m.Maxs + o, exit ? (byte)0 : (byte)255, exit ? (byte)255 : (byte)40, 40, exit); }
            if ((e.ClassName == "info_teleport_destination" || e.ClassName == "info_player_counterterrorist") && inZ(o.Z)) { Box(o - new Vector3(px * 2), o + new Vector3(px * 2), 40, 120, 255, true); }
            if ((e.ClassName == "trigger_push" || e.ClassName == "trigger_gravity") && e.BrushModel > 0) { var m = bsp.Models[e.BrushModel]; if (inZ(m.Mins.Z + o.Z)) Box(m.Mins + o, m.Maxs + o, 255, 0, 255, false); }
        }
        for (int k = 9; k + 6 <= a.Length; k += 6)
            Box(new Vector3(float.Parse(a[k]), float.Parse(a[k + 1]), 0), new Vector3(float.Parse(a[k + 3]), float.Parse(a[k + 4]), 0), a[k + 2] == "s" ? (byte)0 : (byte)255, 255, 0, true);
        using var f = File.Create(outp);
        var hdr = System.Text.Encoding.ASCII.GetBytes($"P6\n{W} {H}\n255\n"); f.Write(hdr); f.Write(img);
        Console.WriteLine($"{W}x{H}, floors {floors.Count}, z {zmin}..{zmax}");
    }
    // brushes bsp x y z r: solid world brushes with a sloped face (0.2 < nz < 0.8) within r of the point, their faces
    static void Brushes(string[] a)
    {
        var bsp = BspFile.Load(a[1]);
        var c = new Vector3(float.Parse(a[2]), float.Parse(a[3]), float.Parse(a[4])); float r = float.Parse(a[5]);
        foreach (int b in bsp.ModelBrushes(0))
        {
            if ((bsp.Brushes[b].Contents & BspFile.MaskPlayerSolid) == 0) continue;
            var faces = BspGeometry.BrushFaces(bsp, b);
            var all = faces.SelectMany(f => f.Key).ToList();
            if (all.Count == 0) continue;
            var mn = new Vector3(all.Min(p => p.X), all.Min(p => p.Y), all.Min(p => p.Z)); var mx = new Vector3(all.Max(p => p.X), all.Max(p => p.Y), all.Max(p => p.Z));
            var near = Vector3.Clamp(c, mn, mx);
            if ((near - c).Length() > r) continue;
            if (!faces.Any(f => Math.Abs(f.Value.Normal.Z) > 0.2f && Math.Abs(f.Value.Normal.Z) < 0.8f)) continue;
            Console.WriteLine($"brush {b} contents {bsp.Brushes[b].Contents:X} bounds {mn} - {mx}");
            foreach (var f in faces) Console.WriteLine($"   n ({f.Value.Normal.X:F3},{f.Value.Normal.Y:F3},{f.Value.Normal.Z:F3}) d {f.Value.Dist:F1} verts {f.Key.Length}: {string.Join(" ", f.Key.Select(p => $"({p.X:F0},{p.Y:F0},{p.Z:F0})"))}");
        }
        for (int d = 0; d < bsp.DispInfos.Length; d++)
        {
            var m = new MeshData();
            BspGeometry.AddDisplacement(m, bsp, d, 1f);
            var pts = m.Vertices.Select(v => new Vector3(v.Z, -v.X, v.Y)).ToList(); // back to Source axes
            var mn = new Vector3(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z)); var mx = new Vector3(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z));
            if ((Vector3.Clamp(c, mn, mx) - c).Length() > r) continue;
            Console.WriteLine($"disp {d}: bounds {mn} - {mx}");
        }
    }

    // route bsp zlo zhi sx sy ex ey [exitRadius]: a path over safe floors (not over fail teleports) from the start to the
    // exit, with jumps over gaps up to 224 u, simplified; prints "x y" lines.
    static void RouteFind(string[] a)
    {
        var bsp = BspFile.Load(a[1]);
        float zlo = float.Parse(a[2]), zhi = float.Parse(a[3]);
        var S = new Vector2(float.Parse(a[4]), float.Parse(a[5])); var E = new Vector2(float.Parse(a[6]), float.Parse(a[7]));
        float exitR = a.Length > 8 ? float.Parse(a[8]) : 64f;
        float G = a.Length > 9 ? float.Parse(a[9]) : 32f;
        int J = (int)(224f / G); // jump reach in cells
        bool outline = Environment.GetEnvironmentVariable("ROUTE_OUTLINE") == "1";
        var floors = new List<Vector3[]>();
        void AddModel(int model, Vector3 o)
        {
            foreach (int b in bsp.ModelBrushes(model))
            {
                if ((bsp.Brushes[b].Contents & BspFile.MaskPlayerSolid) == 0) continue;
                foreach (var poly in BspGeometry.BrushPolygons(bsp, b))
                {
                    var n = Vector3.Normalize(Vector3.Cross(poly[1] - poly[0], poly[2] - poly[0]));
                    if (n.Z < -0.7f) n = -n;
                    if (n.Z <= 0.7f) continue;
                    float z = poly.Max(p => p.Z) + o.Z;
                    if (z < zlo || z > zhi) continue;
                    var c = new Vector3(poly.Average(p => p.X) + o.X, poly.Average(p => p.Y) + o.Y, z + 16);
                    int leaf = bsp.LeafAt(c);
                    if (leaf < 0 || (bsp.Leafs[leaf].Contents & BspFile.ContentsSolid) != 0) continue; // solid above: not a floor
                    floors.Add(poly.Select(p => p + o).ToArray());
                }
            }
        }
        AddModel(0, Vector3.Zero);
        for (int d = 0; d < bsp.DispInfos.Length; d++) // displacements (terrain): their upward triangles
        {
            var dm = new MeshData();
            BspGeometry.AddDisplacement(dm, bsp, d, 1f);
            for (int t = 0; t + 2 < dm.Triangles.Count; t += 3)
            {
                var tri = new[] { dm.Triangles[t], dm.Triangles[t + 1], dm.Triangles[t + 2] }.Select(k => new Vector3(dm.Vertices[k].Z, -dm.Vertices[k].X, dm.Vertices[k].Y)).ToArray();
                var n = Vector3.Cross(tri[1] - tri[0], tri[2] - tri[0]);
                if (n.Length() < 1e-3) continue;
                n = Vector3.Normalize(n);
                if (Math.Abs(n.Z) <= 0.7f) continue;
                float z = tri.Max(p => p.Z);
                if (z < zlo || z > zhi) continue;
                floors.Add(tri);
            }
        }
        foreach (var e in bsp.Entities) if (e.ClassName.StartsWith("func_") && e.BrushModel > 0 && e.ClassName != "func_illusionary") AddModel(e.BrushModel, e.GetVector("origin"));
        var tps = new List<(Vector3 mn, Vector3 mx)>();
        foreach (var e in bsp.Entities)
            if (e.ClassName == "trigger_teleport" && e.BrushModel > 0) { var m = bsp.Models[e.BrushModel]; var o = e.GetVector("origin"); tps.Add((m.Mins + o, m.Maxs + o)); }
        float x0 = Math.Min(S.X, E.X) - 3000, y0 = Math.Min(S.Y, E.Y) - 3000, x1 = Math.Max(S.X, E.X) + 3000, y1 = Math.Max(S.Y, E.Y) + 3000;
        int W = (int)((x1 - x0) / G), H = (int)((y1 - y0) / G);
        var top = new float[W, H];
        for (int i = 0; i < W; i++) for (int j = 0; j < H; j++) top[i, j] = float.NaN;
        foreach (var poly in floors)
        {
            float mx = poly.Min(p => p.X), Mx = poly.Max(p => p.X), my = poly.Min(p => p.Y), My = poly.Max(p => p.Y), z = poly.Max(p => p.Z);
            for (int i = Math.Max(0, (int)((mx - x0) / G)); i <= Math.Min(W - 1, (int)((Mx - x0) / G)); i++)
            for (int j = Math.Max(0, (int)((my - y0) / G)); j <= Math.Min(H - 1, (int)((My - y0) / G)); j++)
            {
                var q = new Vector2(x0 + (i + 0.5f) * G, y0 + (j + 0.5f) * G);
                if (!Inside(poly, q)) continue;
                if (float.IsNaN(top[i, j]) || z > top[i, j]) top[i, j] = z;
            }
            // Thin floors (planks) may miss every cell centre: also the cells their outline passes through.
            for (int k = 0; k < poly.Length && outline; k++)
            {
                var pa = poly[k]; var pb = poly[(k + 1) % poly.Length];
                int n = (int)((pb - pa).Length() / (G * 0.5f)) + 1;
                for (int t = 0; t <= n; t++)
                {
                    var q = pa + (pb - pa) * (t / (float)n);
                    int i = (int)((q.X - x0) / G), j = (int)((q.Y - y0) / G);
                    if (i < 0 || j < 0 || i >= W || j >= H) continue;
                    if (float.IsNaN(top[i, j]) || z > top[i, j]) top[i, j] = z;
                }
            }
        }
        bool InTp(float x, float y, float z) => tps.Any(t => x >= t.mn.X - 16 && x <= t.mx.X + 16 && y >= t.mn.Y - 16 && y <= t.mx.Y + 16 && z + 8 >= t.mn.Z && z <= t.mx.Z);
        var ok = new bool[W, H];
        for (int i = 0; i < W; i++) for (int j = 0; j < H; j++)
            ok[i, j] = !float.IsNaN(top[i, j]) && !InTp(x0 + (i + 0.5f) * G, y0 + (j + 0.5f) * G, top[i, j]);
        (int, int) Cell(Vector2 p) => ((int)((p.X - x0) / G), (int)((p.Y - y0) / G));
        var (si, sj) = Cell(S);
        // Dijkstra; jumps up to 7 cells (224 u) to a cell no more than 48 u higher.
        var dist = new float[W, H]; var prev = new int[W, H];
        for (int i = 0; i < W; i++) for (int j = 0; j < H; j++) { dist[i, j] = float.MaxValue; prev[i, j] = -1; }
        var pq = new PriorityQueue<(int, int), float>();
        dist[si, sj] = 0; pq.Enqueue((si, sj), 0);
        int ei = -1, ej = -1;
        while (pq.Count > 0)
        {
            pq.TryDequeue(out var c, out float d);
            var (ci, cj) = c;
            if (d > dist[ci, cj]) continue;
            var cp = new Vector2(x0 + (ci + 0.5f) * G, y0 + (cj + 0.5f) * G);
            if ((cp - E).Length() < exitR) { ei = ci; ej = cj; break; }
            for (int di = -J; di <= J; di++) for (int dj = -J; dj <= J; dj++)
            {
                if (di == 0 && dj == 0 || di * di + dj * dj > J * J) continue;
                int ni = ci + di, nj = cj + dj;
                if (ni < 0 || nj < 0 || ni >= W || nj >= H) continue;
                var np = new Vector2(x0 + (ni + 0.5f) * G, y0 + (nj + 0.5f) * G);
                bool goal = (np - E).Length() < exitR;
                if (!ok[ni, nj] && !goal) continue;
                float here = float.IsNaN(top[ci, cj]) ? zlo : top[ci, cj], there = float.IsNaN(top[ni, nj]) ? here : top[ni, nj];
                if (there - here > 48) continue;
                bool clear = true; // no wall (a column without any floor in the band) under the flight
                int steps = Math.Max(Math.Abs(di), Math.Abs(dj));
                for (int k = 1; k < steps && clear; k++)
                {
                    int ki = ci + (int)Math.Round(di * k / (float)steps), kj = cj + (int)Math.Round(dj * k / (float)steps);
                    if (float.IsNaN(top[ki, kj]) || top[ki, kj] > Math.Max(here, there) + 40) clear = false;
                }
                if (!clear) continue;
                bool step = Math.Abs(di) <= 1 && Math.Abs(dj) <= 1;
                float cost = (float)Math.Sqrt(di * di + dj * dj) * G * (step ? 1f : 1.3f);
                if (d + cost < dist[ni, nj]) { dist[ni, nj] = d + cost; prev[ni, nj] = ci * H + cj; pq.Enqueue((ni, nj), d + cost); }
            }
        }
        if (ei < 0)
        {
            int vis = 0; float bd = float.MaxValue; Vector2 bp = default;
            for (int i = 0; i < W; i++) for (int j = 0; j < H; j++) if (dist[i, j] < float.MaxValue)
            { vis++; var q = new Vector2(x0 + (i + 0.5f) * G, y0 + (j + 0.5f) * G); if ((q - E).Length() < bd) { bd = (q - E).Length(); bp = q; } }
            int okc = 0; foreach (var b in ok) if (b) okc++;
            Console.WriteLine($"# no path: {vis} cells reached of {okc} safe; closest {bp} ({bd:F0} from the exit); start cell safe {ok[si, sj]} top {top[si, sj]}");
            return;
        }
        var path = new List<Vector2>();
        for (int c = ei * H + ej; c >= 0; c = prev[c / H, c % H]) path.Add(new Vector2(x0 + (c / H + 0.5f) * G, y0 + (c % H + 0.5f) * G));
        path.Reverse();
        path[0] = S;
        // Simplified, but a straight piece may not cross a column without floor (a wall).
        bool Clear(Vector2 p0, Vector2 p1)
        {
            int n = (int)((p1 - p0).Length() / (G * 0.5f)) + 1;
            for (int k = 0; k <= n; k++)
            {
                var (qi, qj) = Cell(p0 + (p1 - p0) * (k / (float)n));
                if (qi < 0 || qj < 0 || qi >= W || qj >= H || float.IsNaN(top[qi, qj])) return false;
            }
            return true;
        }
        var simple = Simplify(path, 96f, Clear);
        Console.WriteLine($"# {path.Count} cells, length {dist[ei, ej]:F0}");
        foreach (var p in simple)
        {
            var (pi, pj) = Cell(p);
            float z = pi >= 0 && pj >= 0 && pi < W && pj < H && !float.IsNaN(top[pi, pj]) ? top[pi, pj] : float.NaN;
            // A point over a pit: the height of the nearest safe floor round it.
            for (int r = 1; r < 8 && float.IsNaN(z) | (pi >= 0 && pj >= 0 && pi < W && pj < H && !ok[pi, pj]); r++)
                for (int di = -r; di <= r; di++) for (int dj = -r; dj <= r; dj++)
                {
                    int qi = pi + di, qj = pj + dj;
                    if (qi >= 0 && qj >= 0 && qi < W && qj < H && ok[qi, qj]) { z = top[qi, qj]; goto found; }
                }
            found:
            Console.WriteLine($"{p.X:F0} {p.Y:F0} {z:F0}");
        }
    }

    static void Blocks(string[] a)
    {
        var bsp = BspFile.Load(a[1]);
        float zlo = float.Parse(a[2]), zhi = float.Parse(a[3]);
        var S = new Vector2(float.Parse(a[4]), float.Parse(a[5])); var E = new Vector2(float.Parse(a[6]), float.Parse(a[7]));
        float reach = a.Length > 8 ? float.Parse(a[8]) : 420f;
        var blocks = new List<Vector3>();
        foreach (var e in bsp.Entities)
        {
            if (e.ClassName != "func_door" || e.BrushModel <= 0) continue;
            var m = bsp.Models[e.BrushModel]; var o = e.GetVector("origin");
            var c = (m.Mins + m.Maxs) * 0.5f + o; float top = m.Maxs.Z + o.Z;
            if (top < zlo || top > zhi) continue;
            blocks.Add(new Vector3(c.X, c.Y, top));
        }
        var here = S; var used = new HashSet<int>();
        Console.WriteLine($"# {blocks.Count} blocks in the band");
        Console.WriteLine($"{S.X:F0} {S.Y:F0}");
        while (true)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < blocks.Count; i++)
            {
                if (used.Contains(i)) continue;
                var p = new Vector2(blocks[i].X, blocks[i].Y); float d = (p - here).Length();
                if (d > reach || (p - E).Length() > (here - E).Length() + 64) continue; // forward only (a little slack)
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) break;
            used.Add(best); here = new Vector2(blocks[best].X, blocks[best].Y);
            Console.WriteLine($"{blocks[best].X:F0} {blocks[best].Y:F0} {blocks[best].Z:F0}");
        }
        Console.WriteLine($"# last block {(here - E).Length():F0} from the exit");
        Console.WriteLine($"{E.X:F0} {E.Y:F0}");
    }

    static List<Vector2> Simplify(List<Vector2> pts, float tol, Func<Vector2, Vector2, bool> clear)
    {
        if (pts.Count < 3) return pts;
        int idx = -1; float best = 0;
        var A = pts[0]; var B = pts[^1]; var AB = B - A; float L = AB.Length();
        for (int i = 1; i < pts.Count - 1; i++)
        {
            float d = L < 1e-3 ? (pts[i] - A).Length() : Math.Abs(AB.X * (A.Y - pts[i].Y) - (A.X - pts[i].X) * AB.Y) / L;
            if (d > best) { best = d; idx = i; }
        }
        if (best <= tol && clear(A, B)) return new List<Vector2> { A, B };
        if (idx < 0) idx = pts.Count / 2;
        var left = Simplify(pts.GetRange(0, idx + 1), tol, clear); var right = Simplify(pts.GetRange(idx, pts.Count - idx), tol, clear);
        left.RemoveAt(left.Count - 1); left.AddRange(right); return left;
    }

    static bool Inside(Vector3[] poly, Vector2 q)
    {
        bool c = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Y > q.Y) != (poly[j].Y > q.Y) && q.X < (poly[j].X - poly[i].X) * (q.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) c = !c;
        return c;
    }
}
