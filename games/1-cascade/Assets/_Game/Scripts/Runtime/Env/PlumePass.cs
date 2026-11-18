using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace CASCADE
{
    /// <summary>HDRP custom pass: ray-marches the plume at reduced resolution, then composites it over sky and far scenery.</summary>
    [System.Serializable]
    public class PlumePass : CustomPass
    {
        public Material mat;
        public int divisor = 2;
        RTHandle buf, fxBuf, tBuf;
        static readonly int idTex = Shader.PropertyToID("_PlumeTex"), idFxTex = Shader.PropertyToID("_FxTex"), idDepthTex = Shader.PropertyToID("_PlDepthTex");
        static readonly int idUv = Shader.PropertyToID("_PlUv");

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            buf = RTHandles.Alloc(Vector2.one / divisor, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat, filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp,
                useDynamicScale: true, name: "CascadePlume");
            fxBuf = RTHandles.Alloc(Vector2.one / divisor, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: GraphicsFormat.R16G16B16A16_SFloat, filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp,
                useDynamicScale: true, name: "CascadeFx");
            // the scene distance each half-res texel is marched against (one R16F channel, capped at 60 km in the shader)
            tBuf = RTHandles.Alloc(Vector2.one / divisor, TextureXR.slices, dimension: TextureXR.dimension,
                colorFormat: GraphicsFormat.R16_SFloat, filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp,
                useDynamicScale: true, name: "CascadePlumeT");
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (mat == null || buf == null || tBuf == null || !PlumeDirector.Active) return;
            Vector4 sc = buf.rtHandleProperties.rtHandleScale;
            ctx.propertyBlock.SetVector(idUv, new Vector4(sc.x, sc.y, divisor, 0f));
            // pass 3: depth of each half-res texel (the same farthest-of-2x2 rule as passes 0 and 2), so the composite
            // upsamples the plume and the fx without bleeding over nearer silhouettes
            CoreUtils.SetRenderTarget(ctx.cmd, tBuf, ClearFlag.None);
            CoreUtils.DrawFullScreen(ctx.cmd, mat, ctx.propertyBlock, shaderPassId: 3);
            ctx.propertyBlock.SetTexture(idDepthTex, tBuf);
            CoreUtils.SetRenderTarget(ctx.cmd, buf, ClearFlag.None);
            CoreUtils.DrawFullScreen(ctx.cmd, mat, ctx.propertyBlock, shaderPassId: 0);
            ctx.propertyBlock.SetTexture(idTex, buf);
            if (PlumeDirector.FxOn)
            {
                CoreUtils.SetRenderTarget(ctx.cmd, fxBuf, ClearFlag.None);
                CoreUtils.DrawFullScreen(ctx.cmd, mat, ctx.propertyBlock, shaderPassId: 2);
            }
            ctx.propertyBlock.SetTexture(idFxTex, fxBuf);
            CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ctx.cameraDepthBuffer, ClearFlag.None);
            CoreUtils.DrawFullScreen(ctx.cmd, mat, ctx.propertyBlock, shaderPassId: 1);
        }

        protected override void Cleanup()
        {
            if (buf != null) { buf.Release(); buf = null; }
            if (fxBuf != null) { fxBuf.Release(); fxBuf = null; }
            if (tBuf != null) { tBuf.Release(); tBuf = null; }
        }
    }

    /// <summary>Feeds the plume shader from the eruption clock.</summary>
    public partial class PlumeDirector
    {
        public static bool Active, FxOn;
        public static Vector4 Bolt0, Bolt1;      // lightning inside the plume: xyz position, w intensity (set by the particle director)
        Material mat;
        bool final;

        static readonly int idVent = Shader.PropertyToID("_PlVent"), idCol = Shader.PropertyToID("_PlCol"), idUmb = Shader.PropertyToID("_PlUmb"),
            idGlow = Shader.PropertyToID("_PlGlow"), idMisc = Shader.PropertyToID("_PlMisc"), idBolt0 = Shader.PropertyToID("_PlBolt0"),
            idBolt1 = Shader.PropertyToID("_PlBolt1"), idSteam = Shader.PropertyToID("_PlSteam"), idWind = Shader.PropertyToID("_PlWind"), idShock = Shader.PropertyToID("_PlShock"),
            idVeil = Shader.PropertyToID("_PlVeil"), idVeil2 = Shader.PropertyToID("_PlVeil2"), idVeilCol = Shader.PropertyToID("_PlVeilCol"),
            idCVent = Shader.PropertyToID("_CVent"), idCVentGlow = Shader.PropertyToID("_CVentGlow"),
            idPlUp = Shader.PropertyToID("_PlUp"), idPlRoot = Shader.PropertyToID("_PlRoot"), idPlTherm = Shader.PropertyToID("_PlTherm"),
            idPlUnder = Shader.PropertyToID("_PlUnder");

        public const float VentRadius = 380f;
        public const float Growth = 0.15f;
        public const float HeadFactor = 1.12f;        // head radius / column radius at the top: a swollen top, not a ball on a neck
        const float RootFloor = 40f;                  // the scar floor lies about this far below the settled vent (m)
        const float RootJet = 0.55f;                  // column radius factor where the jet leaves the vent (0.45 read as a needle)
        const float FireKeyNits = 1600f;              // the fire below as key light once the sun is gone (PlumeFx's dust and spray
                                                      // take it as is; the column scales it by PL_KEY_GAIN/PL_KEY_R in Plume.shader)
        const float LidUnderScale = 14000f;           // radial falloff (m) of the fire light on the underside of the lid: a hot
                                                      // spot over the vent that dims toward the rim (about h = 15 km of point-source falloff)
        // scale height (m) of the incandescent root: a short hot jet once dark (1500 made a 3 km candle flame); above it the
        // column is lit by the fire, not glowing (Plume.shader PL_KEY_*)
        const float GlowHeightDay = 540f, GlowHeightDark = 700f;
        const float VeilDim = 0.4f;                   // the clearing's veil x the storm fog colour: HDRP's lit storm fog reads
                                                      // ~1.3 EV under its colour, so the full colour was a pale card around the hole
        const float UnderBase = 0.35f, UnderDark = 0.9f;  // fire light on the lid underside, x fireNits: base + dark x max(Dark, Night)
        const int HeadThermalCount = 7;
        static readonly Vector4[] headTherm = new Vector4[HeadThermalCount];

        public static PlumeDirector Create(Transform root, bool final)
        {
            var d = new PlumeDirector { final = final, mat = GameAssets.I.Mat("M_Plume") };
            var go = new GameObject("PlumePass");
            go.transform.SetParent(root, false);
            var v = go.AddComponent<CustomPassVolume>();
            v.isGlobal = true;
            v.injectionPoint = CustomPassInjectionPoint.BeforeTransparent;
            var p = (PlumePass)v.AddPassOfType<PlumePass>();
            p.mat = d.mat;
            p.divisor = 2;
            p.name = "Plume";
            p.clearFlags = ClearFlag.None;
            return d;
        }

        public void Tick(float dt)
        {
            float te = Story.T(Ev.Eruption), tc = Story.T(Ev.Collapse);
            Active = true;
            float drop = ConeDirector.VentDrop(tc);
            Vector3 vent = ConeDirector.Vent(tc);
            float top = Sim.ColumnTop(te);
            float zf = te > 0f ? top - vent.y : -5000f;
            float umbBase = Sim.UmbrellaBase - vent.y, umbTop = Sim.UmbrellaTop - vent.y;
            mat.SetVector(idVent, new Vector4(vent.x, vent.y, vent.z, VentRadius));
            mat.SetVector(idCol, new Vector4(zf, Growth, HeadFactor, 0.022f));
            mat.SetVector(idUmb, new Vector4(Sim.UmbrellaRadius(te), umbBase, umbTop, umbBase));
            // the head: thermals in vent space around the head centre (same formulas as BaseShape)
            float hr = (VentRadius + Growth * Mathf.Clamp(zf, 0f, umbBase)) * HeadFactor;
            HeadThermals(te, hr, zf - hr * 0.72f);
            mat.SetVectorArray(idPlTherm, headTherm);
            // the root: during the slide the scar floor falls away before the vent has dropped with it; the jet reaches down to it
            float gap = Mathf.Max(ConeDirector.VentDrop(1e4f) * Sim.Scar(tc) - drop, 0f);
            mat.SetVector(idPlRoot, new Vector4(RootFloor + gap, RootJet, 0f, 0f));
            float flash = MathX.Smooth(0f, 0.3f, te) * (1f - MathX.Smooth(0.5f, 4f, te));
            float calm = MathX.Smooth(0f, 25f, Story.T(Ev.Silence));
            Color fireTint = Color.Lerp(Palette.Lava.linear, Palette.SunRed.linear, 0.6f * calm);
            float fireNits = Mathf.Lerp(1800f, 5200f, EnvDirector.Dark) * (1f - 0.5f * calm);
            Color glow = fireTint * (fireNits + 120000f * flash);
            // the glowing root climbs with the darkness: a tall incandescent stem at night, not a dull trapezoid at the vent
            mat.SetVector(idGlow, new Vector4(glow.r, glow.g, glow.b, Mathf.Lerp(GlowHeightDay, GlowHeightDark, EnvDirector.Dark)));
            Shader.SetGlobalVector(idCVent, new Vector4(vent.x, vent.y, vent.z, 0f));
            Shader.SetGlobalVector(idCVentGlow, new Vector4(glow.r, glow.g, glow.b, 0f) * (te > 0f ? 1f : 0f));
            // the fire as the column's key light once the sun has died (shader: _PlUp, also PlumeFx)
            float lavaK = MathX.Smooth(50f, 220f, te);
            Color up = Palette.Lava.linear * (FireKeyNits * MathX.Smooth(0.5f, 0.9f, EnvDirector.Dark) * (0.6f + 0.4f * lavaK) * (1f - 0.5f * calm));
            mat.SetVector(idPlUp, new Vector4(up.r, up.g, up.b, 0f));
            // the fire light on the underside of the lid (global: CSky mirrors it in the water); no flash, the lid is not there yet
            Color under = fireTint * (fireNits * (UnderBase + UnderDark * Mathf.Max(EnvDirector.Dark, EnvDirector.Night))
                                      * MathX.Smooth(Config.UmbrellaStart, Config.UmbrellaStart + 20f, te));
            Shader.SetGlobalVector(idPlUnder, new Vector4(under.r, under.g, under.b, LidUnderScale));
            float cap = MathX.Smooth(3500f, 7000f, top) * (1f - 0.7f * MathX.Smooth(20000f, 30000f, top));
            mat.SetVector(idMisc, new Vector4(final ? 64f : 52f, 1500f, cap * 0.4f, te));
            mat.SetVector(idBolt0, Bolt0);
            mat.SetVector(idBolt1, Bolt1);
            float shock = te > 0f ? MathX.Smooth(0.2f, 1.6f, te) * (1f - MathX.Smooth(Config.ShockDelay - 1f, Config.ShockDelay + 3.5f, te)) : 0f;
            mat.SetVector(idShock, new Vector4(Sim.ShockRadius(te), shock, 0f, 0f));
            mat.SetVector(idSteam, new Vector4(0f, flash, 0f, 0f));
            // the ash drifts towards the village (east, slightly north)
            mat.SetVector(idWind, new Vector4(0.94f, 0.34f, 2.2e-6f, 0f));

            // ash storm glimpses: the storm does not lift, a ragged clearing opens in it towards the burning flank and closes again
            float t1 = Story.T(Ev.Glimpse1), t2 = Story.T(Ev.Glimpse2);
            float g1 = Sim.GlimpseWindow(t1), g2 = Sim.GlimpseWindow(t2);
            bool second = g2 > g1;
            float tg = second ? t2 : t1;
            float open = MathX.Smooth(0.8f, 5.5f, tg) * (1f - MathX.Smooth(Config.GlimpseDuration - 6.5f, Config.GlimpseDuration - 2f, tg));
            Vector3 eye = PathDef.Pos(Story.Chainage) + Vector3.up * 1.7f;
            Vector3 axis = (Beat06_AshFire.PdcShore + new Vector3(0f, second ? 520f : 420f, 0f) - eye).normalized;
            mat.SetVector(idVeil, new Vector4(axis.x, axis.y, axis.z, Mathf.Clamp01(Mathf.Max(g1, g2) * 3f)));
            // z: the world flash of the current bolt (pulse, strength, exposure compensation): the veil's sheet flash around
            // the channel rides on the same budgeted pulse as the fog and sky flash
            mat.SetVector(idVeil2, new Vector4((second ? 0.21f : 0.15f) * open, 30f, EnvDirector.BoltFlash, Story.Time));
            mat.SetVector(idVeilCol, EnvDirector.StormFog * VeilDim);

            TickFx();
        }

        /// <summary>The head as a stack of thermals: each is born on top, rolls out over the shoulder and sinks back into
        /// the column (24 s cycle, staggered by the golden ratio, fading in and out so none pops). Pure function of te.</summary>
        static void HeadThermals(float te, float hr, float zc)
        {
            for (int k = 0; k < HeadThermalCount; k++)
            {
                float ph = Mathf.Repeat(te / 24f + 0.382f * k, 1f);
                float az = Mathf.PI * 2f * Mathf.Repeat(0.618f * k + 0.17f, 1f) + 0.8f * ph;
                float reach = hr * Mathf.Lerp(0.10f, 0.70f, ph);
                float r = hr * Mathf.Lerp(0.38f, 0.55f, MathX.Hash(k, 977)) * (0.65f + 0.35f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(1.3f * ph)))
                        * MathX.Smooth(0f, 0.12f, ph) * (1f - MathX.Smooth(0.8f, 1f, ph));
                headTherm[k] = new Vector4(Mathf.Cos(az) * reach, zc + hr * Mathf.Lerp(0.62f, -0.45f, ph * ph), Mathf.Sin(az) * reach, r);
            }
        }
    }
}
