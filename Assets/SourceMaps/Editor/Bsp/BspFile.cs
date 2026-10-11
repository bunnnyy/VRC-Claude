using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace SourceMaps.Bsp
{
    /// <summary>
    /// Minimal reader for Source engine BSP files (VBSP version 19-21, CS:S maps are 20).
    /// Reads only what the importer needs: entities, brushes (collision), brush models,
    /// displacements and texture names. Plain C# (System.Numerics), no Unity, so it can be tested in .NET.
    /// Coordinates stay in Source space (x forward, y left, z up, units); BspGeometry converts to Unity.
    /// Layouts follow bspfile.h from the Source SDK 2013.
    /// </summary>
    public class BspFile
    {
        // Lump indices (bspfile.h)
        const int LumpEntities = 0, LumpPlanes = 1, LumpTexData = 2, LumpVertexes = 3, LumpNodes = 5,
            LumpTexInfo = 6, LumpFaces = 7, LumpLeafs = 10, LumpEdges = 12, LumpSurfEdges = 13, LumpModels = 14,
            LumpLeafBrushes = 17, LumpBrushes = 18, LumpBrushSides = 19, LumpDispInfo = 26, LumpDispVerts = 33,
            LumpTexDataStringData = 43, LumpTexDataStringTable = 44,
            LumpWorldLights = 15, LumpWorldLightsHdr = 54, LumpLeafAmbientIndexHdr = 51, LumpLeafAmbientIndex = 52, LumpLeafAmbientLightingHdr = 55, LumpLeafAmbientLighting = 56;

        // Brush contents flags (bspflags.h)
        public const int ContentsSolid = 0x1, ContentsWindow = 0x2, ContentsGrate = 0x8, ContentsSlime = 0x10,
            ContentsWater = 0x20, ContentsPlayerClip = 0x10000, ContentsLadder = 0x20000000;
        /// <summary>What blocks a player (MASK_PLAYERSOLID without CONTENTS_MONSTER).</summary>
        public const int MaskPlayerSolid = ContentsSolid | ContentsWindow | ContentsGrate | ContentsPlayerClip;

        // Surface flags (bspflags.h)
        public const int SurfSky = 0x4, SurfNoDraw = 0x80;

        public struct Plane { public Vector3 Normal; public float Dist; }
        public struct Brush { public int FirstSide, NumSides, Contents; }
        public struct BrushSide { public int Plane, TexInfo, DispInfo; public bool Bevel; }
        public struct Model { public Vector3 Mins, Maxs, Origin; public int HeadNode, FirstFace, NumFaces; }
        public struct Node { public int Plane, Child0, Child1; }
        public struct Leaf { public int Contents, FirstLeafBrush, NumLeafBrushes; public Vector3 Mins, Maxs; }
        /// <summary>Ambient light sample: 6 linear colours (+x, -x, +y, -y, +z, -z, Source axes) at a point.</summary>
        public struct AmbientSample { public Vector3[] Cube; public Vector3 Position; }
        public struct TexInfo { public int Flags, TexData; }
        public struct Face { public int Plane, Side, FirstEdge, NumEdges, TexInfo, DispInfo; }
        public struct DispInfo { public Vector3 StartPosition; public int DispVertStart, Power, Contents, MapFace; }
        public struct DispVert { public Vector3 Vec; public float Dist; }
        /// <summary>A static prop (prop_static, compiled into the game lump): model path, placement, solidity.</summary>
        public struct StaticProp { public string Model; public Vector3 Origin, Angles; public int Solid; }

        public int Version;
        public List<Entity> Entities = new List<Entity>();
        public Plane[] Planes;
        public Brush[] Brushes;
        public BrushSide[] BrushSides;
        public Model[] Models;
        public Node[] Nodes;
        public Leaf[] Leafs;
        /// <summary>Per leaf: the ambient light samples VRAD stored in it (BSP 20+; empty for solid leaves).</summary>
        public AmbientSample[][] LeafAmbient;
        /// <summary>A light VRAD compiled (point, spot, sun...), as the engine uses it to light models.</summary>
        public struct WorldLight
        {
            public const int Surface = 0, Point = 1, Spot = 2, Sky = 3, Quake = 4, SkyAmbient = 5;
            public Vector3 Origin, Intensity, Normal;
            public int Type;
            public float StopDot, StopDot2, Exponent, Radius, Constant, Linear, Quadratic;
        }
        public WorldLight[] WorldLights = new WorldLight[0];
        public ushort[] LeafBrushes;
        public TexInfo[] TexInfos;
        public string[] TexDataNames;
        public Vector3[] Vertexes;
        public int[] SurfEdges;
        public int[] EdgeVerts; // two per edge
        public Face[] Faces;
        public DispInfo[] DispInfos;
        public DispVert[] DispVerts;
        public List<StaticProp> StaticProps = new List<StaticProp>();

        public static BspFile Load(string path)
        {
            using (var stream = File.OpenRead(path))
                return Load(stream);
        }

        public static BspFile Load(Stream stream)
        {
            var bsp = new BspFile();
            var r = new BinaryReader(stream);
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "VBSP") throw new InvalidDataException("Not a Source BSP (no VBSP header)");
            bsp.Version = r.ReadInt32();
            if (bsp.Version < 19 || bsp.Version > 21) throw new InvalidDataException("Unsupported BSP version " + bsp.Version);

            var offsets = new int[64];
            var lengths = new int[64];
            var versions = new int[64];
            for (int i = 0; i < 64; i++)
            {
                offsets[i] = r.ReadInt32();
                lengths[i] = r.ReadInt32();
                versions[i] = r.ReadInt32();
                r.ReadInt32(); // fourCC (uncompressed size when LZMA compressed)
            }

            Func<int, BinaryReader> lump = i =>
            {
                stream.Position = offsets[i];
                byte[] data = r.ReadBytes(lengths[i]);
                if (data.Length >= 4 && data[0] == 'L' && data[1] == 'Z' && data[2] == 'M' && data[3] == 'A')
                    throw new InvalidDataException("LZMA compressed lumps are not supported (lump " + i + ")");
                return new BinaryReader(new MemoryStream(data));
            };
            Func<int, int, int> count = (i, size) => lengths[i] / size;

            using (var br = lump(LumpEntities))
                bsp.Entities = Entity.ParseAll(Encoding.UTF8.GetString(br.ReadBytes(lengths[LumpEntities])));

            using (var br = lump(LumpPlanes))
            {
                bsp.Planes = new Plane[count(LumpPlanes, 20)];
                for (int i = 0; i < bsp.Planes.Length; i++)
                {
                    bsp.Planes[i] = new Plane { Normal = ReadVector(br), Dist = br.ReadSingle() };
                    br.ReadInt32(); // type
                }
            }

            using (var br = lump(LumpBrushes))
            {
                bsp.Brushes = new Brush[count(LumpBrushes, 12)];
                for (int i = 0; i < bsp.Brushes.Length; i++)
                    bsp.Brushes[i] = new Brush { FirstSide = br.ReadInt32(), NumSides = br.ReadInt32(), Contents = br.ReadInt32() };
            }

            using (var br = lump(LumpBrushSides))
            {
                bsp.BrushSides = new BrushSide[count(LumpBrushSides, 8)];
                for (int i = 0; i < bsp.BrushSides.Length; i++)
                    bsp.BrushSides[i] = new BrushSide
                    {
                        Plane = br.ReadUInt16(), TexInfo = br.ReadInt16(), DispInfo = br.ReadInt16(), Bevel = br.ReadInt16() != 0
                    };
            }

            using (var br = lump(LumpModels))
            {
                bsp.Models = new Model[count(LumpModels, 48)];
                for (int i = 0; i < bsp.Models.Length; i++)
                    bsp.Models[i] = new Model
                    {
                        Mins = ReadVector(br), Maxs = ReadVector(br), Origin = ReadVector(br),
                        HeadNode = br.ReadInt32(), FirstFace = br.ReadInt32(), NumFaces = br.ReadInt32()
                    };
            }

            using (var br = lump(LumpNodes))
            {
                bsp.Nodes = new Node[count(LumpNodes, 32)];
                for (int i = 0; i < bsp.Nodes.Length; i++)
                {
                    bsp.Nodes[i] = new Node { Plane = br.ReadInt32(), Child0 = br.ReadInt32(), Child1 = br.ReadInt32() };
                    br.ReadBytes(20); // mins, maxs, firstface, numfaces, area, padding
                }
            }

            using (var br = lump(LumpLeafs))
            {
                // Version 0 leafs (BSP 19) carry 24 bytes of ambient lighting; version 1 (BSP 20+) don't.
                int size = versions[LumpLeafs] == 0 && bsp.Version == 19 ? 56 : 32;
                bsp.Leafs = new Leaf[count(LumpLeafs, size)];
                for (int i = 0; i < bsp.Leafs.Length; i++)
                {
                    long start = br.BaseStream.Position;
                    int contents = br.ReadInt32();
                    br.ReadBytes(2 + 2); // cluster, area/flags
                    var mins = new Vector3(br.ReadInt16(), br.ReadInt16(), br.ReadInt16());
                    var maxs = new Vector3(br.ReadInt16(), br.ReadInt16(), br.ReadInt16());
                    br.ReadBytes(4); // firstleafface, numleaffaces
                    bsp.Leafs[i] = new Leaf { Contents = contents, FirstLeafBrush = br.ReadUInt16(), NumLeafBrushes = br.ReadUInt16(), Mins = mins, Maxs = maxs };
                    br.BaseStream.Position = start + size;
                }
            }

            // Ambient light samples per leaf (LDR, else HDR): index = (ushort count, ushort first) per leaf; a sample is
            // 6 ColorRGBExp32 (r, g, b, signed exponent) then x, y, z fractions of the leaf box (0-255) and a pad byte.
            // Older maps have no index: one cube (6 ColorRGBExp32) per leaf.
            bool ldr = lengths[LumpLeafAmbientLighting] > 0;
            int indexLump = ldr ? LumpLeafAmbientIndex : LumpLeafAmbientIndexHdr, lightLump = ldr ? LumpLeafAmbientLighting : LumpLeafAmbientLightingHdr;
            bsp.LeafAmbient = new AmbientSample[bsp.Leafs.Length][];
            if (lengths[indexLump] / 4 == bsp.Leafs.Length && lengths[lightLump] > 0)
            {
                byte[] light;
                using (var br = lump(lightLump)) light = br.ReadBytes(lengths[lightLump]);
                using (var br = lump(indexLump))
                    for (int i = 0; i < bsp.Leafs.Length; i++)
                    {
                        int n = br.ReadUInt16(), first = br.ReadUInt16();
                        var samples = new AmbientSample[n];
                        for (int k = 0; k < n; k++)
                        {
                            int at = (first + k) * 28;
                            var cube = new Vector3[6];
                            for (int f = 0; f < 6; f++)
                            {
                                float scale = (float)Math.Pow(2, (sbyte)light[at + f * 4 + 3]);
                                cube[f] = new Vector3(light[at + f * 4], light[at + f * 4 + 1], light[at + f * 4 + 2]) * scale;
                            }
                            var frac = new Vector3(light[at + 24], light[at + 25], light[at + 26]) / 255f;
                            var leaf = bsp.Leafs[i];
                            samples[k] = new AmbientSample { Cube = cube, Position = leaf.Mins + (leaf.Maxs - leaf.Mins) * frac };
                        }
                        bsp.LeafAmbient[i] = samples;
                    }
            }
            else if (lengths[indexLump] == 0 && lengths[lightLump] == bsp.Leafs.Length * 24)
            {
                // Older maps (vbsp before the index lumps): one cube per leaf, no position: the leaf's middle. Solid
                // leaves (inside walls, black) are left out.
                using (var br = lump(lightLump))
                    for (int i = 0; i < bsp.Leafs.Length; i++)
                    {
                        var cube = new Vector3[6];
                        for (int f = 0; f < 6; f++)
                        {
                            byte cr = br.ReadByte(), cg = br.ReadByte(), cb = br.ReadByte();
                            float scale = (float)Math.Pow(2, br.ReadSByte());
                            cube[f] = new Vector3(cr, cg, cb) * scale;
                        }
                        var leaf = bsp.Leafs[i];
                        bsp.LeafAmbient[i] = (leaf.Contents & ContentsSolid) != 0 ? new AmbientSample[0]
                            : new[] { new AmbientSample { Cube = cube, Position = (leaf.Mins + leaf.Maxs) * 0.5f } };
                    }
            }

            // World lights (LDR, else HDR), dworldlight_t: 88 bytes (version 0), 100 with a shadow offset (version 1).
            int wl = lengths[LumpWorldLights] > 0 ? LumpWorldLights : LumpWorldLightsHdr;
            int wlSize = versions[wl] >= 1 ? 100 : 88;
            using (var br = lump(wl))
            {
                bsp.WorldLights = new WorldLight[count(wl, wlSize)];
                for (int i = 0; i < bsp.WorldLights.Length; i++)
                {
                    long start = br.BaseStream.Position;
                    var w = new WorldLight { Origin = ReadVector(br), Intensity = ReadVector(br), Normal = ReadVector(br) };
                    if (wlSize == 100) ReadVector(br); // shadow cast offset
                    br.ReadInt32(); // cluster
                    w.Type = br.ReadInt32();
                    br.ReadInt32(); // style
                    w.StopDot = br.ReadSingle(); w.StopDot2 = br.ReadSingle(); w.Exponent = br.ReadSingle(); w.Radius = br.ReadSingle();
                    w.Constant = br.ReadSingle(); w.Linear = br.ReadSingle(); w.Quadratic = br.ReadSingle();
                    bsp.WorldLights[i] = w;
                    br.BaseStream.Position = start + wlSize;
                }
            }

            using (var br = lump(LumpLeafBrushes))
            {
                bsp.LeafBrushes = new ushort[count(LumpLeafBrushes, 2)];
                for (int i = 0; i < bsp.LeafBrushes.Length; i++) bsp.LeafBrushes[i] = br.ReadUInt16();
            }

            using (var br = lump(LumpTexInfo))
            {
                bsp.TexInfos = new TexInfo[count(LumpTexInfo, 72)];
                for (int i = 0; i < bsp.TexInfos.Length; i++)
                {
                    br.ReadBytes(64); // texture and lightmap vectors
                    bsp.TexInfos[i] = new TexInfo { Flags = br.ReadInt32(), TexData = br.ReadInt32() };
                }
            }

            // Texture names: texdata -> string table -> string data
            byte[] stringData;
            using (var br = lump(LumpTexDataStringData)) stringData = br.ReadBytes(lengths[LumpTexDataStringData]);
            var stringTable = new int[count(LumpTexDataStringTable, 4)];
            using (var br = lump(LumpTexDataStringTable))
                for (int i = 0; i < stringTable.Length; i++) stringTable[i] = br.ReadInt32();
            using (var br = lump(LumpTexData))
            {
                bsp.TexDataNames = new string[count(LumpTexData, 32)];
                for (int i = 0; i < bsp.TexDataNames.Length; i++)
                {
                    br.ReadBytes(12); // reflectivity
                    int id = br.ReadInt32();
                    br.ReadBytes(16); // width, height, view width, view height
                    int start = stringTable[id], end = start;
                    while (end < stringData.Length && stringData[end] != 0) end++;
                    bsp.TexDataNames[i] = Encoding.ASCII.GetString(stringData, start, end - start);
                }
            }

            using (var br = lump(LumpVertexes))
            {
                bsp.Vertexes = new Vector3[count(LumpVertexes, 12)];
                for (int i = 0; i < bsp.Vertexes.Length; i++) bsp.Vertexes[i] = ReadVector(br);
            }

            using (var br = lump(LumpEdges))
            {
                bsp.EdgeVerts = new int[count(LumpEdges, 4) * 2];
                for (int i = 0; i < bsp.EdgeVerts.Length; i++) bsp.EdgeVerts[i] = br.ReadUInt16();
            }

            using (var br = lump(LumpSurfEdges))
            {
                bsp.SurfEdges = new int[count(LumpSurfEdges, 4)];
                for (int i = 0; i < bsp.SurfEdges.Length; i++) bsp.SurfEdges[i] = br.ReadInt32();
            }

            using (var br = lump(LumpFaces))
            {
                bsp.Faces = new Face[count(LumpFaces, 56)];
                for (int i = 0; i < bsp.Faces.Length; i++)
                {
                    var f = new Face { Plane = br.ReadUInt16(), Side = br.ReadByte() };
                    br.ReadByte(); // onNode
                    f.FirstEdge = br.ReadInt32();
                    f.NumEdges = br.ReadInt16();
                    f.TexInfo = br.ReadInt16();
                    f.DispInfo = br.ReadInt16();
                    br.ReadBytes(56 - 14); // fog volume, styles, lightmap info, area, primitives, smoothing groups
                    bsp.Faces[i] = f;
                }
            }

            using (var br = lump(LumpDispInfo))
            {
                bsp.DispInfos = new DispInfo[count(LumpDispInfo, 176)];
                for (int i = 0; i < bsp.DispInfos.Length; i++)
                {
                    var d = new DispInfo { StartPosition = ReadVector(br), DispVertStart = br.ReadInt32() };
                    br.ReadInt32(); // DispTriStart
                    d.Power = br.ReadInt32();
                    br.ReadInt32(); // minTess
                    br.ReadSingle(); // smoothingAngle
                    d.Contents = br.ReadInt32();
                    d.MapFace = br.ReadUInt16();
                    br.ReadBytes(176 - 38); // lightmap alpha, sample positions, neighbours, allowed verts
                    bsp.DispInfos[i] = d;
                }
            }

            using (var br = lump(LumpDispVerts))
            {
                bsp.DispVerts = new DispVert[count(LumpDispVerts, 20)];
                for (int i = 0; i < bsp.DispVerts.Length; i++)
                {
                    bsp.DispVerts[i] = new DispVert { Vec = ReadVector(br), Dist = br.ReadSingle() };
                    br.ReadSingle(); // alpha
                }
            }

            ReadStaticProps(bsp, r, offsets[35]);
            return bsp;
        }

        /// <summary>
        /// Static props from the game lump's "sprp" entry: model names, leaf list, then one record per prop whose size
        /// depends on the version. All versions start with origin, angles, prop type (model index), first leaf, leaf
        /// count, solid (0 = not solid, 2 = bounding box, 6 = vphysics).
        /// </summary>
        static void ReadStaticProps(BspFile bsp, BinaryReader r, int gameLumpOffset)
        {
            var stream = r.BaseStream;
            stream.Position = gameLumpOffset;
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32();
                r.ReadUInt16(); // flags
                r.ReadUInt16(); // version
                int offset = r.ReadInt32(), length = r.ReadInt32();
                if (id != ('s' << 24 | 'p' << 16 | 'r' << 8 | 'p')) continue;
                long save = stream.Position;
                if (offset <= 0 || offset + length > stream.Length) continue;
                stream.Position = offset;
                int nameCount = r.ReadInt32();
                if (nameCount < 0 || nameCount * 128L > length) { stream.Position = save; continue; }
                var names = new string[nameCount];
                for (int n = 0; n < names.Length; n++) names[n] = Encoding.ASCII.GetString(r.ReadBytes(128)).TrimEnd('\0');
                int leaves = r.ReadInt32(); // (not "Position += ReadInt32()": that reads Position before the read moves it)
                stream.Position += leaves * 2L;
                int props = r.ReadInt32();
                if (props <= 0 || props > length / 40) { stream.Position = save; continue; }
                long start = stream.Position;
                int size = (int)((offset + length - start) / props);
                for (int p = 0; p < props; p++)
                {
                    stream.Position = start + (long)p * size;
                    var prop = new StaticProp { Origin = ReadVector(r), Angles = ReadVector(r) };
                    int type = r.ReadUInt16();
                    r.ReadUInt16(); r.ReadUInt16(); // first leaf, leaf count
                    prop.Solid = r.ReadByte();
                    prop.Model = type < names.Length ? names[type] : "";
                    bsp.StaticProps.Add(prop);
                }
                stream.Position = save;
            }
        }

        static Vector3 ReadVector(BinaryReader br) { return new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()); }

        /// <summary>The world leaf containing a point (Source space).</summary>
        public int LeafAt(Vector3 p)
        {
            int node = Models[0].HeadNode;
            while (node >= 0)
            {
                var plane = Planes[Nodes[node].Plane];
                node = Vector3.Dot(plane.Normal, p) - plane.Dist >= 0 ? Nodes[node].Child0 : Nodes[node].Child1;
            }
            return -1 - node;
        }

        /// <summary>
        /// Source's ambient light at a point (what it lights models with): the nearest sample in the point's leaf. Null if
        /// the map has none there (no samples, or the point is in a solid leaf).
        /// </summary>
        public Vector3[] AmbientCube(Vector3 p)
        {
            if (LeafAmbient == null) return null;
            var samples = LeafAmbient[LeafAt(p)];
            if (samples == null || samples.Length == 0) return null;
            var best = samples[0];
            foreach (var s in samples)
                if (Vector3.DistanceSquared(s.Position, p) < Vector3.DistanceSquared(best.Position, p)) best = s;
            return best.Cube;
        }

        /// <summary>Ambient cube lighting for a direction n (unit, Source space), as Source's shaders sum it.</summary>
        public static Vector3 AmbientLight(Vector3[] cube, Vector3 n)
        {
            return n.X * n.X * cube[n.X >= 0 ? 0 : 1] + n.Y * n.Y * cube[n.Y >= 0 ? 2 : 3] + n.Z * n.Z * cube[n.Z >= 0 ? 4 : 5];
        }

        /// <summary>
        /// Light from a world light arriving at p (Source space): its colour, and the unit direction towards it. Zero for
        /// lights out of range or behind a spot's cone (like the engine's model lighting; no shadows here: the caller
        /// checks visibility). Surface and ambient-only lights give zero (they are in the ambient cubes).
        /// </summary>
        public static Vector3 LightAt(WorldLight w, Vector3 p, out Vector3 toLight)
        {
            toLight = -w.Normal;
            if (w.Type == WorldLight.Sky) return w.Intensity;
            if (w.Type != WorldLight.Point && w.Type != WorldLight.Spot) return Vector3.Zero;
            Vector3 d = w.Origin - p;
            float dist = d.Length();
            if (dist < 1f || (w.Radius > 0 && dist > w.Radius)) return Vector3.Zero;
            toLight = d / dist;
            float ratio = 1f / Math.Max(1e-6f, w.Constant + w.Linear * dist + w.Quadratic * dist * dist);
            if (w.Type == WorldLight.Spot)
            {
                float dot = Vector3.Dot(-toLight, w.Normal);
                if (dot <= w.StopDot2) return Vector3.Zero;
                if (dot < w.StopDot && w.StopDot > w.StopDot2)
                    ratio *= (float)Math.Pow((dot - w.StopDot2) / (w.StopDot - w.StopDot2), w.Exponent);
            }
            return w.Intensity * ratio;
        }

        /// <summary>True if p is inside a brush with a sky face (what a ray towards the sun hits when it gets out).</summary>
        public bool InSkyBrush(Vector3 p)
        {
            var leaf = Leafs[LeafAt(p)];
            for (int i = 0; i < leaf.NumLeafBrushes; i++)
            {
                var brush = Brushes[LeafBrushes[leaf.FirstLeafBrush + i]];
                bool inside = true, sky = false;
                for (int sIdx = 0; sIdx < brush.NumSides; sIdx++)
                {
                    var side = BrushSides[brush.FirstSide + sIdx];
                    var plane = Planes[side.Plane];
                    if (Vector3.Dot(plane.Normal, p) - plane.Dist > 0.5f) { inside = false; break; }
                    if (side.TexInfo >= 0 && (TexInfos[side.TexInfo].Flags & SurfSky) != 0) sky = true;
                }
                if (inside && sky) return true;
            }
            return false;
        }

        /// <summary>Brush indices of a brush model (0 = world), found by walking its BSP tree to the leaves.</summary>
        public List<int> ModelBrushes(int model)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(Models[model].HeadNode);
            while (stack.Count > 0)
            {
                int node = stack.Pop();
                if (node < 0)
                {
                    var leaf = Leafs[-1 - node];
                    for (int i = 0; i < leaf.NumLeafBrushes; i++)
                    {
                        int brush = LeafBrushes[leaf.FirstLeafBrush + i];
                        if (seen.Add(brush)) result.Add(brush);
                    }
                }
                else
                {
                    stack.Push(Nodes[node].Child0);
                    stack.Push(Nodes[node].Child1);
                }
            }
            result.Sort();
            return result;
        }

        /// <summary>Corner positions of a face, in winding order.</summary>
        public Vector3[] FaceVertices(int face)
        {
            var f = Faces[face];
            var verts = new Vector3[f.NumEdges];
            for (int i = 0; i < f.NumEdges; i++)
            {
                int se = SurfEdges[f.FirstEdge + i];
                verts[i] = Vertexes[se >= 0 ? EdgeVerts[se * 2] : EdgeVerts[-se * 2 + 1]];
            }
            return verts;
        }
    }

    /// <summary>One entity from the entity lump. Keys can repeat (outputs like OnStartTouch), so they're kept as a list.</summary>
    public class Entity
    {
        public List<KeyValuePair<string, string>> Pairs = new List<KeyValuePair<string, string>>();

        public string ClassName { get { return Get("classname"); } }
        public string TargetName { get { return Get("targetname"); } }

        public string Get(string key, string fallback = "")
        {
            foreach (var kv in Pairs)
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return fallback;
        }

        public int GetInt(string key, int fallback = 0)
        {
            int v;
            return int.TryParse(Get(key), out v) ? v : fallback;
        }

        /// <summary>"x y z" key as a vector (origin, angles), zero if missing.</summary>
        public Vector3 GetVector(string key)
        {
            var parts = Get(key).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var v = new float[3];
            for (int i = 0; i < 3 && i < parts.Length; i++)
                float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i]);
            return new Vector3(v[0], v[1], v[2]);
        }

        /// <summary>Brush model index from "model" "*N", or -1 for point entities.</summary>
        public int BrushModel
        {
            get
            {
                string m = Get("model");
                int n;
                return m.StartsWith("*") && int.TryParse(m.Substring(1), out n) ? n : -1;
            }
        }

        /// <summary>One output ("OnStartTouch" -> target, input, parameter, delay, times).</summary>
        public struct Output { public string Event, Target, Input, Parameter; public float Delay; public int Times; }

        /// <summary>
        /// Outputs: keys starting with "On", values "target,input,parameter,delay,times" separated by ESC (newer
        /// compilers) or commas.
        /// </summary>
        public List<Output> Outputs()
        {
            var list = new List<Output>();
            foreach (var kv in Pairs)
            {
                if (!kv.Key.StartsWith("On", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = kv.Value.Split(kv.Value.IndexOf('\u001b') >= 0 ? '\u001b' : ',');
                if (parts.Length < 2) continue;
                var o = new Output { Event = kv.Key, Target = parts[0], Input = parts[1], Parameter = parts.Length > 2 ? parts[2] : "", Times = -1 };
                if (parts.Length > 3) float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out o.Delay);
                if (parts.Length > 4) int.TryParse(parts[4], out o.Times);
                list.Add(o);
            }
            return list;
        }

        public static List<Entity> ParseAll(string text)
        {
            var list = new List<Entity>();
            Entity current = null;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '{') { current = new Entity(); i++; }
                else if (c == '}') { if (current != null) list.Add(current); current = null; i++; }
                else if (c == '"' && current != null)
                {
                    string key = ReadQuoted(text, ref i);
                    while (i < text.Length && text[i] != '"' && text[i] != '}') i++;
                    if (i >= text.Length || text[i] == '}') break;
                    current.Pairs.Add(new KeyValuePair<string, string>(key, ReadQuoted(text, ref i)));
                }
                else i++;
            }
            return list;
        }

        static string ReadQuoted(string text, ref int i)
        {
            int start = ++i;
            while (i < text.Length && text[i] != '"') i++;
            string s = text.Substring(start, i - start);
            i++;
            return s;
        }
    }
}
