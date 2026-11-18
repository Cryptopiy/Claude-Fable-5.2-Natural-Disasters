namespace MESOCYCLONE
{
    /// <summary>The procedural audio rig lives on the listener (the camera).</summary>
    public static partial class Extras
    {
        static partial void BuildAudio(Bootstrap boot)
        {
            if (Bootstrap.Cam != null) Audio.AudioRig.Create(Bootstrap.Cam.gameObject);
        }
    }
}
