using UnityEngine;
using UnityEngine.Rendering;

namespace MESOCYCLONE
{
    /// <summary>The truck cab: builds every part under the Cab transform and animates it - steering wheel, gauge
    /// needles, wipers, instrument backlight, the sky ambient inside the cab at dusk and the motion of the body around
    /// the driver's head. Runs after the director and before the camera, which reads the EyeAnchor under the same
    /// transform.</summary>
    [DefaultExecutionOrder(-600)]
    public sealed class CabRig : MonoBehaviour
    {
        public Renderer mirrorFace;                       // rear-view mirror glass (the mirror view is assigned by MirrorFx)
        public Renderer sideMirrorFaceL, sideMirrorFaceR; // outside mirror glasses
        public static float GaugeLight;                   // 0..1 instrument backlight

        /// <summary>Three seeded sines: smooth noise in -1..1 with no state.</summary>
        struct Wobble
        {
            float f1, f2, f3, p1, p2, p3;
            public Wobble(ref Rng r, float fMin, float fMax)
            {
                const float tau = Mathf.PI * 2f;
                f1 = r.Range(fMin, fMax) * tau; f2 = r.Range(fMin, fMax) * 1.37f * tau; f3 = r.Range(fMin, fMax) * 0.61f * tau;
                p1 = r.Range(0f, tau); p2 = r.Range(0f, tau); p3 = r.Range(0f, tau);
            }
            public float Eval(float t) { return (Mathf.Sin(t * f1 + p1) + 0.6f * Mathf.Sin(t * f2 + p2) + 0.4f * Mathf.Sin(t * f3 + p3)) * 0.5f; }
        }

        const float Wheelbase = 3.7f, SteerRatio = 16f;
        static readonly int idEmissive = Shader.PropertyToID("_EmissiveColor");
        static readonly int idAOMax = Shader.PropertyToID("_AORemapMax");

        Transform cab, wheel, wiperL, wiperR, nSpeed, nTach, nFuel, nTemp;
        Vector3 restPos;
        Quaternion restRot, planeRot, wiperRot;
        Material glowMat, greenMat, needleMat, radioMat;
        Material[] ambMats;                                // own instances of the photo materials whose sky ambient follows the dusk
        float ambK = -1f, ambMin;                          // the AO ceiling last written, and its night value (cab.ambient)
        Color glowBase, greenBase, needleBase, radioBase;
        Wobble wSteer, wGust, wBuffet, wRoadY, wRoadP, wRoadR, wFloatY, wFloatP, wFloatR, wTach, wFlick;
        float steer, vSpeed, vTach, vFuel, vTemp, prevSpeed, accel, dive, diveVel;
        bool primed;

