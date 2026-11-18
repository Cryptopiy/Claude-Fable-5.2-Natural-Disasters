using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Builds all beats in order: gaze cues, designed lightning and each beat's own scenery.</summary>
    public static class Beats
    {
        public static void BuildAll(Transform root)
        {
            Gaze.Clear();
            LightningScore.Clear();
            Beat01_Evening.Build(root);
            Beat02_Anvil.Build(root);
            Beat03_Wall.Build(root);
            Beat04_Hail.Build(root);
            Beat05_Funnel.Build(root);
            Beat06_Unseen.Build(root);
            Beat07_Tear.Build(root);
            Beat08_Light.Build(root);
        }
    }
}
