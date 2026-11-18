using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>Driver of the wind farm. Per frame, without allocation: (1) the rotor speed of every turbine as a
    /// pure function of story time (light wind in beat 1, coasting to a stop in the dead calm, spin-up to at most
    /// 20 rpm when the gust front reaches the turbine, feathering and coasting to a stop, a few left idling) and
    /// one rotation write per turning rotor; (2) the one shared emissive colour of all aviation lights (one flash
    /// every 2 s, exactly simultaneous); (3) the swap intact -> wreck of the turbines the wedge has swallowed.
    /// The LightDots component rescales the lights with distance.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class TurbineDriver : MonoBehaviour
    {
        public Transform[] rotor;
        public Quaternion[] baseRot;
        public float[] angle, rpm0, calm, coast0, coast1, rpmMax, hitLag, hold, feather, idle, worldZ, killT;
        public int[] wreckSite;
        public GameObject[] intact, wreck;
        public Material lightMat;
        public LightDots dots;

        bool[] dead;
        int count, wreckCount;
        float frontZ0, frontRate;                 // dust wall: z at T = 100 and metres per second (negative: southward)
        float baseNits, white;
        const float BlinkOn = Config.TurbineBlinkOn, BlinkEdge = 0.08f, LightsFromT = 20f, SpinUp = 6f;   // 0.35 s window, 0.27 s at half level

        public void Alloc(int n, int wrecks)
        {
            count = n; wreckCount = wrecks;
            rotor = new Transform[n]; baseRot = new Quaternion[n];
            angle = new float[n]; rpm0 = new float[n]; calm = new float[n]; coast0 = new float[n]; coast1 = new float[n];
            rpmMax = new float[n]; hitLag = new float[n]; hold = new float[n]; feather = new float[n]; idle = new float[n];
            worldZ = new float[n]; killT = new float[n];
            wreckSite = new int[wrecks]; intact = new GameObject[wrecks]; wreck = new GameObject[wrecks];
            dead = new bool[wrecks];
        }

        void Start()
        {
            // the gust front moves at a constant speed in the score: sample it twice
            var a = new StoryState(); var b = new StoryState();
            Story.Evaluate(100f, ref a); Story.Evaluate(200f, ref b);
            frontZ0 = a.dustWallZ; frontRate = (b.dustWallZ - a.dustWallZ) / 100f;
            if (Mathf.Abs(frontRate) < 0.01f) frontRate = -90f;
            baseNits = Tune.F("world.turbineNits", 9000f);
            white = Tune.F("world.turbineWhite", 30f);
            for (int i = 0; i < count; i++)
                if (rotor[i] != null) rotor[i].localRotation = baseRot[i] * Quaternion.Euler(0f, 0f, angle[i]);
        }

        static float Smooth(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * (3f - 2f * u);
        }

        /// <summary>Rotor speed of turbine i at story time T, revolutions per minute. Never above 20.</summary>
        float Rpm(int i, float T)
        {
            float hit = 100f + (worldZ[i] - frontZ0) / frontRate + hitLag[i];     // the gust front reaches this turbine
            float u = T - hit;
            if (u < 0f)
                return Mathf.Lerp(rpm0[i], calm[i], Smooth((T - coast0[i]) / (coast1[i] - coast0[i])));
            if (u < SpinUp) return Mathf.Lerp(calm[i], rpmMax[i], Smooth(u / SpinUp));
            u -= SpinUp;
            if (u < hold[i]) return rpmMax[i];
            float x = Mathf.Clamp01((u - hold[i]) / feather[i]);
            float k = 1f - x;
            return Mathf.Lerp(idle[i], rpmMax[i], k * k);                          // feathered: fast loss first, long coast
        }

        void Update()
        {
            if (!Director.Running) return;
            float T = Director.T, dt = Director.FrameDt;

            // ---- rotors ----
            if (dt > 0f)
            {
                for (int i = 0; i < count; i++)
                {
                    float rpm = Rpm(i, T);
                    if (rpm < 0.004f) continue;
                    float a = angle[i] + rpm * 6f * dt;                             // 1 rpm = 6 degrees per second
                    if (a >= 360f) a -= 360f;
                    angle[i] = a;
                    rotor[i].localRotation = baseRot[i] * Quaternion.Euler(0f, 0f, a);
                }
            }

            // ---- aviation lights: all at once ----
            float ph = Mathf.Repeat(Director.Ambient, Config.TurbineBlinkPeriod);
            float env = Mathf.SmoothStep(0f, 1f, ph / BlinkEdge) * (1f - Mathf.SmoothStep(0f, 1f, (ph - BlinkOn + BlinkEdge) / BlinkEdge));
            env *= Mathf.Clamp01((T - LightsFromT) / 1.5f);
            bool show = env > 0.002f;
            if (dots != null && dots.Mr.enabled != show) dots.Mr.enabled = show;
            if (show && lightMat != null)
                lightMat.SetColor(WorldProps.IdEmissive, WorldProps.Hdr(Palette.TurbineRed, WorldProps.DotNits(baseNits, white) * env));

            // ---- the wedge takes the turbines in its path ----
            for (int w = 0; w < wreckCount; w++)
            {
                int i = wreckSite[w];
                bool gone = T >= killT[i];
                if (gone == dead[w]) continue;
                dead[w] = gone;
                intact[w].SetActive(!gone);
                wreck[w].SetActive(gone);
                rotor[i].gameObject.SetActive(!gone);
                if (dots != null) dots.Set(i, !gone);
            }
        }
    }
}
