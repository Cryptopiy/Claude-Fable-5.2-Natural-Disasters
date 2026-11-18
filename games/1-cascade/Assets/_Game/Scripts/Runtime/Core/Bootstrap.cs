using UnityEngine;

namespace CASCADE
{
    /// <summary>Entry point placed in the scene by the build script. Holds the reference that pulls every generated asset into the build.</summary>
    public class Bootstrap : MonoBehaviour
    {
        public GameAssets assets;

        void Awake()
        {
            GameAssets.I = assets;
            gameObject.AddComponent<GameLoop>();
        }
    }
}
