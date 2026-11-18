using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace MESOCYCLONE
{
    /// <summary>Creates the weather systems at start: the tornado's particle layers, rain and splashes, the 3D hail,
    /// the flying debris and the glass pass.</summary>
    public static partial class Extras
    {
        static partial void BuildWeather(Bootstrap boot)
        {
            TornadoFx.Build(null);
            WeatherFx.Build(Bootstrap.Truck);
            LightningVisuals.Build(Bootstrap.WorldRoot);
            HailField.Build(Bootstrap.Truck);          // before HeroHail: it owns the shared live ice material
            HeroHail.Build(Bootstrap.Truck);
            DebrisFly.Build(Bootstrap.Truck);
            DebrisSwarm.Build(Bootstrap.Truck);

            var pv = new GameObject("GlassPass").AddComponent<CustomPassVolume>();
            pv.isGlobal = true;
            pv.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
            var glass = (WindscreenPass)pv.AddPassOfType<WindscreenPass>();
            glass.targetColorBuffer = CustomPass.TargetBuffer.None;
            glass.targetDepthBuffer = CustomPass.TargetBuffer.None;
            glass.material = new Material(GameAssets.I.Shader("Hidden/MESOCYCLONE/Windscreen"));
            glass.cab = Bootstrap.Cab;
        }
    }
}
