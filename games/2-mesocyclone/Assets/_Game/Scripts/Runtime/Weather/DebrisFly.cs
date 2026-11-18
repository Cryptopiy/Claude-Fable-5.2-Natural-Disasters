using UnityEngine;
using UnityEngine.Rendering;

namespace MESOCYCLONE
{
    /// <summary>Beat 6: the hero pieces the wind throws through the headlights. A corrugated roof sheet (galvanised,
    /// rust runs, bent) cartwheels across the road just after the vortex has crossed it (9:05-9:10); planks, a strip
    /// of siding and a painted barn board come through in the gusts before. Scripted in story time, in the truck's
    /// frame, never touching the truck. The swarm of smaller debris around them is DebrisSwarm.</summary>
    [DefaultExecutionOrder(-80)]
    public sealed class DebrisFly : MonoBehaviour
    {
        struct Piece { public Transform t; public float t0, dur, z0, z1, y0, bounce, spin, side; }
        Piece[] pieces;

        public static DebrisFly Build(Transform truck)
        {
            var go = new GameObject("DebrisFly");
            go.transform.SetParent(truck, false);
            var d = go.AddComponent<DebrisFly>();
            Material wood = WeatherLibrary.Mat(WeatherLibrary.DebrisMat, "atlas");
            Material metal = WeatherLibrary.Mat(WeatherLibrary.DebrisMetalMat, "atlas_metal");
            // (start time, duration, z far, z near, height, bounce, spin, from side: +1 = from the right)
            float[,] def =
            {
                { 524.0f, 2.2f, 46f, 30f, 1.2f, 0.8f, 520f, 1f },
                { 528.6f, 1.8f, 38f, 26f, 0.6f, 0.5f, 700f, 1f },
                { 535.2f, 2.0f, 52f, 34f, 2.0f, 1.2f, 430f, 1f },
                { 540.4f, 1.6f, 30f, 22f, 0.5f, 0.4f, 800f, 1f },
                { 544.6f, 3.4f, 58f, 27f, 1.6f, 2.2f, 300f, 1f },      // the roof sheet
                { 546.9f, 1.9f, 40f, 28f, 0.8f, 0.6f, 620f, 1f },
                { 552.0f, 2.4f, 48f, 33f, 1.0f, 0.7f, 500f, 1f },
            };
            int n = def.GetLength(0);
            d.pieces = new Piece[n];
            Mesh sheetMesh = DebrisKit.RoofSheet();
            for (int i = 0; i < n; i++)
            {
                bool sheet = i == 4;
                Mesh mesh = sheet ? sheetMesh : BoardMesh(i);
                var o = new GameObject("Fly" + i);
                o.transform.SetParent(go.transform, false);
                o.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = o.AddComponent<MeshRenderer>();
                mr.sharedMaterial = sheet ? metal : wood;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                o.SetActive(false);
                d.pieces[i] = new Piece { t = o.transform, t0 = def[i, 0], dur = def[i, 1], z0 = def[i, 2], z1 = def[i, 3], y0 = def[i, 4], bounce = def[i, 5], spin = def[i, 6], side = def[i, 7] };
            }
            return d;
        }

        /// <summary>Boards of a torn barn and house: weathered planks, a red barn board, a strip of vinyl siding,
        /// each with real thickness and a ragged length.</summary>
        static Mesh BoardMesh(int i)
        {
            var rng = new Rng(4242u + (uint)i * 31u);
            var mb = new MeshBuilder();
            int kind = i % 3;
            if (kind == 2)
            {
                mb.uvRect = WeatherLibrary.Cell(WeatherLibrary.Siding);
                float half = rng.Range(0.6f, 1.2f);
                var prof = new[] { new Vector2(0.012f, -0.1f), new Vector2(0f, 0f), new Vector2(0.012f, 0f), new Vector2(0f, 0.1f) };
                mb.ExtrudeX(prof, -half, half, false);
            }
            else
            {
                mb.uvRect = WeatherLibrary.Cell(kind == 0 ? WeatherLibrary.Plank : WeatherLibrary.BarnBoard);
                float len = rng.Range(0.9f, 2.4f), thick = rng.Range(0.022f, 0.04f), wide = rng.Range(0.12f, 0.24f);
                mb.Box(Vector3.zero, new Vector3(len, thick, wide));
                // a splintered end: a short narrower piece still attached
                mb.Box(new Vector3(len * 0.5f + 0.08f, 0f, wide * 0.2f), new Vector3(0.16f, thick * 0.8f, wide * 0.45f));
            }
            Mesh m = mb.ToMesh("FlyBoard" + i);
            m.UploadMeshData(true);
            return m;
        }

        void LateUpdate()
        {
            if (!Director.Running || pieces == null) return;
            float T = Director.T;
            for (int i = 0; i < pieces.Length; i++)
            {
                Piece p = pieces[i];
                float u = (T - p.t0) / p.dur;
                bool on = u >= 0f && u <= 1f;
                if (p.t.gameObject.activeSelf != on) p.t.gameObject.SetActive(on);
                if (!on) continue;
                float x = Mathf.Lerp(22f * p.side, -24f * p.side, u);                       // right to left, with the wind
                float z = Mathf.Lerp(p.z0, p.z1, u);
                float hop = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 2.6f));
                float y = 0.25f + p.y0 * (0.4f + 0.6f * (1f - u)) + p.bounce * hop;
                p.t.localPosition = new Vector3(x, y, z);
                p.t.localRotation = Quaternion.Euler(u * p.spin * p.dur * 0.6f, 35f + u * 90f, u * p.spin * p.dur);
            }
        }
    }
}
