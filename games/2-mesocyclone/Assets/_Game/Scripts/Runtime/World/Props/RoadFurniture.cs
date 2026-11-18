using System.Collections.Generic;
using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>F and G. Road furniture: delineator posts every 100 m on both sides (300 m chunks, fading out at
    /// about 300 m), mile-marker posts, blank warning signs with simple symbols, blank stop signs on the side
    /// roads, a blinking amber beacon over each crossroad, the abandoned car with hazard lights on the shoulder
    /// and the pickup waiting at the second crossroad. No text, no numerals.</summary>
    public static class RoadFurniture
    {
        const float DelLat = 6.2f;
        static readonly float[] BendS = { 1750f, 4350f, 8550f, 12650f, 14250f };

        public static void Build(Transform root, PropLights lights)
        {
            var rng = new Rng(9103);
            var holder = new GameObject("RoadFurniture").transform;
            holder.SetParent(root, false);
            Material atlas = WorldProps.Atlas;

            // ---- delineators and mile markers ----
            for (int k0 = -6; k0 <= 175; k0 += 3)
            {
                var ch = new Chunk((k0 + 1) * 100.0, 0.0);
                for (int k = k0; k < k0 + 3; k++)
                {
                    double s = k * 100.0;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (Blocked(s, side)) continue;
                        float lean = 0f;
                        if (WorldProps.InSwath(s))
                        {
                            if (rng.Chance(0.4f)) continue;
                            if (rng.Chance(0.6f)) lean = rng.Range(15f, 60f);
                        }
                        ch.mb.Push(ch.P(s, side * DelLat), ch.RoadRot(s, lean > 0f ? side * rng.Range(40f, 140f) : 0f) * Quaternion.Euler(lean, 0f, 0f));
                        Delineator(ch.mb);
                    }
                }
                for (int m = 0; m <= 10; m++)
                {
                    double ms = m * 1609.0;
                    if (ms < k0 * 100.0 - 50.0 || ms >= (k0 + 3) * 100.0 - 50.0 || WorldProps.InSwath(ms)) continue;
                    ch.At(ms, 6.9);
                    ch.mb.Swatch(Sw.Galvanized);
                    ch.mb.Box(new Vector3(0f, 0.75f, 0f), new Vector3(0.07f, 1.5f, 0.07f), 4);
                    ch.mb.Swatch(Sw.SignGreen);
                    ch.mb.Box(new Vector3(0f, 1.3f, -0.05f), new Vector3(0.26f, 0.42f, 0.05f));
                }
                if (ch.mb.VertexCount == 0) continue;
                WorldProps.Lod(ch.Emit("Delineators", holder, false, atlas), 290f);
            }

            // ---- warning signs: bends and crossroads ----
            for (int i = 0; i < BendS.Length; i++)
            {
                double s = BendS[i] - 120.0;
                bool right = PathDef.HeadingDeg(BendS[i] + 700.0) > PathDef.HeadingDeg(BendS[i]);
                var ch = new Chunk(s, 6.0);
                Diamond(ch, s, 6.0f, right ? 1 : -1, WorldProps.InSwath(s) ? 38f : 0f);
                WorldProps.Lod(ch.Emit("SignBend", holder, true, atlas), 600f);
            }
            var beaconPos = new List<Vector3>(2); var beaconR = new List<float>(2);
            foreach (double cs in PathDef.Crossroads)
            {
                var ch = new Chunk(cs, 0.0);
                Diamond(ch, cs - 150.0, 6.0f, 0, 0f);
                // blank stop signs: for the traffic of the side roads
                StopSign(ch, cs + 4.6, 7.5f, 90f);
                StopSign(ch, cs - 4.6, -7.5f, -90f);
                // beacon: a mast at the south-east corner with an arm over the road
                ch.At(cs - 7.0, 7.4);
                MeshBuilder mb = ch.mb;
                mb.Swatch(Sw.Galvanized);
                mb.Cylinder(Vector3.zero, new Vector3(0f, 6.3f, 0f), 0.13f, 0.09f, 6, true);
                mb.Box(new Vector3(-3.7f, 6.0f, 0f), new Vector3(7.5f, 0.12f, 0.12f));
                mb.Swatch(Sw.Black);
                mb.Box(new Vector3(-7.4f, 5.66f, 0f), new Vector3(0.36f, 0.56f, 0.3f));
                Vector3 lamp = ch.origin + ch.P(cs - 7.0, 7.4) + ch.RoadRot(cs - 7.0) * new Vector3(-7.4f, 5.66f, -0.2f);
                beaconPos.Add(lamp); beaconR.Add(0.16f);
                WorldProps.Lod(ch.Emit("Crossroad", holder, true, atlas), 900f);
            }
            lights.beaconMat = new Material(GameAssets.I.Mat("glow_amber"));
            lights.beaconDots = LightDots.Create("Beacons", root, lights.beaconMat, beaconPos, beaconR, 0.0012f);

            // ---- G. the abandoned car on the right shoulder, hazard lights blinking ----
            {
                const double cs = 2050.0; const float clat = 5.0f;
                var ch = new Chunk(cs, clat);
                ch.At(cs, clat, 0.02f, 2f);
                PropKit.Car(ch.mb, Sw.CarLight, false);
                WorldProps.Lod(ch.Emit("ShoulderCar", holder, true, atlas), 1500f);
                var hp = new List<Vector3>(4); var hr = new List<float>(4);
                Quaternion q = ch.RoadRot(cs, 2f);
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        hp.Add(ch.origin + q * new Vector3(sx * 0.84f, 0.8f, sz * 2.33f));
                        hr.Add(0.07f);
                    }
                lights.hazardMat = new Material(GameAssets.I.Mat("glow_amber"));
                lights.hazardDots = LightDots.Create("HazardLights", root, lights.hazardMat, hp, hr, 0.0012f);
            }
            // ---- a pickup waiting on the east side road of the second crossroad ----
            {
                double cs = PathDef.Crossroads[PathDef.Crossroads.Length - 1] + 1.6;
                var ch = new Chunk(cs, 13.0);
                ch.At(cs, 13.0, 0.02f, -90f);
                PropKit.Car(ch.mb, Sw.DeepRed, true);
                WorldProps.Lod(ch.Emit("CrossroadPickup", holder, true, atlas), 1500f);
            }
        }

        /// <summary>No post where a driveway, a side road or the gas-station apron meets the road.</summary>
        static bool Blocked(double s, int side)
        {
            foreach (double cs in PathDef.Crossroads) if (System.Math.Abs(s - cs) < 9.0) return true;
            for (int i = 0; i < WorldProps.FarmS.Length; i++)
                if (System.Math.Abs(s - WorldProps.FarmS[i]) < 8.0 && (WorldProps.FarmLat[i] > 0f) == (side > 0)) return true;
            if (side > 0 && (System.Math.Abs(s - Beat05_Funnel.TownS) < 8.0 || System.Math.Abs(s - Beat05_Funnel.ElevS) < 8.0)) return true;
            if (side > 0 && s > Director.GoalS - 150.0 && s < Director.GoalS + 70.0) return true;
            return false;
        }

        static void Delineator(MeshBuilder mb)
        {
            mb.Swatch(Sw.White);
            mb.Surface(Surf.Plastic);                                                        // a flexible plastic post, grimy at the foot
            mb.Box(new Vector3(0f, 0.55f, 0f), new Vector3(0.10f, 1.1f, 0.05f), 4);
            mb.Swatch(Sw.Orange);
            mb.Box(new Vector3(0f, 0.92f, 0f), new Vector3(0.08f, 0.13f, 0.07f));
        }

        /// <summary>Yellow diamond on a post, facing the traffic coming from the south. Symbol: +1 / -1 = an arrow
        /// bending right / left, 0 = a cross (crossroad). No text.</summary>
        static void Diamond(Chunk ch, double s, float lat, int symbol, float lean)
        {
            MeshBuilder mb = ch.mb;
            Quaternion q = ch.RoadRot(s) * Quaternion.Euler(0f, 0f, -lean);
            Vector3 p = ch.P(s, lat);
            mb.Push(p, q);
            mb.Swatch(Sw.Galvanized);
            mb.Box(new Vector3(0f, 1.25f, 0.035f), new Vector3(0.07f, 2.5f, 0.07f), 4);
            mb.Push(p + q * new Vector3(0f, 2.45f, 0f), q * Quaternion.Euler(0f, 0f, 45f));
            mb.Swatch(Sw.SignYellow);
            mb.Box(new Vector3(0f, 0f, -0.02f), new Vector3(0.9f, 0.9f, 0.04f));
            mb.Swatch(Sw.Aluminium);                                                         // bare aluminium back of the blank
            mb.Box(new Vector3(0f, 0f, 0.003f), new Vector3(0.91f, 0.91f, 0.006f));
            mb.Push(p + q * new Vector3(0f, 2.45f, -0.045f), q);
            mb.Swatch(Sw.Black);
            if (symbol == 0)
            {
                mb.Box(Vector3.zero, new Vector3(0.09f, 0.62f, 0.04f));
                mb.Box(new Vector3(0f, 0.02f, -0.004f), new Vector3(0.5f, 0.09f, 0.04f));
                return;
            }
            float d = symbol;
            mb.Box(new Vector3(-0.1f * d, -0.16f, 0f), new Vector3(0.09f, 0.34f, 0.04f));
            mb.Push(p + q * new Vector3(0.0f, 0.12f + 2.45f, -0.049f), q * Quaternion.Euler(0f, 0f, -38f * d));
            mb.Box(new Vector3(0f, 0.02f, 0f), new Vector3(0.09f, 0.36f, 0.04f));
            // arrow head
            Vector3 tip = new Vector3(0f, 0.36f, -0.024f), l = new Vector3(-0.13f, 0.14f, -0.024f), r = new Vector3(0.13f, 0.14f, -0.024f);
            WorldProps.TriF(mb, l, r, tip, Vector3.back);
        }

        /// <summary>Blank red octagon on a post; faceYaw = where its face looks, relative to the road direction.</summary>
        static void StopSign(Chunk ch, double s, float lat, float faceYaw)
        {
            MeshBuilder mb = ch.mb;
            Quaternion q = ch.RoadRot(s, faceYaw);
            Vector3 p = ch.P(s, lat);
            mb.Push(p, q);
            mb.Swatch(Sw.Galvanized);
            mb.Box(new Vector3(0f, 1.1f, -0.035f), new Vector3(0.07f, 2.2f, 0.07f), 4);
            mb.Push(p + q * new Vector3(0f, 2.2f, 0.02f), q * Quaternion.Euler(0f, 0f, 22.5f));
            mb.Swatch(Sw.SignRed);
            mb.Cylinder(new Vector3(0f, 0f, -0.02f), new Vector3(0f, 0f, 0.02f), 0.41f, 0.41f, 8, true);
            mb.Swatch(Sw.Aluminium);                                                         // bare aluminium back (the face looks along +Z)
            mb.Cylinder(new Vector3(0f, 0f, -0.026f), new Vector3(0f, 0f, -0.021f), 0.415f, 0.415f, 8, true);
        }
    }
}
