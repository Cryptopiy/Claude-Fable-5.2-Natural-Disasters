using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MESOCYCLONE
{
    /// <summary>Water, wiper clearing and hail cracks on the cab glass: one fullscreen Custom Pass before the
    /// post-processing. The panes are analytic quads taken from CabDims, so nothing has to be drawn twice.</summary>
    [System.Serializable]
    public sealed class WindscreenPass : CustomPass
    {
        public Material material;
        public Transform cab;
        RTHandle source;

        // the three impacts: glass metres (u right of the centre line, v up the slope), radial count, max length, rings, bullseye.
        // Cracks 2 and 3 have fewer radials than a spider web: the shader gives each star one or two long runners and
        // short stubs for the rest. Positions, seeds, crack 1 and the Story times stay (HeroHail and the audio use them).
        public static readonly Vector2[] CrackPos = { new Vector2(-0.02f, 0.24f), new Vector2(-0.50f, 0.58f), new Vector2(-0.12f, 0.40f) };
        static readonly Vector4[] CrackPar = { new Vector4(7f, 0.09f, 1f, 0.011f), new Vector4(9f, 0.40f, 3f, 0.016f), new Vector4(12f, 0.62f, 4f, 0.022f) };
        static readonly float[] CrackSeed = { 3.1f, 17.7f, 41.3f };

        static readonly int idSource = Shader.PropertyToID("_WsSource"), idCabInv = Shader.PropertyToID("_WsCabInv"),
            idO = Shader.PropertyToID("_WsO"), idU = Shader.PropertyToID("_WsU"), idV = Shader.PropertyToID("_WsV"), idB = Shader.PropertyToID("_WsB"),
            idState = Shader.PropertyToID("_WsState"), idWiper = Shader.PropertyToID("_WsWiper"), idWiperGeo = Shader.PropertyToID("_WsWiperGeo"),
            idLight = Shader.PropertyToID("_WsLight"), idLightDir = Shader.PropertyToID("_WsLightDir"),
            idCrack = Shader.PropertyToID("_WsCrack"), idCrackP = Shader.PropertyToID("_WsCrackP"),
            idClear = Shader.PropertyToID("_WsClear"), idCabCur = Shader.PropertyToID("_WsCabCur"), idCabPrev = Shader.PropertyToID("_WsCabPrev");

        readonly Vector4[] pO = new Vector4[4], pU = new Vector4[4], pV = new Vector4[4], pB = new Vector4[4];
        readonly Vector4[] crack = new Vector4[3];
        bool panesReady;

        // cab pose (cab space -> world) at this and at the last rendered frame, for the glass motion vectors
        Matrix4x4 prevCab, lastCab;
        int prevFrame = -100;
        readonly RenderTargetIdentifier[] mrt = new RenderTargetIdentifier[2];

        protected override bool executeInSceneView => false;

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            source = RTHandles.Alloc(Vector2.one, TextureXR.slices, dimension: TextureXR.dimension, colorFormat: GraphicsFormat.B10G11R11_UFloatPack32,
                filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp, useDynamicScale: true, name: "WsSource");
        }

        void BuildPanes()
        {
            // 0: windscreen
            Vector3 v0 = (CabDims.WTL - CabDims.WBL); v0.x = 0f; v0.Normalize();
            pO[0] = new Vector3(0f, CabDims.WBL.y, CabDims.WBL.z);
            pU[0] = new Vector4(1f, 0f, 0f, CabDims.WBL.x);
            pV[0] = new Vector4(v0.x, v0.y, v0.z, CabDims.WBR.x);
            pB[0] = new Vector4(CabDims.GlassHeight, (CabDims.WBR.x - CabDims.WTR.x) / CabDims.GlassHeight, 1f, 0f);
            // 1, 2: side glass (u forward, v up); the front edge leans back with the A-pillar
            float su = CabDims.SideGlassZ1 - CabDims.SideGlassZ0, sv = CabDims.SideGlassY1 - CabDims.SideGlassY0;
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -CabDims.CabHalfWidth : CabDims.CabHalfWidth;
                pO[1 + i] = new Vector3(x, CabDims.SideGlassY0, CabDims.SideGlassZ0);
                pU[1 + i] = new Vector4(0f, 0f, 1f, 0f);
                pV[1 + i] = new Vector4(0f, 1f, 0f, su);
                pB[1 + i] = new Vector4(sv, 0f, 2f + i, CabDims.SideGlassFrontSlant);
            }
            // 3: rear glass
            pO[3] = new Vector3(-CabDims.RearGlassX, CabDims.RearGlassY0, CabDims.RearWallZ);
            pU[3] = new Vector4(1f, 0f, 0f, 0f);
            pV[3] = new Vector4(0f, 1f, 0f, 2f * CabDims.RearGlassX);
            pB[3] = new Vector4(CabDims.RearGlassY1 - CabDims.RearGlassY0, 0f, 4f, 0f);
            panesReady = true;
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (material == null || source == null || cab == null || !Director.Running) return;
            if (ctx.hdCamera.camera.cameraType != CameraType.Game) return;
            // the cab moves against the camera (heave, pitch, roll, hail jolts), so the glass has its own motion. Track
            // the pose every rendered frame, even while the glass is still dry, so the history is valid when it gets wet.
            // After a skipped frame (a seek, a stall) there is no valid history and the cab counts as still; a second
            // render in the same frame keeps the pair it already has.
            Matrix4x4 curCab = cab.localToWorldMatrix;
            int frame = Time.frameCount;
            if (frame != prevFrame)
            {
                lastCab = frame - prevFrame == 1 ? prevCab : curCab;
                prevCab = curCab; prevFrame = frame;
            }
            if (!panesReady) BuildPanes();
            StoryState s = Director.S;
            // nothing on the glass yet: skip the whole pass
            if (s.wetness <= 0.001f && s.crack1 <= 0f) return;

            MaterialPropertyBlock b = ctx.propertyBlock;
            CustomPassUtils.Copy(ctx, ctx.cameraColorBuffer, source);
            b.SetTexture(idSource, source);
            b.SetMatrix(idCabInv, cab.worldToLocalMatrix);
            b.SetVectorArray(idO, pO); b.SetVectorArray(idU, pU); b.SetVectorArray(idV, pV); b.SetVectorArray(idB, pB);
            b.SetVector(idState, new Vector4(s.rain, Director.Speed, Director.Ambient, s.wetness));
            // the tear: the air is washed clear, so the mist film on the glass thins (wetness stays: it also drives the road)
            b.SetVector(idClear, new Vector4(s.clearAir, 0f, 0f, 0f));
            b.SetMatrix(idCabCur, curCab);
            b.SetMatrix(idCabPrev, lastCab);

            // wipers: same clock as the blades and the sound (WiperModel)
            float T = Director.T;
            int mode = WiperModel.Mode(T);
            WiperModel.Sweep(T, out float ts, out float dur, out float cycle);
            b.SetVector(idWiper, new Vector4(ts, dur, cycle, mode > 0 ? 1f : 0f));
            float sinceOff = mode > 0 ? 60f : Mathf.Max(0.5f, T - WiperModel.LastStop(T));
            float pivotV = -0.02f;
            b.SetVector(idWiperGeo, new Vector4(CabDims.WiperPivotL.x, CabDims.WiperPivotR.x, pivotV, sinceOff));

            // light for glints: the flash seen in the plane of the windscreen, the headlight spill, the dash glow
            Color fc = LightningDirector.LevelColor * LightningDirector.Level * Tune.F("glass.flash", 14f);
            b.SetVector(idLight, new Vector4(fc.r, fc.g, fc.b, TruckLights.Level));
            Vector3 dl = cab.InverseTransformDirection(LightningDirector.MainDirLocal);
            Vector2 d2 = new Vector2(dl.x, dl.y);
            if (d2.sqrMagnitude < 1e-4f) d2 = Vector2.up;
            d2.Normalize();
            b.SetVector(idLightDir, new Vector4(d2.x, d2.y, CabGlow, s.hail));

            crack[0] = new Vector4(CrackPos[0].x, CrackPos[0].y, s.crack1, CrackSeed[0]);
            crack[1] = new Vector4(CrackPos[1].x, CrackPos[1].y, s.crack2, CrackSeed[1]);
            crack[2] = new Vector4(CrackPos[2].x, CrackPos[2].y, s.crack3, CrackSeed[2]);
            b.SetVectorArray(idCrack, crack);
            b.SetVectorArray(idCrackP, CrackPar);

            // The pass runs before TAA: with the camera motion-vector buffer bound as a second target (pass 1) the drops
            // and cracks carry the glass motion and TAA stops smearing them along the world's. Without that buffer
            // (motion vectors off for this camera) the colour-only pass 0 is drawn as before.
            if (material.passCount > 1 && ctx.cameraMotionVectorsBuffer != null
                && ctx.hdCamera.frameSettings.IsEnabled(FrameSettingsField.MotionVectors))
            {
                mrt[0] = ctx.cameraColorBuffer;
                mrt[1] = ctx.cameraMotionVectorsBuffer;
                CoreUtils.SetRenderTarget(ctx.cmd, mrt, ctx.cameraDepthBuffer);
                CoreUtils.DrawFullScreen(ctx.cmd, material, b, 1);
            }
            else
            {
                CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ctx.cameraDepthBuffer, ClearFlag.None);
                CoreUtils.DrawFullScreen(ctx.cmd, material, b, 0);
            }
        }

        /// <summary>Instrument glow level 0..1, written by the cab.</summary>
        public static float CabGlow;

        protected override void Cleanup()
        {
            source?.Release();
            source = null;
        }
    }
}
