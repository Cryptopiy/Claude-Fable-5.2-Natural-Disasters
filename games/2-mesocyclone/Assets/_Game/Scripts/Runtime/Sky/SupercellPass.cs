using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MESOCYCLONE
{
    /// <summary>The supercell: a fullscreen Custom Pass (BeforePreRefraction, so the windscreen and every transparent
    /// see it). Low-resolution bounded sphere-traced march into two targets, then a depth-compare composite.
    /// All uniforms are computed here from the story state, in the moving local frame.</summary>
    [System.Serializable]
    public sealed class SupercellPass : CustomPass
    {
        public Material material;
        public Texture3D noise;
        public float scale = 0.5f;                 // set by Bootstrap: 0.7 final profile, 0.5 test profile
        RTHandle colorRT, auxRT;
        readonly RenderTargetIdentifier[] mrt = new RenderTargetIdentifier[2];
        int marchPass, compositePass;
        int frame;

        static readonly int idNoise = Shader.PropertyToID("_ScNoise"), idRes = Shader.PropertyToID("_ScRes"),
            idOrigin = Shader.PropertyToID("_ScOrigin"), idStorm = Shader.PropertyToID("_ScStorm"), idAnvil = Shader.PropertyToID("_ScAnvil"),
            idShelf = Shader.PropertyToID("_ScShelf"), idWall = Shader.PropertyToID("_ScWall"), idTor = Shader.PropertyToID("_ScTor"),
            idTor2 = Shader.PropertyToID("_ScTor2"), idTorRot = Shader.PropertyToID("_ScTorRot"), idRain = Shader.PropertyToID("_ScRain"),
            idDust = Shader.PropertyToID("_ScDust"), idSunDir = Shader.PropertyToID("_ScSunDir"), idSunCol = Shader.PropertyToID("_ScSunCol"),
            idAmbTop = Shader.PropertyToID("_ScAmbTop"), idAmbBot = Shader.PropertyToID("_ScAmbBot"), idTint = Shader.PropertyToID("_ScTint"),
            idFog = Shader.PropertyToID("_ScFog"), idFlashPos = Shader.PropertyToID("_ScFlashPos"), idFlashCol = Shader.PropertyToID("_ScFlashCol"),
            idGlowPos = Shader.PropertyToID("_ScGlowPos"), idGlowCol = Shader.PropertyToID("_ScGlowCol"),
            idSunGlow = Shader.PropertyToID("_ScSunGlow"), idSunSky = Shader.PropertyToID("_ScSunSky"), idCloudTex = Shader.PropertyToID("_ScCloudTex"), idAuxTex = Shader.PropertyToID("_ScAuxTex"),
            idCore = Shader.PropertyToID("_ScCore"), idCoreCol = Shader.PropertyToID("_ScCoreCol"),
            idTor3 = Shader.PropertyToID("_ScTor3"), idCore2 = Shader.PropertyToID("_ScCore2"), idCore2Col = Shader.PropertyToID("_ScCore2Col"),
            idClear = Shader.PropertyToID("_ScClear"), idPrecip = Shader.PropertyToID("_ScPrecip");

        /// <summary>Where the light behind the vortex is now (local frame) and how bright (nits at the source).</summary>
        public static Vector4 CorePos;
        public static Color CoreCol;

        /// <summary>The light deep inside the departing storm in the tear (local frame, radius in w) and how bright.</summary>
        public static Vector4 StormCorePos;
        public static Color StormCoreCol;

        static float Hash01(int i)
        {
            uint h = (uint)i * 2654435761u + 0x9e37u;
            h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216f;
        }

        /// <summary>Smooth value noise 0..1 (cubic between integer knots): it never jumps.</summary>
        public static float Smooth01(float x)
        {
            int i = Mathf.FloorToInt(x);
            float f = x - i;
            f = f * f * (3f - 2f * f);
            return Mathf.Lerp(Hash01(i), Hash01(i + 1), f);
        }

        /// <summary>The storm's own light: in-cloud lightning that never stops, deep in the rain behind the vortex.
        /// It wanders and breathes over seconds (never a flash: far below the 100 ms luminance step of the flash rule)
        /// and stands the funnel against it as a silhouette.</summary>
        static void CoreLight(in StoryState s)
        {
            float k = s.coreBack * Tune.F("core.nits", 12f);
            if (k <= 1e-4f) { CorePos = new Vector4(0f, 5000f, 0f, 1f); CoreCol = Color.black; return; }
            float T = s.T;
            Vector3 tor = Director.TornadoLocal();
            Vector2 dir = new Vector2(tor.x, tor.z);
            float dist = dir.magnitude;
            dir = dist > 1f ? dir / dist : Vector2.up;
            Vector2 perp = new Vector2(dir.y, -dir.x);
            float behind = s.torRadius + Tune.F("core.behind", 3800f);
            float side = (Smooth01(T * 0.13f + 5f) - 0.5f) * Tune.F("core.wander", 3000f);
            Vector2 c = new Vector2(tor.x, tor.z) + dir * behind + perp * side;
            float y = 900f + 1100f * Smooth01(T * 0.17f + 9f);
            CorePos = new Vector4(c.x, y, c.y, Tune.F("core.radius", 13000f));
            // breathing: a slow swell with a faster shimmer on top, never below a third
            float breath = 0.35f + 0.65f * Smooth01(T * 0.5f) * (0.55f + 0.45f * Smooth01(T * 1.4f + 31f));
            Color col = Palette.FlashBlue.linear * (k * breath);
            CoreCol = new Color(col.r, col.g, col.b, 1f);
        }

        /// <summary>The tear: the departing storm lit from inside by lightning that never quite stops. It replaces the
        /// flat 16-nit night ambient on every face with one placed glow in the heart of the storm, a little toward the
        /// truck, that wanders sideways and up and down over tens of seconds and breathes slowly (the fastest change is
        /// about 2.4 % of the glow per 100 ms, so it is never a flash). The designed pulses of beat 7 ride on top.</summary>
        static void StormCoreLight(in StoryState s)
        {
            float k = s.stormCore * Tune.F("sc.stormCoreNits", 5f);
            if (k <= 1e-4f) { StormCorePos = new Vector4(0f, 5000f, 0f, 1f); StormCoreCol = Color.black; return; }
            float T = s.T;
            Vector3 st = Director.StormLocal();
            Vector2 sxz = new Vector2(st.x, st.z);
            float dist = sxz.magnitude;
            // toward the truck (the local origin); straight south if the storm were ever overhead
            Vector2 toTruck = dist > 1f ? -sxz / dist : Vector2.down;
            Vector2 perp = new Vector2(toTruck.y, -toTruck.x);
            Vector2 c = sxz + toTruck * Tune.F("sc.stormCoreNear", 2500f) + perp * ((Smooth01(T * 0.11f + 41f) - 0.5f) * 5000f);
            float y = 7000f + 2500f * Smooth01(T * 0.09f + 23f);
            StormCorePos = new Vector4(c.x, y, c.y, Tune.F("sc.stormCoreRadius", 18000f));
            // a slow swell with a faster shimmer on top, never below about half
            float breath = 0.55f + 0.45f * Smooth01(T * 0.35f + 3f) * (0.6f + 0.4f * Smooth01(T * 0.9f + 17f));
            Color col = Palette.FlashBlue.linear * (k * breath);
            StormCoreCol = new Color(col.r, col.g, col.b, 1f);
        }

        readonly Vector4[] glowPos = new Vector4[2], glowCol = new Vector4[2];

        // slow rotations, integrated on the ambient clock (big is slow)
        public static float ShellAngle, InnerAngle, WallAngle, TowerAngle;
        public static Vector3 TownGlowLocal;        // set by the town
        public static float TownGlow;               // 0..1

        protected override bool executeInSceneView => false;

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            colorRT = RTHandles.Alloc(Vector2.one * scale, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, filterMode: FilterMode.Bilinear,
                wrapMode: TextureWrapMode.Clamp, dimension: TextureDimension.Tex2D, name: "ScCloud");
            auxRT = RTHandles.Alloc(Vector2.one * scale, colorFormat: GraphicsFormat.R16G16_SFloat, filterMode: FilterMode.Bilinear,
                wrapMode: TextureWrapMode.Clamp, dimension: TextureDimension.Tex2D, name: "ScAux");
            if (material != null)
            {
                marchPass = material.FindPass("March");
                compositePass = material.FindPass("Composite");
            }
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (material == null || colorRT == null || !Director.Running) return;
            if (ctx.hdCamera.camera.cameraType != CameraType.Game) return;
            CommandBuffer cmd = ctx.cmd;
            MaterialPropertyBlock b = ctx.propertyBlock;
            Fill(b);

            mrt[0] = colorRT.nameID; mrt[1] = auxRT.nameID;
            cmd.SetRenderTarget(mrt, colorRT.nameID, 0, CubemapFace.Unknown, -1);
            CoreUtils.SetViewport(cmd, colorRT);
            Vector2Int sz = colorRT.GetScaledSize(colorRT.rtHandleProperties.currentViewportSize);
            b.SetVector(idRes, new Vector4(sz.x, sz.y, 1f / sz.x, 1f / sz.y));
            CoreUtils.DrawFullScreen(cmd, material, b, marchPass);

            CoreUtils.SetRenderTarget(cmd, ctx.cameraColorBuffer, ctx.cameraDepthBuffer, ClearFlag.None);
            b.SetTexture(idCloudTex, colorRT);
            b.SetTexture(idAuxTex, auxRT);
            CoreUtils.DrawFullScreen(cmd, material, b, compositePass);
        }

        void Fill(MaterialPropertyBlock b)
        {
            StoryState s = Director.S;
            float dt = Director.FrameDt;
            // differential rotation: the debris ring and sub-vortices carry the visible turn, the shell is slower,
            // the wall cloud takes minutes, the tower barely moves
            float twoPi = Mathf.PI * 2f;
            float small = Mathf.InverseLerp(900f, 60f, s.torRadius);                  // ropes turn fast, wedges slowly
            ShellAngle += dt * twoPi / Mathf.Lerp(55f, 9f, small);
            InnerAngle += dt * twoPi / Mathf.Lerp(16f, 5f, small);
            WallAngle += dt * twoPi / Mathf.Lerp(190f, 70f, Mathf.Clamp01(s.torDescend));
            TowerAngle += dt * twoPi / 900f;
            frame++;

            b.SetTexture(idNoise, noise);
            b.SetVector(idOrigin, new Vector4((float)Director.OriginX, (float)Director.OriginZ, Director.Ambient, frame % 64));
            Vector3 storm = Director.StormLocal();
            b.SetVector(idStorm, new Vector4(storm.x, storm.z, s.anvilReach, s.mammatus));
            b.SetVector(idAnvil, new Vector4(s.anvilBack, s.anvilHalfWidth, TowerAngle, s.deckCover));
            // shelf: its leading edge crosses the road at world z = shelfZ; the apex of the arc sits on the road
            Vector3 shelf = Director.RoadToLocal(s.shelfZ, 0.0, 0f);
            b.SetVector(idShelf, new Vector4(shelf.z, shelf.x, s.shelfAmount, 0f));
            Vector3 tor = Director.TornadoLocal();
            b.SetVector(idWall, new Vector4(tor.x, tor.z, s.wallCloud, WallAngle));
            b.SetVector(idTor, new Vector4(tor.x, tor.z, s.torRadius, s.torDescend));
            b.SetVector(idTor2, new Vector4(s.torMorph, s.torRope, s.torWrap, s.torDebris));
            // the funnel's top follows the wall cloud (FunnelShape.Top), so the rope stretches thin as the wall lifts
            // instead of showing a straight column; w = gain of the silver rim the flashes put on the funnel's skin
            b.SetVector(idTorRot, new Vector4(ShellAngle, InnerAngle, FunnelShape.Top(s), Tune.F("tor.rim", 0.3f)));
            // x = strength of the funnel's soft shadow in the flash light (a diagnosis knob: 0 turns it off);
            // y = size of the debris dome, held low while the vortex passes close in beat 6 so the dust does not fill the glass
            // z: how much of the core's glow the lowered wall cloud over the vortex keeps off its own underside
            // w: the faint fill of the funnel's face by the lit rain around it (fraction of the core glow)
            b.SetVector(idTor3, new Vector4(Tune.F("sc.funnelShadow", 1f), s.domeScale, Tune.F("sc.wallShade", 0.75f), Tune.F("tor.fill", 0.08f)));
            // the rain sheet behind the shelf is only a light haze until the shelf forms (rainSheet), so the
            // forward-flank core beside the base is not hidden behind a uniform horizon veil
            float rainExt = s.coreGlow * s.rainSheet * Tune.F("sc.rainExt", 1f / 9000f);
            float wrapExt = Tune.F("sc.wrapExt", 1f / 1400f);
            b.SetVector(idRain, new Vector4(rainExt, 7500f, wrapExt, Tune.F("sc.mistExt", 1f / 24000f)));
            // the forward-flank precipitation core: downshear, east of the updraft and on the side away from the
            // tornado, so the rain-free base stays clear; it travels with the storm
            Vector3 pc = Director.ToLocal(s.stormX + Tune.F("precip.dx", 17000f), 0f, s.stormZ + Tune.F("precip.dz", -2000f));
            b.SetVector(idPrecip, new Vector4(pc.x, pc.z, s.precipCore * Tune.F("precip.rainExt", 1f / 2600f), s.precipCore * Tune.F("precip.hailExt", 1f / 1100f)));
            Vector3 dustP = Director.RoadToLocal(s.dustWallZ, 0.0, 0f);
            float dustAmt = Mathf.Clamp01(s.shelfAmount) * Mathf.InverseLerp(330f, 262f, s.T) * Mathf.InverseLerp(170f, 200f, s.T);
            b.SetVector(idDust, new Vector4(dustP.z, 520f, dustAmt, 0f));

            // at cloud height the low sun shines from just below the horizontal: the anvil is lit from underneath
            // (while the sun is still well up it shines on the flanks and the underside stays in shade)
            Vector3 sd = Atmosphere.SunDir;
            sd.y = Mathf.Lerp(-Tune.F("cloud.underlight", 0.11f), 0.07f, Mathf.InverseLerp(1.2f, 4.6f, s.sunElev));
            sd.Normalize();
            b.SetVector(idSunDir, sd);
            b.SetVector(idSunSky, Atmosphere.SunDir);
            b.SetVector(idSunCol, Atmosphere.CloudSunColor);
            b.SetVector(idSunGlow, Atmosphere.SunGlow);
            // ambient: twilight sky from above, the land's bounce from below; nearly nothing at night
            // twilight falls by a factor of ten for every 2.6 degrees the sun sinks below the horizon
            float day = Mathf.Pow(10f, Mathf.Min(0f, s.sunElev - 0.5f) / 3.6f) * Mathf.InverseLerp(-0.5f, 4f, s.sunElev + 2.5f);
            day = Mathf.Clamp01(day);
            // the night ambient follows the story: 16 nits through the storm, down to 3 once the tear opens, so the
            // departing storm is lit by its own placed core light and pulses instead of an even blue flood
            float nAmb = s.nightAmb * Tune.F("sc.nightAmbScale", 1f);
            Color top = Color.Lerp(Palette.StormSlate.linear * nAmb, new Color(0.38f, 0.5f, 0.78f) * Tune.F("sc.dayAmb", 105f), day);
            Color bot = Color.Lerp(Palette.StormSlate.linear * nAmb * 0.5f, new Color(0.44f, 0.44f, 0.54f) * Tune.F("sc.dayAmb", 105f) * 0.5f, day);
            b.SetVector(idAmbTop, top);
            b.SetVector(idAmbBot, bot);
            b.SetVector(idTint, new Vector4(s.greenTint, Mathf.Lerp(130000f, 115000f, Mathf.InverseLerp(-6f, 2f, s.sunElev)), s.starVis, Tune.F("sc.starNits", 9f)));
            b.SetVector(idFog, new Vector4(Atmosphere.FogExtinction, Atmosphere.FogBase, Atmosphere.FogScaleHeight, 0f));
            b.SetVectorArray(idFlashPos, LightningDirector.Pos);
            b.SetVectorArray(idFlashCol, LightningDirector.Col);
            Color sod = Palette.Sodium.linear * (TownGlow * Tune.F("sc.townNits", 1.6f));
            glowPos[0] = new Vector4(TownGlowLocal.x, 40f, TownGlowLocal.z, 3200f);
            glowCol[0] = new Vector4(sod.r, sod.g, sod.b, 1f);
            glowPos[1] = LightningDirector.GroundPos;
            glowCol[1] = LightningDirector.GroundCol;
            b.SetVectorArray(idGlowPos, glowPos);
            b.SetVectorArray(idGlowCol, glowCol);
            CoreLight(s);
            b.SetVector(idCore, CorePos);
            b.SetVector(idCoreCol, CoreCol);
            StormCoreLight(s);
            b.SetVector(idCore2, StormCorePos);
            b.SetVector(idCore2Col, StormCoreCol);
            // x = the tear's clear air (rain only under the departing storm), y = the lit-chamber mask on the flashes
            b.SetVector(idClear, new Vector4(s.clearAir, s.clearAir, 0f, 0f));
        }

        protected override void Cleanup()
        {
            colorRT?.Release(); auxRT?.Release();
            colorRT = null; auxRT = null;
        }
    }
}
