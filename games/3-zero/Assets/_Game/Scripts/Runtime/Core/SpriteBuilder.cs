using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZERO
{
    /// <summary>Camera-facing quads baked into chunk meshes and expanded in the vertex shader (shaders "Zero/Fire",
    /// "Zero/Smoke", "Zero/Mote", "Zero/Streak"). This is the game's particle system: deterministic, driven by
    /// _WorldTime, no simulation. Positions are in the builder's local space.</summary>
    public class SpriteBuilder
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Vtx
        {
            public Vector3 pos;                     // POSITION: sprite centre
            public Color32 color;                   // COLOR: rgb tint, a = opacity
            public ushort cx, cy, hw, hh;           // TEXCOORD0 (half4): corner (-1..1), half width, half height
            public ushort seed, param, rot, tRef;   // TEXCOORD1 (half4)
            public ushort vx, vy, vz, mode;         // TEXCOORD2 (half4): velocity (world m/s), mode 0 facing / 1 upright / 2 velocity-aligned
        }

        struct Sprite { public Vector3 p; public Color32 c; public float hw, hh, seed, param, rot, tRef, mode; public Vector3 vel; }
        class Cell { public List<Sprite> s = new List<Sprite>(1024); public Bounds b; public bool has; }

        readonly Dictionary<long, Cell> cells = new Dictionary<long, Cell>();
        readonly float cellSize; readonly string name;
        public int Count { get; private set; }

        static readonly VertexAttributeDescriptor[] layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float16, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float16, 4),
        };

        public SpriteBuilder(string name, float cellSize = 64f) { this.name = name; this.cellSize = cellSize; }

        /// <param name="mode">0 = faces the camera, 1 = upright (turns around the vertical axis), 2 = stretched along its velocity</param>
        public void Add(Vector3 pos, float halfW, float halfH, Color32 col, float seed, float param = 0f, float rot = 0f,
                        Vector3 vel = default, float mode = 0f, float tRef = 0f)
        {
            long kx = Mathf.FloorToInt(pos.x / cellSize), kz = Mathf.FloorToInt(pos.z / cellSize);
            long key = (kx << 32) ^ (kz & 0xffffffffL);
            if (!cells.TryGetValue(key, out var c)) { c = new Cell(); cells[key] = c; }
            c.s.Add(new Sprite { p = pos, c = col, hw = halfW, hh = halfH, seed = seed, param = param, rot = rot, tRef = tRef, mode = mode, vel = vel });
            var sb = new Bounds(pos, new Vector3(halfW, halfH, halfW) * 2.2f);
            if (!c.has) { c.b = sb; c.has = true; } else c.b.Encapsulate(sb);
            Count++;
        }

        public List<MeshRenderer> Build(Transform parent, Material mat, float motionMargin = 30f, int sortingOrder = 0)
        {
            var list = new List<MeshRenderer>();
            int idx = 0;
            foreach (var kv in cells)
            {
                var c = kv.Value; int n = c.s.Count;
                if (n == 0) continue;
                // far-to-near for a camera that walks toward +z
                c.s.Sort((a, b) => b.p.z.CompareTo(a.p.z));
                Vector3 org = new Vector3(Mathf.Round(c.b.center.x), Mathf.Round(c.b.center.y), Mathf.Round(c.b.center.z));
                var v = new Vtx[n * 4];
                bool big = n * 4 > 65000;
                var tri32 = big ? new int[n * 6] : null; var tri16 = big ? null : new ushort[n * 6];
                for (int i = 0; i < n; i++)
                {
                    var s = c.s[i];
                    for (int k = 0; k < 4; k++)
                    {
                        float cx = (k == 0 || k == 3) ? -1f : 1f, cy = k < 2 ? -1f : 1f;
                        v[i * 4 + k] = new Vtx
                        {
                            pos = s.p - org, color = s.c,
                            cx = Mathf.FloatToHalf(cx), cy = Mathf.FloatToHalf(cy), hw = Mathf.FloatToHalf(s.hw), hh = Mathf.FloatToHalf(s.hh),
                            seed = Mathf.FloatToHalf(s.seed), param = Mathf.FloatToHalf(s.param), rot = Mathf.FloatToHalf(s.rot), tRef = Mathf.FloatToHalf(s.tRef),
                            vx = Mathf.FloatToHalf(s.vel.x), vy = Mathf.FloatToHalf(s.vel.y), vz = Mathf.FloatToHalf(s.vel.z), mode = Mathf.FloatToHalf(s.mode),
                        };
                    }
                    int b = i * 4, t = i * 6;
                    if (big) { tri32[t] = b; tri32[t + 1] = b + 2; tri32[t + 2] = b + 1; tri32[t + 3] = b; tri32[t + 4] = b + 3; tri32[t + 5] = b + 2; }
                    else { tri16[t] = (ushort)b; tri16[t + 1] = (ushort)(b + 2); tri16[t + 2] = (ushort)(b + 1); tri16[t + 3] = (ushort)b; tri16[t + 4] = (ushort)(b + 3); tri16[t + 5] = (ushort)(b + 2); }
                }
                var mesh = new Mesh { name = name + "_" + idx };
                const MeshUpdateFlags F = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
                mesh.SetVertexBufferParams(v.Length, layout);
                mesh.SetVertexBufferData(v, 0, 0, v.Length, 0, F);
                mesh.SetIndexBufferParams(n * 6, big ? IndexFormat.UInt32 : IndexFormat.UInt16);
                if (big) mesh.SetIndexBufferData(tri32, 0, 0, n * 6, F); else mesh.SetIndexBufferData(tri16, 0, 0, n * 6, F);
                mesh.subMeshCount = 1;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, n * 6), MeshUpdateFlags.DontRecalculateBounds);
                var bb = c.b; bb.center -= org; bb.Expand(motionMargin * 2f); mesh.bounds = bb;
                mesh.UploadMeshData(true);

                var go = new GameObject(name + "_" + idx);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = org;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                r.sortingOrder = sortingOrder;
                list.Add(r); idx++;
            }
            cells.Clear();
            return list;
        }
    }
}
