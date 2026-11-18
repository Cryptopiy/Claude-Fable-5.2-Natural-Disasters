using UnityEngine;

namespace CASCADE
{
    /// <summary>
    /// Sits next to a clip-less, looping AudioSource created by AudioDirector. With no clip attached, this filter IS the
    /// source: Unity pulls it every DSP block and the director renders the score into the buffer.
    /// </summary>
    public sealed class AudioPump : MonoBehaviour
    {
        internal volatile AudioDirector owner;

        void OnAudioFilterRead(float[] data, int channels)
        {
            AudioDirector d = owner;
            if (d != null) d.PumpRead(data, channels);
        }
    }
}