        // ------------------------------------------------------------------------------------------------ build
        public static CabRig Build(Transform cab)
        {
            GameAssets a = GameAssets.I;
            var rig = cab.gameObject.AddComponent<CabRig>();
            rig.cab = cab; rig.restPos = cab.localPosition; rig.restRot = cab.localRotation;

            // own instances of the four animated emissive materials
            rig.glowMat = new Material(a.Mat("cab_gauge_glow"));
            rig.greenMat = new Material(a.Mat("cab_tell_green"));
            rig.needleMat = new Material(a.Mat("cab_needle"));
            rig.radioMat = new Material(a.Mat("cab_radio_glow"));
            rig.glowBase = Palette.DashAmber.linear * CabLibrary.GaugeNits;
            rig.greenBase = Palette.DashGreen.linear * CabLibrary.GreenNits;
            rig.needleBase = CabLibrary.NeedleColor.linear * CabLibrary.NeedleNits;
            rig.radioBase = Palette.DashAmber.linear * CabLibrary.RadioNits;

            // own instances of the seven mask-mapped photo materials: their AO ceiling scales the sky ambient at dusk
            // (see Update). Order matters: [0] dash, ..., [6] wheel
            string[] amb = { "cab_dash", "cab_pad", "cab_leather", "cab_fabric", "cab_liner", "cab_carpet", "cab_wheel" };
            rig.ambMats = new Material[amb.Length];
            for (int i = 0; i < amb.Length; i++) rig.ambMats[i] = new Material(a.Mat(amb[i]));
            rig.ambMin = Tune.F("cab.ambient", 0.4f);

            var mats = new Material[CabMesh.SlotCount];
            mats[CabMesh.Dash] = rig.ambMats[0]; mats[CabMesh.Pad] = rig.ambMats[1]; mats[CabMesh.Leather] = rig.ambMats[2];
            mats[CabMesh.Fabric] = rig.ambMats[3]; mats[CabMesh.Liner] = rig.ambMats[4]; mats[CabMesh.Carpet] = rig.ambMats[5];
            mats[CabMesh.Gloss] = a.Mat("cab_trim_gloss"); mats[CabMesh.Chrome] = a.Mat("cab_chrome"); mats[CabMesh.Paint] = a.Mat("cab_paint");
            mats[CabMesh.Rubber] = a.Mat("cab_rubber"); mats[CabMesh.Face] = a.Mat("cab_gauge_face"); mats[CabMesh.Plastic] = a.Mat("cab_plastic");
            mats[CabMesh.Glow] = rig.glowMat; mats[CabMesh.Green] = rig.greenMat; mats[CabMesh.Needle] = rig.needleMat; mats[CabMesh.Radio] = rig.radioMat;

            // ---- everything that does not move: one mesh, one submesh per material ----
            var g = new CabMesh();
            g.mb.SetSub(CabMesh.SlotCount - 1);
            CabDash.Build(g);
            CabGauges.Build(g);
            CabShell.Build(g);
            CabSeats.Build(g);
            CabExterior.Build(g);
            CabParts.Housings(g);
            GameObject body = g.ToObject("CabBody", cab, mats);
            body.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.TwoSided;   // the shell is single-sided: it must still shade the cabin
            int verts = g.VertexCount;

            // ---- steering wheel ----
            rig.planeRot = Quaternion.LookRotation(CabLayout.WheelAxis, CabLayout.WheelUp);
            CabMesh wm = CabParts.Wheel();
            rig.wheel = wm.ToObject("SteeringWheel", cab, rig.ambMats[6], rig.ambMats[0], a.Mat("cab_chrome")).transform;   // worn leather rim
            rig.wheel.localPosition = CabDims.WheelCentre;
            rig.wheel.localRotation = rig.planeRot;
            verts += wm.VertexCount;

            // ---- wipers: rotate in the glass plane about their pivots ----
            rig.wiperRot = Quaternion.LookRotation(CabLayout.GlassOut, CabLayout.GlassUp);
            Material rubber = a.Mat("cab_rubber");
            rig.wiperL = CabParts.Wiper().ToObject("WiperL", cab, rubber).transform;
            rig.wiperR = CabParts.Wiper().ToObject("WiperR", cab, rubber).transform;
            rig.wiperL.localPosition = CabDims.WiperPivotL;
            rig.wiperR.localPosition = CabDims.WiperPivotR;
            rig.wiperL.localRotation = rig.wiperR.localRotation = rig.wiperRot * Quaternion.Euler(0f, 0f, CabDims.WiperParkDeg);

            // ---- needles ----
            Material gloss = a.Mat("cab_trim_gloss");
            rig.nSpeed = MakeNeedle(cab, "NeedleSpeed", -CabLayout.DialBigX, CabLayout.DialY, true, rig.needleMat, gloss);
            rig.nTach = MakeNeedle(cab, "NeedleTach", CabLayout.DialBigX, CabLayout.DialY, true, rig.needleMat, gloss);
            rig.nFuel = MakeNeedle(cab, "NeedleFuel", -CabLayout.DialSmallX, CabLayout.DialSmallY, false, rig.needleMat, gloss);
            rig.nTemp = MakeNeedle(cab, "NeedleTemp", CabLayout.DialSmallX, CabLayout.DialSmallY, false, rig.needleMat, gloss);
            rig.nSpeed.localRotation = rig.nTach.localRotation = rig.planeRot * Quaternion.Euler(0f, 0f, 120f);
            rig.nFuel.localRotation = rig.nTemp.localRotation = rig.planeRot * Quaternion.Euler(0f, 0f, 50f);

            // ---- mirror glasses ----
            Material mp = a.Mat("cab_mirror_base");
            rig.mirrorFace = CabParts.Face(cab, "MirrorFace", CabDims.MirrorCentre, CabParts.RearMirrorNormal, 0.24f, 0.07f, mp);
            rig.sideMirrorFaceL = CabParts.Face(cab, "SideMirrorFaceL", CabDims.SideMirrorL, CabParts.SideMirrorNormal(-1), 0.17f, 0.105f, mp);
            rig.sideMirrorFaceR = CabParts.Face(cab, "SideMirrorFaceR", CabDims.SideMirrorR, CabParts.SideMirrorNormal(1), 0.17f, 0.105f, mp);

            // ---- seeded motion ----
            var r = new Rng(0x0CAB5EEDu);
            rig.wSteer = new Wobble(ref r, 0.07f, 0.23f);
            rig.wGust = new Wobble(ref r, 0.25f, 0.7f);
            rig.wBuffet = new Wobble(ref r, 0.5f, 1.6f);
            rig.wRoadY = new Wobble(ref r, 3.1f, 8.5f);
            rig.wRoadP = new Wobble(ref r, 2.2f, 6.0f);
            rig.wRoadR = new Wobble(ref r, 2.6f, 7.0f);
            rig.wFloatY = new Wobble(ref r, 0.35f, 0.8f);
            rig.wFloatP = new Wobble(ref r, 0.3f, 0.7f);
            rig.wFloatR = new Wobble(ref r, 0.25f, 0.6f);
            rig.wTach = new Wobble(ref r, 0.8f, 2.4f);
            rig.wFlick = new Wobble(ref r, 5f, 11f);
            rig.SetGlow(0f, 0f);
            Debug.Log("[CabRig] built, " + verts + " vertices in the body and the wheel");
            return rig;
        }

