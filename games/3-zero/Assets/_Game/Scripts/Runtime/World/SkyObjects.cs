using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZERO
{
    /// <summary>The luminous things that ride with the asteroid: the plasma sheath on its leading face, the trail it
    /// draws through the air, the glow around it. Far out in space it only shines like a star with a faint tail;
    /// from the entry into the air (120 km up, 32 s before the contact) it burns.</summary>
    public static class SkyObjects
    {
        public static Vector3 CentreAbs;            // centre of the body on the flat map
        static readonly float[] shellPower = { 6.5f, 3.4f, 1.8f };
        static Material[] shells; static Material trailMat, glowMat;
        static GameObject trailGo, glowGo;
        static readonly List<GameObject> shellGos = new List<GameObject>();

        public static void Build(Transform root)
        {
            Transform rig = AsteroidBuilder.Rig;
            shellGos.Clear();
            float[] inflate = { 9f, 34f, 95f };
            float[] fres = { 1.8f, 2.4f, 3.4f };
            float[] core = { 0.8f, 0.15f, 0.0f };
            shells = new Material[3];
            for (int i = 0; i < 3; i++)
            {
                var m = new Material(Ctx.A.Plasma) { name = "Plasma" + i };
                m.SetFloat("_Inflate", inflate[i]); m.SetFloat("_Power", shellPower[i]); m.SetFloat("_Fresnel", fres[i]); m.SetFloat("_Core", core[i]); m.SetFloat("_NearFade", 0f);
                m.renderQueue = 2980 + i;
                var go = Ctx.Child("PlasmaShell" + i, rig, Vector3.zero, Quaternion.identity);
                Ctx.Renderer(go, AsteroidBuilder.ShellMesh, m);
                shells[i] = m; shellGos.Add(go);
            }
            BuildTrail(rig);
            // the glow: what is seen first, a new star by day
            var glow = new SpriteBuilder("RigGlow", 1e7f);
            Color gold = Palette.PlasmaGold.linear, white = Palette.PlasmaWhite.linear;
            Vector3 p = AsteroidBuilder.TipLocal * 0.55f;
            glow.Add(p, 46000f, 46000f, new Color(gold.r, gold.g, gold.b, 0.07f), 0.3f, 0.0f);
            glow.Add(AsteroidBuilder.TipLocal * 0.8f, 15000f, 15000f, new Color(white.r, white.g, white.b, 0.2f), 0.6f, 0.35f);
            glow.Add(AsteroidBuilder.TipLocal, 7000f, 7000f, new Color(white.r, white.g, white.b, 0.5f), 0.9f, 0.7f);
            glowMat = new Material(Ctx.A.Glow) { name = "RigGlow" }; glowMat.SetFloat("_Pull", 60000f);
            foreach (var r in glow.Build(rig, glowMat, 0f)) { r.gameObject.layer = Ctx.LayerSky; r.GetComponent<MeshFilter>().sharedMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e7f); glowGo = r.gameObject; }
            Tick(0f);
        }

        /// <summary>Per frame: the rig follows the trajectory and drives into the ground; its lights follow the timetable.</summary>
        public static void Tick(float T)
        {
            Transform rig = AsteroidBuilder.Rig;
            if (rig == null) return;
            Vector3 tip = Schedule.AstTip(T);
            CentreAbs = tip - rig.localRotation * AsteroidBuilder.TipLocal * Schedule.AstScale;
            rig.localPosition = CentreAbs;
            float since = T - Schedule.ContactTime;
            float entry = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Schedule.EntryTime - 3f, Schedule.EntryTime + 12f, T));
            float star = Schedule.BodyGlow(T);
            bool bodyOn = since < 4.5f;
            if (AsteroidBuilder.Body != null && AsteroidBuilder.Body.gameObject.activeSelf != bodyOn) AsteroidBuilder.Body.gameObject.SetActive(bodyOn);
            for (int i = 0; i < shells.Length; i++)
            {
                shells[i].SetFloat("_Power", shellPower[i] * entry);
                bool on = entry > 0.002f && since < 7f;
                if (shellGos[i].activeSelf != on) shellGos[i].SetActive(on);
            }
            // the faint tail of the far body; from the entry on the burning wake (ImpactFX) takes its place
            trailMat.SetFloat("_Power", 4f * 0.05f * star * (1f - entry));
            bool trailOn = since < 160f;
            if (trailGo.activeSelf != trailOn) trailGo.SetActive(trailOn);
            glowMat.SetFloat("_Power", (2.6f * Mathf.Clamp01(1f - T / 150f) + 0.5f * star + 2.6f * entry) * (since > 0f ? Mathf.Clamp01(1f - since / 3f) : 1f));
            // the far light is a point in the sky: once a long lens resolves the rock it would only sit on its face like a smudge
            float lens = Bootstrap.I != null && Bootstrap.I.Cam != null ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 26f, Bootstrap.I.Cam.fieldOfView)) : 1f;
            glowMat.SetFloat("_Power", glowMat.GetFloat("_Power") * Mathf.Max(lens, entry));
            bool glowOn = since < 3f;
            if (glowGo != null && glowGo.activeSelf != glowOn) glowGo.SetActive(glowOn);
        }

        static void Tube(List<Vector3> V, List<Vector3> Nn, List<Vector4> U, List<int> T, System.Func<float, Vector3> centre, System.Func<float, float> radius, int sides, int segs, float layer, float seed)
        {
            int b = V.Count; Vector3 prevA = Vector3.right;
            for (int k = 0; k <= segs; k++)
            {
                float t = k / (float)segs;
                Vector3 c = centre(t);
                Vector3 dir = (centre(Mathf.Min(1f, t + 0.002f)) - centre(Mathf.Max(0f, t - 0.002f))).normalized;
                Vector3 a = (prevA - dir * Vector3.Dot(prevA, dir)).normalized; prevA = a;
                Vector3 bb = Vector3.Cross(dir, a);
                float r = radius(t);
                for (int i = 0; i <= sides; i++)
                {
                    float ang = i * Mathf.PI * 2f / sides;
                    Vector3 d = a * Mathf.Cos(ang) + bb * Mathf.Sin(ang);
                    V.Add(c + d * r); Nn.Add(d); U.Add(new Vector4(i / (float)sides, t, layer, seed));
                }
            }
            int ring = sides + 1;
            for (int k = 0; k < segs; k++)
                for (int i = 0; i < sides; i++)
                {
                    int p = b + k * ring + i;
                    T.Add(p); T.Add(p + ring + 1); T.Add(p + ring); T.Add(p); T.Add(p + 1); T.Add(p + ring + 1);
                }
        }

        /// <summary>The trail behind the body: a hairline core, three twisting strands and a soft sheath (in the rig's
        /// own units: 17 000 = 220 km at the scale of the body, the whole path through the air).</summary>
        static void BuildTrail(Transform rig)
        {
            const float z0 = 650f, z1 = 17000f;
            var V = new List<Vector3>(); var Nn = new List<Vector3>(); var U = new List<Vector4>(); var T = new List<int>();
            System.Func<float, float> R = t => Mathf.Lerp(440f, 70f, Mathf.Pow(t, 0.55f));
            System.Func<float, float> Z = t => Mathf.Lerp(z0, z1, t * t * 0.6f + t * 0.4f);
            Tube(V, Nn, U, T, t => new Vector3(0, 0, Z(t)), t => Mathf.Lerp(16f, 9f, t), 8, 96, 0f, 0.1f);
            for (int s = 0; s < 3; s++)
            {
                float ph = s * Mathf.PI * 2f / 3f; float sd = 0.2f + 0.3f * s; int k = s;
                Tube(V, Nn, U, T, t =>
                {
                    float z = Z(t); float a = ph + z / 5200f * Mathf.PI * 2f + 0.6f * Mathf.Sin(z / 1700f + k);
                    float rho = R(t) * (0.42f + 0.1f * Mathf.Sin(z / 900f + k * 2f));
                    return new Vector3(Mathf.Cos(a) * rho, Mathf.Sin(a) * rho, z);
                }, t => R(t) * (0.20f + 0.05f * Mathf.Sin(Z(t) / 600f + k)), 12, 160, 1f, sd);
            }
            Tube(V, Nn, U, T, t => new Vector3(0, 0, Z(t)), t => R(t) * 1.2f, 28, 96, 2f, 0.7f);
            var mesh = new Mesh { name = "Trail", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(V); mesh.SetNormals(Nn); mesh.SetUVs(0, U); mesh.SetTriangles(T, 0, true);
            mesh.bounds = new Bounds(new Vector3(0f, 0f, z1 * 0.5f), new Vector3(4000f, 4000f, z1 * 1.2f));
            mesh.UploadMeshData(true);
            trailGo = Ctx.Child("Trail", rig, Vector3.zero, Quaternion.identity);
            trailMat = new Material(Ctx.A.Trail) { name = "Trail" };
            Ctx.Renderer(trailGo, mesh, trailMat);
        }
    }
}
