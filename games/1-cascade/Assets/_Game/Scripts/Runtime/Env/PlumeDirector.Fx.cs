using UnityEngine;

namespace CASCADE
{
    /// <summary>The low volumes of the catastrophe (Plume.shader pass 2 / PlumeFx.hlsl): collapse dust, splash, crest spray, cliff strike.</summary>
    public partial class PlumeDirector
    {
        static readonly int idFxDust = Shader.PropertyToID("_FxDust"), idFxDust2 = Shader.PropertyToID("_FxDust2"), idFxSplash = Shader.PropertyToID("_FxSplash"),
            idFxSplash2 = Shader.PropertyToID("_FxSplash2"), idFxCrest = Shader.PropertyToID("_FxCrest"), idFxCrest2 = Shader.PropertyToID("_FxCrest2"),
            idFxCrest3 = Shader.PropertyToID("_FxCrest3"), idFxMisc = Shader.PropertyToID("_FxMisc"),
            idFxImpact = Shader.PropertyToID("_FxImpact"), idFxImpact2 = Shader.PropertyToID("_FxImpact2");

        // the strike on the cliffs (a tower of water inside the ring segment)
        const float ImpactStrength = 1f;        // density scale of the strike volume
        const float ImpactJetCap = 650f;        // m: cap of the jet height before the per-jet variation (x0.55..1.45)
        const float ImpactReach = 1400f;        // m behind the front: >= FX_IMP_LIFE1 (36 s) * front speed (32.6 m/s) + lobe offsets
        const float ImpactTop = 900f;           // m: top of the strike volume (the tallest jet is held 60 m under it)
        // The fire below lights these volumes with the column's _PlUp (PlumePass), at the same place and falloff:
        const float FireKeyGain = 2.5f;         // x _PlUp: the warm side of dust and crown once the sun is gone
        const float FireBackGain = 0.6f;        // forward-scattered back light through spray (molten rims with the column behind the water)

        void TickFx()
        {
            // ---- low volumes: the dust of the collapse, the splash crown, the spray curtain of the wave ----
            float tc = Story.T(Ev.Collapse), tw = Story.T(Ev.Wave);
            float dustK = tc > 0.3f ? MathX.Smooth(0.3f, 6f, tc) * (1f - MathX.Smooth(38f, 95f, tc)) : 0f;
            float uf = Mathf.Min(Sim.Slide(tc) * ConeDirector.SlideRun + 420f + Mathf.Max(tc - Config.SlideDuration, 0f) * 14f, 3250f);
            mat.SetVector(idFxDust, new Vector4(Sim.SlideDir.x, Sim.SlideDir.y, uf, dustK));
            mat.SetVector(idFxDust2, new Vector4((160f + 950f * MathX.Smooth(0f, 40f, tc)) * (1f - 0.45f * MathX.Smooth(40f, 90f, tc)), 600f + 2200f * MathX.Smooth(0f, 25f, tc), Mathf.Max(tc, 0f), 0f));
            float ts = tc - Config.SplashAt;
            float splashK = ts > -0.5f ? MathX.Smooth(-0.5f, 3f, ts) * (1f - MathX.Smooth(70f, 125f, ts)) : 0f;
            mat.SetVector(idFxSplash, new Vector4(Sim.SplashXZ.x, Sim.SplashXZ.y, 80f + 1.25f * Sim.SplashHeight(tc) + 140f * MathX.Smooth(0f, 6f, ts) * (1f - MathX.Smooth(20f, 60f, ts)), 260f + 700f * MathX.Smooth(0f, 30f, ts)));
            mat.SetVector(idFxSplash2, new Vector4(splashK, MathX.Smooth(8f, 40f, ts), Mathf.Max(ts, 0f), 0f));
            float crestK = tw > 6f ? MathX.Smooth(6f, 24f, tw) * (1f - MathX.Smooth(Config.WaveCross + 4f, Config.WaveCross + 26f, tw)) : 0f;
            Vector2 wc = Sim.WaveCentre(tw);
            float A = Sim.WaveHeight(tw);
            mat.SetVector(idFxCrest, new Vector4(wc.x, wc.y, Sim.WaveFrontRadius(tw), A));
            mat.SetVector(idFxCrest2, new Vector4(Mathf.Max(0.5f * A, 26f), crestK, Sim.WaveAimDir.x, Sim.WaveAimDir.y));
            mat.SetVector(idFxCrest3, new Vector4(Mathf.Max(tw, 0f), Sim.WaveReach / Config.WaveCross, Config.MaxRunup, 0f));

            // ---- the strike: from the first landfall on the south coast (tw ~ 63) until the mist of the village strike clears ----
            float impactK = tw > 50f ? MathX.Smooth(55f, 62f, tw) * (1f - MathX.Smooth(Config.WaveCross + 32f, Config.WaveCross + 52f, tw)) : 0f;
            mat.SetVector(idFxImpact, new Vector4(impactK * ImpactStrength, ImpactJetCap, ImpactReach, ImpactTop));
            // each stretch of coast keeps the height of the wave that struck it, not the decaying height of today's crest
            mat.SetVector(idFxImpact2, new Vector4(A, Sim.WaveHeight(tw - 10f), Sim.WaveHeight(tw - 20f), Sim.WaveHeight(tw - 30f)));

            FxOn = dustK > 0.001f || splashK > 0.001f || crestK > 0.001f || impactK > 0.001f;
            mat.SetVector(idFxMisc, new Vector4(FxOn ? 1f : 0f, final ? 64f : 48f, FireBackGain, FireKeyGain));
        }
    }
}