        static Transform MakeNeedle(Transform cab, string name, float x, float y, bool big, Material needle, Material gloss)
        {
            CabMesh m = big ? CabParts.Needle(0.043f, 0.012f, 0.0032f, 0.009f) : CabParts.Needle(0.021f, 0.006f, 0.0026f, 0.006f);
            GameObject go = m.ToObject(name, cab, needle, gloss);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            go.transform.localPosition = CabLayout.Cluster(x, y, -0.006f);
            return go.transform;
        }

        void SetGlow(float gauge, float radio)
        {
            glowMat.SetColor(idEmissive, glowBase * gauge);
            greenMat.SetColor(idEmissive, greenBase * gauge);
            needleMat.SetColor(idEmissive, needleBase * gauge);
            radioMat.SetColor(idEmissive, radioBase * radio);
        }

        void OnDestroy()
        {
            if (glowMat != null) Destroy(glowMat);
            if (greenMat != null) Destroy(greenMat);
            if (needleMat != null) Destroy(needleMat);
            if (radioMat != null) Destroy(radioMat);
            if (ambMats != null)
                for (int i = 0; i < ambMats.Length; i++)
                    if (ambMats[i] != null) Destroy(ambMats[i]);
        }

        static float Ease(float dt, float tau) { return 1f - Mathf.Exp(-dt / tau); }

