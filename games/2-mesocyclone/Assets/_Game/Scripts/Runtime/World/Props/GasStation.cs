using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MESOCYCLONE
{
    /// <summary>H. The gas station at the goal. Canopy 10 m wide x 14 m long, underside at 5.2 m, centred at lateral
    /// +10.5 so the truck stops between the two pump islands (lateral 8.6 .. 12.4 is clear for the whole length).
    /// Station frame: origin on the ground under the canopy centre, +X away from the road, +Z along it.
    /// One mesh. Materials: matte (atlas, its surface classes are appended by MeshBuilder), metal, light panels, trim
    /// and sign, shop window, then the photo grounds: the asphalt apron and the open concrete get wet with the rain,
    /// the slab under the canopy stays dry (the roof kept the rain off it); then the soffit (the deck's underside, a
    /// lit surface with a painted glow map: no light can reach it from below, so it carries its own light) and the
    /// pump screens. Four real spot lights.</summary>
    public static class GasStation
    {
        public const float Lat = 10.5f;
        // canopy and light-panel layout in the station frame; WorldLibrary.PaintSoffit reads the same numbers, so the
        // halos painted into the soffit's glow map always sit round the panels
        public const float CanopyW = 10f, CanopyL = 14f, SoffitY = 5.2f, PanelX = 1.55f, PanelZ = 4.4f, PanelW = 1.3f, PanelL = 2.6f;
        const int SubApron = 5, SubSlabWet = 6, SubSlabDry = 7, SubSoffit = 8, SubPumpGlow = 9;

        public static void Build(Transform root, PropLights lights)
        {
            double s0 = Director.GoalS + 1.0;
            var ch = new Chunk(s0, Lat, 10);                                                 // must equal the material count of Emit
            MeshBuilder mb = ch.mb;
            Quaternion R = ch.RoadRot(s0);

            // ---- ground: the apron the truck pulls onto, the shop's pad, the slab under the canopy ----
            mb.SetSub(SubApron);
            for (int i = 0; i < 19; i++) ch.Ground(s0 - 135.0 + i * 10.0, s0 - 125.0 + i * 10.0, 5.25, 22.0, 0.03f, Sw.Asphalt);
            mb.SetSub(SubSlabWet);
            ch.Ground(s0 - 19.0, s0 + 21.0, 22.0, 42.0, 0.03f, Sw.Concrete);
            mb.SetSub(SubSlabDry);
            ch.Ground(s0 - 8.0, s0 + 8.0, 5.9, 15.1, 0.045f, Sw.Concrete);

            // ---- canopy, columns, islands, pumps ----
            mb.Push(Vector3.zero, R);
            mb.SetSub(0);
            mb.Swatch(Sw.CanopyWhite);
            // the deck without its underside (-Y skipped): the soffit below is its own lit quad. No battens: the seams
            // of the linear-metal strips are painted into the soffit map, real ones read as black stripes on a lit ceiling
            mb.Box(new Vector3(0f, SoffitY + 0.3f, 0f), new Vector3(CanopyW, 0.6f, CanopyL), 4);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                float x = sx * 2.8f;
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    mb.Swatch(Sw.White);
                    mb.Box(new Vector3(x, 2.6f, sz * 5f), new Vector3(0.3f, 5.2f, 0.3f), 4 | 8);
                    mb.Swatch(Sw.Grey60);                                                    // steel guard around the column foot
                    mb.Box(new Vector3(x, 0.66f, sz * 5f), new Vector3(0.42f, 0.9f, 0.42f), 4);
                    mb.Box(new Vector3(x, 5.16f, sz * 5f), new Vector3(0.4f, 0.04f, 0.4f), 8);  // bracket plate under the deck
                }
                // the island: a concrete curb on the dry slab
                mb.SetSub(SubSlabDry);
                mb.Swatch(Sw.Concrete);
                mb.Box(new Vector3(x, 0.125f, 0f), new Vector3(1.1f, 0.16f, 11.2f), 4);
                mb.SetSub(0);
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Pump(mb, x, sz * 1.9f);
                    Bollard(mb, x - 0.33f, sz * 5.8f);
                    Bollard(mb, x + 0.33f, sz * 5.8f);
                }
            }
            // rubbish drum with a lid
            mb.Swatch(Sw.Grey20);
            mb.Surface(Surf.Paint);
            mb.Cylinder(new Vector3(-2.8f, 0.205f, 3.6f), new Vector3(-2.8f, 1.05f, 3.6f), 0.27f, 0.27f, 14, false);
            mb.Swatch(Sw.Black);
            mb.Cylinder(new Vector3(-2.8f, 1.05f, 3.6f), new Vector3(-2.8f, 1.11f, 3.6f), 0.29f, 0.26f, 14, true);

            // ---- shop behind (lateral +26 .. +38), ice box, parked cars ----
            mb.Swatch(Sw.Cream);
            mb.Surface(Surf.Concrete);                                                       // painted block, not siding
            mb.Box(new Vector3(21.5f, 2.1f, 0f), new Vector3(12f, 4.2f, 18f), 4 | 8);
            mb.Swatch(Sw.Brick);                                                             // brick wainscot under the window line
            mb.Box(new Vector3(15.46f, 0.46f, -2.475f), new Vector3(0.08f, 0.86f, 13.05f), 4);
            mb.Box(new Vector3(15.46f, 0.46f, 7.25f), new Vector3(0.08f, 0.86f, 3.5f), 4);
            mb.Swatch(Sw.Grey40);
            mb.Box(new Vector3(21.5f, 4.4f, 0f), new Vector3(12.3f, 0.4f, 18.3f));
            mb.Box(new Vector3(24f, 5.05f, -4f), new Vector3(1.2f, 0.9f, 1.6f), 4);                // roof air-conditioning unit
            mb.Swatch(Sw.Black);
            mb.Cylinder(new Vector3(24f, 5.5f, -4f), new Vector3(24f, 5.52f, -4f), 0.42f, 0.42f, 16, true);
            mb.Swatch(Sw.Grey40);
            mb.Cylinder(new Vector3(15.42f, 0.03f, -8.85f), new Vector3(15.42f, 4.2f, -8.85f), 0.05f, 0.05f, 8, false);   // downspouts
            mb.Cylinder(new Vector3(15.42f, 0.03f, 8.85f), new Vector3(15.42f, 4.2f, 8.85f), 0.05f, 0.05f, 8, false);
            mb.Swatch(Sw.GlassDark);
            WorldProps.QuadF(mb, new Vector3(15.47f, 0.03f, 4.2f), new Vector3(15.47f, 2.25f, 4.2f), new Vector3(15.47f, 2.25f, 5.4f), new Vector3(15.47f, 0.03f, 5.4f), Vector3.left);
            mb.Swatch(Sw.Aluminium);                                                         // door frame
            mb.Box(new Vector3(15.45f, 1.14f, 4.17f), new Vector3(0.07f, 2.22f, 0.06f));
            mb.Box(new Vector3(15.45f, 1.14f, 5.43f), new Vector3(0.07f, 2.22f, 0.06f));
            mb.Box(new Vector3(15.45f, 2.28f, 4.8f), new Vector3(0.07f, 0.06f, 1.32f));
            mb.Box(new Vector3(15.43f, 1.05f, 4.8f), new Vector3(0.05f, 0.04f, 1.1f));             // push bar
            mb.Swatch(Sw.Grey20);
            mb.Box(new Vector3(15.45f, 2.0f, -3f), new Vector3(0.06f, 2.2f, 0.08f));
            mb.Box(new Vector3(15.45f, 2.0f, 0f), new Vector3(0.06f, 2.2f, 0.08f));
            mb.Box(new Vector3(15.45f, 0.88f, -1.65f), new Vector3(0.07f, 0.06f, 9.8f));          // window sill
            mb.Swatch(Sw.White);
            mb.Box(new Vector3(14.9f, 0.98f, 7.2f), new Vector3(0.9f, 1.9f, 1.8f), 4);              // ice box
            mb.Swatch(Sw.SteelBlue);
            mb.Box(new Vector3(14.9f, 1.99f, 7.2f), new Vector3(0.96f, 0.08f, 1.86f));
            mb.Swatch(Sw.Grey40);
            mb.Box(new Vector3(14.43f, 1.2f, 7.2f), new Vector3(0.04f, 0.05f, 1.2f));             // its handle bar
            mb.Push(R * new Vector3(12.3f, 0.03f, 2.4f), R * Quaternion.Euler(0f, 90f, 0f));
            PropKit.Car(mb, Sw.CarDark, false);
            mb.Push(R * new Vector3(12.6f, 0.03f, -5.6f), R * Quaternion.Euler(0f, 270f, 0f));
            PropKit.Car(mb, Sw.Tan, true);

            // ---- metal: the sign pole ----
            mb.Push(Vector3.zero, R);
            mb.SetSub(1);
            mb.Swatch(Sw.Galvanized);
            mb.Cylinder(new Vector3(8.5f, 0.03f, -24f), new Vector3(8.5f, 9.1f, -24f), 0.18f, 0.15f, 12, false);
            mb.SetSub(0);
            mb.Swatch(Sw.Concrete);
            mb.Cylinder(new Vector3(8.5f, 0.0f, -24f), new Vector3(8.5f, 0.35f, -24f), 0.42f, 0.4f, 16, true);      // footing
            mb.Swatch(Sw.Grey20);
            mb.Box(new Vector3(8.5f, 9.9f, -24f), new Vector3(3.0f, 1.8f, 0.3f));

            // ---- light panels under the canopy (the brightest clean warm light of the game) ----
            mb.SetSub(2);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int k = -1; k <= 1; k++)
                    mb.Box(new Vector3(sx * PanelX, 5.19f, k * PanelZ), new Vector3(PanelW, 0.05f, PanelL), 8);

            // ---- trim: a lit band around the canopy edge and the blank sign panel ----
            mb.SetSub(3);
            mb.Box(new Vector3(0f, 5.5f, 7.01f), new Vector3(10.06f, 0.22f, 0.06f));
            mb.Box(new Vector3(0f, 5.5f, -7.01f), new Vector3(10.06f, 0.22f, 0.06f));
            mb.Box(new Vector3(5.01f, 5.5f, 0f), new Vector3(0.06f, 0.22f, 13.9f));
            mb.Box(new Vector3(-5.01f, 5.5f, 0f), new Vector3(0.06f, 0.22f, 13.9f));
            mb.Box(new Vector3(8.5f, 9.9f, -24f), new Vector3(2.8f, 1.6f, 0.36f));

            // ---- the shop's big lit window ----
            mb.SetSub(4);
            WorldProps.QuadF(mb, new Vector3(15.47f, 0.9f, -6.5f), new Vector3(15.47f, 3.1f, -6.5f), new Vector3(15.47f, 3.1f, 3.2f), new Vector3(15.47f, 0.9f, 3.2f), Vector3.left);

            // ---- the soffit: one quad over the whole underside, UV 0..1 across the canopy so the glow map's halos
            // land round the panels. Same corner order as Box's -Y face, so it faces down. ----
            float hw = CanopyW * 0.5f, hl = CanopyL * 0.5f;
            mb.SetSub(SubSoffit);
            mb.uvRect = new Rect(0f, 0f, 1f, 1f);
            mb.QuadUV(new Vector3(-hw, SoffitY, hl), new Vector3(-hw, SoffitY, -hl), new Vector3(hw, SoffitY, -hl), new Vector3(hw, SoffitY, hl),
                new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f));
            mb.SetSub(0);
            // any geometry added after this point must call Swatch() first: uvRect is reset only there, and it still
            // spans the whole swatch atlas here

            lights.canopyMat = new Material(GameAssets.I.Mat("glow_canopy"));
            lights.trimMat = new Material(GameAssets.I.Mat("glow_canopy"));
            lights.shopMat = new Material(GameAssets.I.Mat("glow_window"));
            lights.soffitMat = new Material(GameAssets.I.Mat("gs_soffit"));                    // PropLights drives its emissive with the canopy
            var pumpGlow = GameAssets.I.Mat("glow_pump");                                     // steady backlit screens: the shared material will do
            var apron = new Material(GameAssets.I.Mat("gs_asphalt"));
            var slabWet = new Material(GameAssets.I.Mat("gs_concrete"));
            var slabDry = new Material(GameAssets.I.Mat("gs_concrete"));
            Land.RegisterWet(apron, Tune.F("world.apronWetDarken", 0.6f), 0.55f);
            Land.RegisterWet(slabWet, 0.72f, 0.45f);
            Land.RegisterWet(slabDry, 0.97f, 0.05f);                                         // only damp, blown in at the edges
            // exactly one material per submesh, in submesh order (10, as the Chunk was made): a missing one would
            // silently drop its submesh, because Route() pads and clears the slots the caller gave no material
            GameObject go = ch.Emit("GasStation", root, true, WorldProps.Atlas, WorldProps.Metal, lights.canopyMat, lights.trimMat, lights.shopMat,
                apron, slabWet, slabDry, lights.soffitMat, pumpGlow);

            // ---- four real spot lights pointing down (the only Light components of the props) ----
            lights.spots = new Light[4];
            int n = 0;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var lg = new GameObject("CanopySpot");
                    lg.transform.SetParent(go.transform, false);
                    lg.transform.localPosition = R * new Vector3(sx * PanelX, 5.1f, sz * 3.3f);
                    lg.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    HDAdditionalLightData hd = lg.AddHDLight(LightType.Spot);
                    Light l = lg.GetComponent<Light>();
                    l.spotAngle = 110f; l.innerSpotAngle = 60f; l.range = 30f;
                    l.lightUnit = LightUnit.Lumen;
                    lights.spotCandela = LightUnitUtils.ConvertIntensity(l, 9000f, LightUnit.Lumen, LightUnit.Candela);
                    l.intensity = lights.spotCandela;
                    l.color = Palette.CanopyWarm;                                            // the same sodium-toward-white as the panels and the soffit
                    hd.affectsVolumetric = true;
                    hd.EnableShadows(false);
                    lights.spots[n++] = l;
                }
        }

        /// <summary>A fuel dispenser on the island at (x, z), faces toward +X and -X (the lanes): red lower cabinet on
        /// a dark plinth, pale upper head with the two lit displays (four bands each) in black bezels, a red band and
        /// the cap, aluminium corner trims, two nozzles in their boots on each face and the hoses hanging from the head.
        /// No text. The screens go on the glow submesh; everything else on the atlas.</summary>
        static void Pump(MeshBuilder mb, float x, float z)
        {
            const float top = 0.205f;                                                        // island top
            mb.Swatch(Sw.Grey20);
            mb.Box(new Vector3(x, top + 0.05f, z), new Vector3(0.9f, 0.1f, 0.55f), 4);
            mb.Swatch(Sw.PumpRed);
            mb.Box(new Vector3(x, top + 0.65f, z), new Vector3(0.85f, 1.1f, 0.5f), 4 | 8);
            mb.Swatch(Sw.CanopyWhite);
            mb.Box(new Vector3(x, top + 1.5f, z), new Vector3(0.8f, 0.6f, 0.48f), 4);
            mb.Swatch(Sw.PumpRed);
            mb.Box(new Vector3(x, top + 1.86f, z), new Vector3(0.86f, 0.12f, 0.52f));
            mb.Swatch(Sw.White);
            mb.Box(new Vector3(x, top + 1.98f, z), new Vector3(0.95f, 0.12f, 0.6f));
            mb.Swatch(Sw.Aluminium);
            for (int cx = -1; cx <= 1; cx += 2)
                for (int cz = -1; cz <= 1; cz += 2)
                    mb.Box(new Vector3(x + cx * 0.425f, top + 0.65f, z + cz * 0.25f), new Vector3(0.035f, 1.1f, 0.035f), 4 | 8);
            for (int f = -1; f <= 1; f += 2)
            {
                float fx = x + f * 0.4f;
                mb.Swatch(Sw.Black);                                                         // bezel, then the screen in front of it
                mb.Box(new Vector3(fx + f * 0.006f, top + 1.47f, z), new Vector3(0.012f, 0.4f, 0.42f), 4 | 8);
                // the screen is a lit LCD (its own glow submesh): a dark glass slot read as a dead pump under the canopy
                mb.SetSub(SubPumpGlow);
                WorldProps.QuadF(mb, new Vector3(fx + f * 0.013f, top + 1.32f, z - 0.17f), new Vector3(fx + f * 0.013f, top + 1.62f, z - 0.17f),
                    new Vector3(fx + f * 0.013f, top + 1.62f, z + 0.17f), new Vector3(fx + f * 0.013f, top + 1.32f, z + 0.17f), new Vector3(f, 0f, 0f));
                mb.SetSub(0);
                // three thin dark row dividers split it into four lit bands: a display without numerals
                mb.Swatch(Sw.Black);
                for (int r = 0; r < 3; r++)
                    mb.Box(new Vector3(fx + f * 0.016f, top + 1.395f + r * 0.075f, z), new Vector3(0.006f, 0.012f, 0.3f));
                mb.Swatch(Sw.Grey40);                                                        // keypad
                mb.Box(new Vector3(fx + f * 0.01f, top + 1.24f, z + 0.12f), new Vector3(0.02f, 0.1f, 0.12f));
                for (int k = -1; k <= 1; k += 2)
                {
                    float nz = z + k * 0.15f, bx = x + f * 0.425f;
                    mb.Swatch(Sw.Black);                                                     // nozzle boot
                    mb.Box(new Vector3(bx + f * 0.045f, top + 0.95f, nz), new Vector3(0.09f, 0.2f, 0.1f));
                    mb.Swatch(Sw.Grey20);                                                    // nozzle handle and spout
                    mb.Box(new Vector3(bx + f * 0.1f, top + 0.93f, nz), new Vector3(0.05f, 0.16f, 0.06f));
                    mb.Cylinder(new Vector3(bx + f * 0.06f, top + 1.04f, nz), new Vector3(bx + f * 0.02f, top + 0.82f, nz), 0.012f, 0.01f, 6, false);
                    // the hose: from the head's side, a sagging loop down past the cabinet and up into the handle
                    mb.Swatch(Sw.Black);
                    mb.Surface(Surf.Rubber);
                    Vector3 h0 = new Vector3(x + f * 0.36f, top + 1.76f, nz + k * 0.1f);
                    Vector3 h1 = new Vector3(x + f * 0.56f, top + 1.2f, nz + k * 0.14f);
                    Vector3 h2 = new Vector3(x + f * 0.6f, top + 0.62f, nz + k * 0.1f);
                    Vector3 h3 = new Vector3(x + f * 0.52f, top + 0.5f, nz + k * 0.04f);
                    Vector3 h4 = new Vector3(bx + f * 0.12f, top + 0.84f, nz);
                    mb.Cylinder(h0, h1, 0.014f, 0.014f, 6, false);
                    mb.Cylinder(h1, h2, 0.014f, 0.014f, 6, false);
                    mb.Cylinder(h2, h3, 0.014f, 0.014f, 6, false);
                    mb.Cylinder(h3, h4, 0.014f, 0.014f, 6, false);
                }
            }
        }

        /// <summary>A steel-pipe bollard, painted safety yellow with one black band, a domed top and a concrete
        /// collar (the scuffs come from the paint class's detail map).</summary>
        static void Bollard(MeshBuilder mb, float x, float z)
        {
            const float y0 = 0.045f, h = 1.1f, r = 0.085f;
            mb.Swatch(Sw.Concrete);
            mb.Cylinder(new Vector3(x, y0, z), new Vector3(x, y0 + 0.07f, z), 0.15f, 0.13f, 14, true);
            mb.Swatch(Sw.SignYellow);
            mb.Cylinder(new Vector3(x, y0, z), new Vector3(x, y0 + h, z), r, r, 14, false);
            mb.Sphere(new Vector3(x, y0 + h, z), r, 14, 6);
            mb.Swatch(Sw.Black);
            mb.Cylinder(new Vector3(x, y0 + 0.78f, z), new Vector3(x, y0 + 0.86f, z), r + 0.002f, r + 0.002f, 14, false);
        }
    }
}
