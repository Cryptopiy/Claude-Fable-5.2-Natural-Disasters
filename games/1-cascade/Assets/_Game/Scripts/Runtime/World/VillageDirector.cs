using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace CASCADE
{
    /// <summary>
    /// Everything that changes in the built world: windows and lamps switching on, boats on the falling sea, and the
    /// swap of the village to its ruined version as the water takes each altitude band. Pure functions of the clocks,
    /// so seeking backwards restores the intact village.
    /// </summary>
    public class VillageDirector
    {
        public PathArchitecture.ArchResult arch;
        public Village.VillageResult vil;
        Material winA, winB, winC, winKeeper, lampA, lampB, beacon;
        static readonly int idEmis = Shader.PropertyToID("_EmissiveColor");
        // seconds after the wall reaches the harbour at which each altitude band is under water
        static readonly float[] BandDelay = { -3.5f, -1f, 1f, 2.6f, 4.2f, 5.8f, 7.4f, 1e9f };
        float[] boatSeabed, boatHit, boatRoll;
        Vector3[] boatDir;                     // horizontal direction the wall travels at each mooring
        // boats ridden and thrown by the wall (seconds on the crest's arrival clock at the boat)
        const float BoatLiftStart = -2f;       // the face starts to lift the boat
        const float BoatLiftRise = 2.5f;       // ... and has it on the crest this much later
        const float BoatThrowEnd = 4.5f;       // hidden in the strike spray after this
        const float BoatCarry = 0.7f;          // fraction of the front speed at which a thrown boat travels
        const float BoatSpin = 60f;            // deg/s of yaw while tumbling
        // lamp lights: in the storm the lamps make volumetric halos against the ash
        const float LampVolDay = 1.4f, LampVolStorm = 6f, KeeperVolDay = 2f, KeeperVolStorm = 8f;
        // Practicals and the exposure ladder (EnvDirector.LadderEV, up to +1.65 EV at night). The ladder lifts the surfaces;
        // the glass was tuned before it and clipped to flat yellow-white boxes (600 lamp 248/228/152, 664 window 247/217/110),
        // and a near lamp's halo flooded half the frame (600). The glass, the beacon and the lamp halos give back 60% of the
        // lift: they still read brighter in the dark, the glass sits at ~0.9-1.0 linear exposed (amber, not clipped,
        // still over the night bloom threshold of ~0.6). The small village windows never clipped: they give back less.
        const float PracticalComp = 0.6f;
        const float WindowComp = 0.3f;
        // the keeper's glass: redder than its own spill light (1, 0.40, 0.07). Through ACES a bright orange slides to yellow
        // once red saturates while green still climbs, and night bloom plus the haze in front add ~1.45x on the pane (v4: the
        // pane implies 1.6-1.7 linear red for 1.1 emitted): (1, 0.46, 0.10) x 7500 read lemon 246,203,51 with red clipped.
        // This sits at ~235,150,45 at the hot spot of the pane's emissive map (AssetFactory, M_WindowKeeper) and ~175,90,38
        // in its corners (map 0.45); without the map the whole pane reads at the hot-spot amber.
        static readonly Color KeeperGlass = new Color(1f, 0.25f, 0.065f);
        const float KeeperGlassNits = 7000f;   // at the hot spot; the ending's subject stays the brightest practical (~1.05 linear exposed red at night)
        // the keeper's window at the end: a pool of warm light down the apron and the top steps, and a shaft in the ash
        const float KeeperLumen = 160000f;     // 60000 gave a 30 px sliver on the apron (about 45 sRGB on stone)
        const float KeeperTilt = 22f;          // deg below horizontal: the hot spot ~5 m out, the cone's lower half spills down the steps
        const float KeeperRange = 30f;
        // ash haze held around the house: in the clear night air (mean free path ~4 km) no light can draw a shaft, so a soft
        // box of ash in front of the window gives the keeper spot (forward scattering toward the walker) something to light
        const float KeeperHazeMfp = 200f;      // m inside the box at full night (optical depth ~0.06 across it)
        const float LampHandover = 6f;         // m: a pooled light fades out before it jumps to another lamp (no popping halos)
        // the lantern flashes when one of the two beams sweeps past the eye
        const float BeaconNits = 30000f;       // glass luminance with a beam pointing at the eye
        const float BeaconBase = 0.15f;        // glow between flashes, relative
        const float BeaconLobe = 20f;          // pow of |cos| between beam and eye azimuth: ~30 deg wide flash, every 7.5 s
        // ash coat: (material, colour property, clean value, ash-covered value, weight)
        readonly System.Collections.Generic.List<Material> ashMats = new System.Collections.Generic.List<Material>();
        readonly System.Collections.Generic.List<int> ashProps = new System.Collections.Generic.List<int>();
        readonly System.Collections.Generic.List<Color> ashBase = new System.Collections.Generic.List<Color>(), ashTarget = new System.Collections.Generic.List<Color>();
        readonly System.Collections.Generic.List<float> ashWeight = new System.Collections.Generic.List<float>();
        static readonly string[] AshNames = { "M_Stone", "M_StoneWet", "M_Paving", "M_Gravel", "M_Whitewash", "M_WhitewashWarm", "M_WhitewashOld", "M_RockWall",
            "M_PlasterTrim", "M_Pumice", "M_DomeBlue", "M_ShutterGreen", "M_ShutterRed", "M_Land", "M_Wood", "M_WoodDark", "M_Canvas", "M_DoorBlue", "M_Iron",
            "M_WallOchre", "M_WallSand", "M_WallRose", "M_WallTerracotta", "M_WallSky", "M_Plinth",
            "M_PlantCore", "M_BougCore", "M_Cactus", "M_Bark", "M_Plant", "M_Bougainvillea", "M_FarOchre", "M_FarRose", "M_FarSand" };
        Light lampLightA, lampLightB, keeperLight;
        HDAdditionalLightData lampDataA, lampDataB, keeperData;
        LocalVolumetricFog keeperHaze;

        public static VillageDirector Create(Transform root, World world)
        {
            var d = new VillageDirector();
            var a = GameAssets.I;
            d.winA = a.Mat("M_WindowA"); d.winB = a.Mat("M_WindowB"); d.winC = a.Mat("M_WindowC"); d.winKeeper = a.Mat("M_WindowKeeper");
            d.lampA = a.Mat("M_LampA"); d.lampB = a.Mat("M_LampB"); d.beacon = a.Mat("M_BeaconGlass");
            // ash settles on everything: the shared materials drift towards ash grey (restored when seeking back)
            for (int i = 0; i < AshNames.Length; i++)
            {
                Material m = a.Mat(AshNames[i]);
                bool layered = AshNames[i] == "M_Land";
                for (int k = 0; k < (layered ? 4 : 1); k++)
                {
                    string prop = layered ? "_BaseColor" + k : "_BaseColor";
                    Color target;
                    if (!a.AshTint(AshNames[i] + "/" + prop, out target)) target = new Color(0.62f, 0.60f, 0.58f);   // sRGB, untextured materials
                    d.ashMats.Add(m); d.ashProps.Add(Shader.PropertyToID(prop)); d.ashBase.Add(m.GetColor(prop)); d.ashTarget.Add(target);
                    d.ashWeight.Add(AshNames[i].StartsWith("M_Whitewash") || AshNames[i].StartsWith("M_Wall") || AshNames[i] == "M_Plinth" ? 0.75f : layered ? 0.85f : 1f);
                }
            }
            d.arch = PathArchitecture.Build(root);
            d.vil = Village.Build(root);

            // lamp halos (switch-on order follows the path) and gull perches
            var lamps = new List<Vector4>(d.arch.lampHeads.Count);
            for (int i = 0; i < d.arch.lampHeads.Count; i++)
            {
                Vector3 p = d.arch.lampHeads[i];
                lamps.Add(new Vector4(p.x, p.y, p.z, MathX.Hash(i, 401)));
            }
            if (ParticleDirector.I != null)
            {
                if (lamps.Count > 0) ParticleDirector.I.SetLamps(lamps);
                ParticleDirector.I.SetBeacon(d.arch.beaconPos);
                // gull perches: the quay edge beside the walker (default set) plus the builder's mast tops and roof corners near the harbour
                if (d.vil.gullPerches != null && d.vil.gullPerches.Count > 4)
                {
                    var pp = new List<Vector4>(150);
                    for (int i = 0; i < 100; i++)
                    {
                        float z = Mathf.Lerp(-150f, -26f, Mathf.Pow(MathX.Hash(i, 3), 0.7f));
                        bool edge = MathX.Hash(i, 5) < 0.7f;
                        pp.Add(new Vector4(edge ? -19.6f + MathX.Hash(i, 7) * 0.6f : -22f - MathX.Hash(i, 9) * 26f, edge ? 2.12f : 0.2f, z, 0f));
                    }
                    for (int i = 0; i < d.vil.gullPerches.Count && pp.Count < 150; i++)
                    {
                        Vector3 p = d.vil.gullPerches[i];
                        if (p.z > -170f && p.x < 12f) pp.Add(new Vector4(p.x, p.y + 0.12f, p.z, 0f));
                    }
                    ParticleDirector.I.SetGullPerches(pp);
                }
            }

            // boats: where the sea floor is under each, and when the wall reaches it
            int nb = d.vil.boats != null ? d.vil.boats.Length : 0;
            d.boatSeabed = new float[nb]; d.boatHit = new float[nb]; d.boatRoll = new float[nb]; d.boatDir = new Vector3[nb];
            for (int i = 0; i < nb; i++)
            {
                Vector3 m = d.vil.boats[i].mooring;
                Vector2 m2 = new Vector2(m.x, m.z);
                d.boatSeabed[i] = TerrainBuilder.Height(m.x, m.z);
                d.boatHit[i] = Sim.WaveArrival(m2);
                Vector2 away = m2 - Sim.WaveCentre(d.boatHit[i]);
                away = away.sqrMagnitude > 1f ? away.normalized : Sim.WaveAimDir;
                d.boatDir[i] = new Vector3(away.x, 0f, away.y);
                d.boatRoll[i] = (MathX.Hash(i, 311) < 0.5f ? -1f : 1f) * (13f + 10f * MathX.Hash(i, 313));
            }

            // two pooled warm lights ride on the lamps nearest to the walker: pools of light to walk between
            d.lampLightA = MakeLampLight(root, "LampLightA");
            d.lampLightB = MakeLampLight(root, "LampLightB");
            d.lampDataA = d.lampLightA.GetComponent<HDAdditionalLightData>();
            d.lampDataB = d.lampLightB.GetComponent<HDAdditionalLightData>();
            // the last lit window spills onto its wall, the door and the steps
            // a spot just in front of the glass, aimed out and down: a warm trapezoid on the terrace and steps, a shaft in the ash
            {
                var go = new GameObject("KeeperLight");
                go.transform.SetParent(root, false);
                var hd = go.AddHDLight(LightType.Spot);
                var l = go.GetComponent<Light>();
                l.lightUnit = UnityEngine.Rendering.LightUnit.Lumen;
                l.color = new Color(1f, 0.66f, 0.30f);
                l.intensity = 0f;
                l.range = KeeperRange;
                l.spotAngle = 115f;
                l.innerSpotAngle = 60f;
                l.shadows = LightShadows.Soft;
                hd.SetShadowResolution(512);
                hd.SetShadowResolutionOverride(true);
                hd.affectsVolumetric = true;
                hd.volumetricDimmer = 2f;
                Vector3 n = d.arch.keeperWindowNormal; n.y = 0f; n.Normalize();
                go.transform.position = d.arch.keeperWindowPos + n * 0.6f;                     // clear of the window recess
                go.transform.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(KeeperTilt, Vector3.Cross(Vector3.up, n)) * n, Vector3.up);
                d.keeperLight = l;
                d.keeperData = hd;

                // the ash haze in front of the window (off until the night; Tick thickens it with the night clock)
                var hgo = new GameObject("KeeperHaze");
                hgo.transform.SetParent(root, false);
                hgo.transform.position = d.arch.keeperWindowPos + n * 6.5f + Vector3.down * 1.2f;   // apron and top steps
                hgo.transform.rotation = Quaternion.LookRotation(n, Vector3.up);
                var hz = hgo.AddComponent<LocalVolumetricFog>();
                hz.parameters.size = new Vector3(13f, 7f, 13f);                                 // across, up, out from the facade
                hz.parameters.meanFreePath = 4000f;
                hz.parameters.albedo = new Color(0.80f, 0.78f, 0.75f);                         // ash grey; the light gives the colour
                hz.parameters.blendingMode = LocalVolumetricFogBlendingMode.Additive;
                // soft on every side (fractions of the size): full density in front of the glass up to ~2.2 m over the
                // apron, thinning over 6 m toward the walker and 2 m above the window; thin at the facade so the wall stays clean
                hz.parameters.positiveFade = new Vector3(0.4f, 0.3f, 0.5f);
                hz.parameters.negativeFade = new Vector3(0.4f, 0.15f, 0.12f);
                hz.parameters.distanceFadeStart = 120f;
                hz.parameters.distanceFadeEnd = 220f;
                d.keeperHaze = hz;
                hgo.SetActive(false);
            }
            Gaze.SetLandmarks(d.arch.keeperWindowPos, d.arch.beaconPos, d.vil.chapelPos);
            return d;
        }

        static Light MakeLampLight(Transform root, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var hd = go.AddHDLight(LightType.Point);
            var l = go.GetComponent<Light>();
            l.lightUnit = UnityEngine.Rendering.LightUnit.Lumen;
            l.color = Palette.WindowAmber;
            l.intensity = 0f;
            l.range = 26f;
            l.shadows = LightShadows.Soft;                                   // railings and posts cast shadows into the pool
            hd.SetShadowResolution(512);
            hd.SetShadowResolutionOverride(true);
            hd.affectsVolumetric = true;
            hd.volumetricDimmer = 1.4f;
            return l;
        }

        public void Tick(float dt)
        {
            float te = Story.T(Ev.Eruption), tw = Story.T(Ev.Wave), ti = tw - Config.WaveCross;
            float night = EnvDirector.Night;
            // ---- windows and lamps: on at noon, in three groups; most of the village loses power when the wave lands ----
            float power = 1f - 0.8f * MathX.Smooth(-2f, 6f, ti);
            Color amber = Palette.WindowAmber.linear;
            // exposure-ladder take-back (see PracticalComp): 1 by day, 0.50 for the glass and 0.71 for the windows at night
            float pc = Mathf.Pow(2f, -PracticalComp * EnvDirector.LadderEV);
            float pw = Mathf.Pow(2f, -WindowComp * EnvDirector.LadderEV) * power;
            winA.SetColor(idEmis, amber * (3200f * Sim.Lamps(te, 0f) * pw));
            winB.SetColor(idEmis, amber * (2600f * Sim.Lamps(te, 6f) * pw));
            winC.SetColor(idEmis, amber * (3000f * Sim.Lamps(te, 13f) * pw));
            winKeeper.SetColor(idEmis, KeeperGlass * (KeeperGlassNits * Sim.Lamps(te, 3f) * pc));      // deeper amber: it must survive the grey grade
            keeperLight.intensity = KeeperLumen * Sim.Lamps(te, 3f);
            // the haze thickens with the night clock (no pop; inside the storm's 26 m fog it adds nothing visible)
            bool hazeOn = night > 0.01f;
            if (keeperHaze.gameObject.activeSelf != hazeOn) keeperHaze.gameObject.SetActive(hazeOn);
            if (hazeOn) keeperHaze.parameters.meanFreePath = Mathf.Lerp(4000f, KeeperHazeMfp, night);
            float lampOn = Sim.Lamps(te, 1.5f);
            lampA.SetColor(idEmis, amber * (6000f * lampOn * pc));
            lampB.SetColor(idEmis, amber * (6000f * Sim.Lamps(te, 4f) * pc));
            // the lantern glass flares when one of the two opposite beams sweeps past the eye (same angle as the beam itself)
            Vector3 eye = PathDef.Pos(Story.Chainage);
            float ba = ParticleDirector.BeamAngle(Story.Time);
            Vector2 toEye = new Vector2(eye.x - arch.beaconPos.x, eye.z - arch.beaconPos.z);
            float face = toEye.sqrMagnitude > 1f ? Mathf.Abs(Mathf.Cos(ba) * toEye.x + Mathf.Sin(ba) * toEye.y) / toEye.magnitude : 1f;
            float flare = BeaconBase + (1f - BeaconBase) * Mathf.Pow(Mathf.Clamp01(face), BeaconLobe);
            beacon.SetColor(idEmis, new Color(1f, 0.93f, 0.8f) * (BeaconNits * Sim.Lamps(te, 8f) * flare * pc));   // the peak still clips white

            // ---- the two nearest lamps get real lights ----
            // the lumens stay (the ladder is there to read what they light); their fog halo gives back the lift like the glass
            float storm = Mathf.Max(EnvDirector.Storm, night);
            float lampVol = Mathf.Lerp(LampVolDay, LampVolStorm, storm) * pc;
            lampDataA.volumetricDimmer = lampVol;
            lampDataB.volumetricDimmer = lampVol;
            keeperData.volumetricDimmer = Mathf.Lerp(KeeperVolDay, KeeperVolStorm, storm);     // not taken back: its shaft in the haze is the ending
            if (arch.lampHeads.Count > 0)
            {
                int b0 = -1, b1 = -1, b2 = -1; float d0 = 1e9f, d1 = 1e9f, d2 = 1e9f;
                for (int i = 0; i < arch.lampHeads.Count; i++)
                {
                    float dd = (arch.lampHeads[i] - eye).sqrMagnitude;
                    if (dd < d0) { d2 = d1; b2 = b1; d1 = d0; b1 = b0; d0 = dd; b0 = i; }
                    else if (dd < d1) { d2 = d1; b2 = b1; d1 = dd; b1 = i; }
                    else if (dd < d2) { d2 = dd; b2 = i; }
                }
                float r0 = Mathf.Sqrt(d0), r1 = Mathf.Sqrt(d1), r2 = b2 >= 0 ? Mathf.Sqrt(d2) : 1e4f;
                // a light fades to zero as the third-nearest lamp catches up with its lamp, so its jump is invisible
                float w0 = Mathf.Clamp01((r2 - r0) / LampHandover), w1 = Mathf.Clamp01((r2 - r1) / LampHandover);
                float lm = 150000f * lampOn * (0.15f + 0.85f * Mathf.Max(EnvDirector.Dark, night));
                if (b0 >= 0) { lampLightA.transform.position = arch.lampHeads[b0] + Vector3.down * 0.25f; lampLightA.intensity = lm * Mathf.Clamp01(2.2f - r0 / 22f) * w0; }
                if (b1 >= 0) { lampLightB.transform.position = arch.lampHeads[b1] + Vector3.down * 0.25f; lampLightB.intensity = lm * Mathf.Clamp01(2.2f - r1 / 22f) * w1; }
            }

            // ---- ash cover: a dusting during the column, a blanket by the end ----
            float coat = 0.22f * Sim.Ashfall(te) + 0.5f * MathX.Smooth(0f, 60f, Story.T(Ev.AshStorm)) + 0.18f * MathX.Smooth(0f, 30f, Story.T(Ev.Silence));
            for (int i = 0; i < ashMats.Count; i++)
                ashMats[i].SetColor(ashProps[i], Color.Lerp(ashBase[i], ashTarget[i], coat * ashWeight[i]));

            // ---- destruction: each altitude band swaps to its ruin when the water covers it ----
            for (int i = 0; i < 8; i++)
            {
                bool ruined = ti > BandDelay[i];
                if (vil.intactBands != null && i < vil.intactBands.Length && vil.intactBands[i] != null && vil.intactBands[i].activeSelf == ruined) vil.intactBands[i].SetActive(!ruined);
                if (vil.ruinedBands != null && i < vil.ruinedBands.Length && vil.ruinedBands[i] != null && vil.ruinedBands[i].activeSelf != ruined) vil.ruinedBands[i].SetActive(ruined);
                if (arch.fragileBands != null && i < arch.fragileBands.Length && arch.fragileBands[i] != null && arch.fragileBands[i].activeSelf == ruined) arch.fragileBands[i].SetActive(!ruined);
            }

            // ---- boats: float, settle on the silt as the sea leaves, then the wall lifts them and throws them inland ----
            if (vil.boats != null)
            {
                float waveTop = Mathf.Min(Sim.WaveHeight(tw), Config.MaxRunup);
                float carry = Sim.WaveReach / Config.WaveCross * BoatCarry;
                for (int i = 0; i < vil.boats.Length; i++)
                {
                    var b = vil.boats[i];
                    if (b == null || b.t == null) continue;
                    float ta = tw - boatHit[i];                       // seconds since the crest reached this mooring
                    bool gone = ta > BoatThrowEnd;                     // by now inside the spray of the strike
                    if (b.t.gameObject.activeSelf == gone) b.t.gameObject.SetActive(!gone);
                    if (gone) continue;
                    float level = WaterDirector.LevelAt(b.mooring.x, b.mooring.z);
                    float floor = boatSeabed[i] + b.draft * 0.55f;
                    float rest = Mathf.Clamp01((floor - level) / 0.5f + 0.5f);
                    float y = Mathf.Max(level, floor);
                    float bob = (1f - rest) * 0.05f * Mathf.Sin(Story.Time * 1.3f + i * 1.7f);
                    Vector3 pos = new Vector3(b.mooring.x, y + bob, b.mooring.z);
                    Quaternion rot = Quaternion.Euler((1f - rest) * 1.5f * Mathf.Sin(Story.Time * 0.9f + i), b.yaw, boatRoll[i] * rest + (1f - rest) * 2f * Mathf.Sin(Story.Time * 1.1f + i * 2.3f));
                    float lift = Mathf.Clamp01((ta - BoatLiftStart) / BoatLiftRise);
                    if (lift > 0f)
                    {
                        // ridden up the face, then carried inland with the front, tumbling; never below the ground it is swept over
                        Vector3 dir = boatDir[i];
                        float ride = Mathf.Max(ta, 0f);
                        Vector3 thrown = new Vector3(b.mooring.x, 0f, b.mooring.z) + dir * (carry * ride);
                        float crest = waveTop * (0.55f + 0.2f * Mathf.Sin(1.3f * ta + i));
                        thrown.y = Mathf.Max(crest, TerrainBuilder.Height(thrown.x, thrown.z) + 1.5f);
                        pos = Vector3.Lerp(pos, thrown, lift);
                        // the face tips the boat forward along the travel direction, then it spins and rolls
                        float sgn = boatRoll[i] < 0f ? -1f : 1f;
                        float tilt = lift * (35f + 40f * lift);
                        Quaternion thrownRot = Quaternion.AngleAxis(tilt, Vector3.Cross(Vector3.up, dir))
                                             * Quaternion.Euler(0f, b.yaw + sgn * BoatSpin * ride, 25f * lift * Mathf.Sin(2f * ta + i));
                        rot = Quaternion.Slerp(rot, thrownRot, lift);
                    }
                    b.t.position = pos;
                    b.t.rotation = rot;
                }
            }
        }
    }

    /// <summary>Pyroclastic currents and the dust of the collapse (the blanket shader on the cone's polar grid).</summary>
    public class BlanketDirector
    {
        static readonly int idTongue = Shader.PropertyToID("_BlTongue"), idDust = Shader.PropertyToID("_BlDust"), idMisc = Shader.PropertyToID("_BlMisc");
        readonly Vector4[] tongues = new Vector4[6];
        // azimuth (degrees), first start on the eruption clock, repeat period
        static readonly float[] Az = { 150f, 215f, 95f, -100f };
        static readonly float[] T0 = { 50f, 80f, 108f, 136f };
        static readonly float[] Period = { 150f, 172f, 190f, 164f };

        public static BlanketDirector Create(Transform root, GameObject cone)
        {
            var go = new GameObject("Blanket");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = cone.GetComponent<MeshFilter>().sharedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GameAssets.I.Mat("M_Blanket");
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return new BlanketDirector();
        }

        public void Tick(float dt)
        {
            float te = Story.T(Ev.Eruption), tc = Story.T(Ev.Collapse);
            for (int i = 0; i < 4; i++)
            {
                float clock = te > T0[i] ? Mathf.Repeat(te - T0[i], Period[i]) : 0f;
                tongues[i] = new Vector4(Az[i] * Mathf.Deg2Rad, clock, 0.20f + 0.05f * i, 0.8f + 0.1f * i);
            }
            // the two currents the gusts reveal: down the scar, towards the walker
            float g1 = Story.T(Ev.Glimpse1) + 26f, g2 = Story.T(Ev.Glimpse2) + 30f;
            tongues[4] = new Vector4((Sim.SlideAzimuth + 34f) * Mathf.Deg2Rad, g1 > 0f ? g1 : 0f, 0.22f, 1.0f);
            tongues[5] = new Vector4((Sim.SlideAzimuth + 6f) * Mathf.Deg2Rad, g2 > 0f ? g2 : 0f, 0.30f, 1.35f);
            Shader.SetGlobalVectorArray(idTongue, tongues);
            float dust = 0f;      // the collapse dust is a ray-marched volume now (PlumePass)
            Shader.SetGlobalVector(idDust, new Vector4(Mathf.Max(tc, 0f), dust, Sim.Slide(tc) * ConeDirector.SlideRun + Mathf.Max(tc - Config.SlideDuration, 0f) * 14f, 0f));
            float glow = MathX.Smooth(0.45f, 0.95f, EnvDirector.Dark) * (0.45f + 0.55f * EnvDirector.Night);
            Shader.SetGlobalVector(idMisc, new Vector4(52f, 230f, glow, Story.Time));
        }
    }
}