        // ------------------------------------------------------------------------------------------------ animation
        void Update()
        {
            if (!Director.Running) return;
            float dt = Mathf.Min(Director.FrameDt, 0.1f), t = Director.Ambient, T = Director.T;
            float speed = Director.Speed, engine = Mathf.Clamp01(Director.S.engine), gust = Mathf.Clamp01(Director.S.gust);
            float sf = Mathf.Clamp01(speed / Config.SpeedBrisk), moving = Mathf.Clamp01(speed / 6f);
            if (!primed)
            {
                primed = true; prevSpeed = speed;
                vSpeed = speed * 3.6f; vTach = (700f + 900f * sf) * engine; vFuel = 0.75f; vTemp = 0.5f;
            }

            // ---- instrument light: on with the headlights, flickers out with the engine; the radio dies on its own ----
            float alive = engine >= 1f ? 1f : engine * (0.65f + 0.35f * wFlick.Eval(t));
            GaugeLight = Mathf.Clamp01(TruckLights.Level * alive);
            WindscreenPass.CabGlow = GaugeLight;
            float radio = Director.S.radio;
            float radioLevel = Mathf.Clamp01(radio * 2f) * Mathf.Min(1f, engine * 4f);
            if (radio < 0.5f) radioLevel *= Mathf.Clamp01(0.55f + 0.6f * wFlick.Eval(t * 1.7f + 3f));
            SetGlow(GaugeLight, radioLevel);

            // ---- dusk ambient: HDRP's sky ambient lights the cab as if it stood in the open, and at dusk its bright
            // orange-green lower half turned the pillars and the headliner into an olive slab brighter than the hood.
            // Inside, most of each surface's hemisphere is roof, pillars and floor, so the AO ceiling of the photo
            // materials follows the sun down (1 by day, cab.ambient at night). It scales only the ambient and the
            // specular occlusion; the dash light, the canopy, the headlights and the flashes are direct and untouched ----
            float sk = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-2.5f, 1.5f, Director.S.sunElev));
            float ak = Mathf.Lerp(ambMin, 1f, sk);
            // written only when it moves (a property write per material); the ends are written exactly
            if (Mathf.Abs(ak - ambK) > 0.002f || (ak != ambK && (sk <= 0f || sk >= 1f)))
            {
                ambK = ak;
                for (int i = 0; i < ambMats.Length; i++) ambMats[i].SetFloat(idAOMax, ak);
            }

            // ---- steering wheel: road curvature, the pull-off at the end, slow corrections, counter-steer in gusts ----
            double s = Director.TruckS;
            float lane = (Director.LaneOffset(s - 4.0) - 2f * Director.LaneOffset(s) + Director.LaneOffset(s + 4.0)) / 16f;
            float side = Mathf.Sin((Director.S.windDir - Director.HeadingDeg) * Mathf.Deg2Rad);      // + = wind pushes the truck to the right
            float target = (PathDef.Curvature(s) + lane) * Wheelbase * SteerRatio * Mathf.Rad2Deg * moving
                           + 2f * wSteer.Eval(t) * moving
                           - side * gust * (2.5f + 3f * (0.5f + 0.5f * wGust.Eval(t))) * moving;
            steer = Mathf.Lerp(steer, Mathf.Clamp(target, -40f, 40f), Ease(dt, 0.18f));
            wheel.localRotation = planeRot * Quaternion.Euler(0f, 0f, -steer);

            // ---- needles ----
            float rpm = (700f + 900f * sf + 22f * wTach.Eval(t)) * engine;
            float ignition = engine > 0.02f ? 1f : 0f;
            vSpeed = Mathf.Lerp(vSpeed, speed * 3.6f, Ease(dt, 0.25f));
            vTach = Mathf.Lerp(vTach, rpm, Ease(dt, 0.14f));
            vFuel = Mathf.Lerp(vFuel, 0.75f * ignition, Ease(dt, 0.7f));
            vTemp = Mathf.Lerp(vTemp, 0.5f * ignition, Ease(dt, 0.9f));
            nSpeed.localRotation = planeRot * Quaternion.Euler(0f, 0f, 120f - 240f * Mathf.Clamp01(vSpeed / 160f));
            nTach.localRotation = planeRot * Quaternion.Euler(0f, 0f, 120f - 240f * Mathf.Clamp01(vTach / 6000f));
            nFuel.localRotation = planeRot * Quaternion.Euler(0f, 0f, 50f - 100f * Mathf.Clamp01(vFuel));
            nTemp.localRotation = planeRot * Quaternion.Euler(0f, 0f, 50f - 100f * Mathf.Clamp01(vTemp));

            // ---- wipers: the shared model, in the plane of the glass ----
            Quaternion wr = wiperRot * Quaternion.Euler(0f, 0f, CabDims.WiperAngle(T));
            wiperL.localRotation = wr; wiperR.localRotation = wr;

            // ---- body motion ----
            // road texture and slow suspension float
            float heave = sf * (0.0022f * wRoadY.Eval(t) + 0.003f * wFloatY.Eval(t));
            float pitch = sf * (0.05f * wRoadP.Eval(t) + 0.10f * wFloatP.Eval(t));
            float roll = sf * (0.045f * wRoadR.Eval(t) + 0.09f * wFloatR.Eval(t));
            float shove = 0f;

            // gusts: lean downwind, buffet, a small lateral shove
            float buffet = wBuffet.Eval(t), lean = side * 0.55f + buffet * 0.45f;
            roll -= lean * 0.8f * gust;
            shove += lean * 0.008f * gust;
            heave += 0.001f * gust * wRoadY.Eval(t * 0.6f + 11f);

            // big hail stones on the roof, the hood and the glass: short damped jolts (stateless, from the score)
            if (T > 285f && T < 346f)
            {
                int k1 = Mathf.FloorToInt(T / HailScore.SlotLen);
                for (int k = k1 - 3; k <= k1; k++)
                {
                    if (!HailScore.BigHit(k, out float time, out HailScore.Where wh, out float size, out Vector2 pos)) continue;
                    float age = T - time;
                    if (age < 0f || age > 0.6f || wh == HailScore.Where.Road) continue;
                    float j = size / 0.11f * Mathf.Exp(-age * 9f) * Mathf.Sin(age * 40f);
                    if (wh == HailScore.Where.Roof) { heave -= 0.0022f * j; pitch += 0.04f * j * (pos.y - 0.5f); }
                    else if (wh == HailScore.Where.Hood) { heave -= 0.0008f * j; pitch += 0.14f * j; }
                    else pitch += 0.06f * j;
                    roll += 0.09f * j * (pos.x - 0.5f);
                }
            }

            // braking: the nose dives, then settles (a damped spring driven by the deceleration)
            if (dt > 1e-5f)
            {
                float a = Mathf.Clamp((speed - prevSpeed) / dt, -6f, 1.5f);
                prevSpeed = speed;
                accel = Mathf.Lerp(accel, a, Ease(dt, 0.12f));
                const float w0 = 9f, zeta = 0.5f;
                float goal = -accel / 4f * 0.6f;
                diveVel += (w0 * w0 * (goal - dive) - 2f * zeta * w0 * diveVel) * dt;
                dive += diveVel * dt;
                dive = Mathf.Clamp(dive, -0.5f, 1.2f);
            }
            pitch += dive;

            // engine: idle shiver (stronger standing still), a shudder as it stops
            float idle = engine * (0.00012f + 0.00022f * (1f - sf));
            heave += idle * Mathf.Sin(t * 147f);
            roll += idle * 60f * Mathf.Sin(t * 73.5f + 1f);
            float shudder = 4f * engine * (1f - engine);
            heave += 0.0012f * shudder * Mathf.Sin(t * 56f);
            roll += 0.12f * shudder * Mathf.Sin(t * 46f + 0.7f);

            cab.localPosition = restPos + new Vector3(shove, heave, 0f);
            cab.localRotation = restRot * Quaternion.Euler(pitch, 0f, roll);
        }
    }
}
