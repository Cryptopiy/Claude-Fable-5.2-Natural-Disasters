using System;
using UnityEngine;

namespace MESOCYCLONE
{
    /// <summary>The road centreline: a smooth curve defined in code. Heading (degrees, positive = to the right/east)
    /// is a smooth function of the chainage s; the position is its integral, tabulated in double precision.
    /// World frame: +Z north (the road), +X east (right of the road), +Y up.</summary>
    public static class PathDef
    {
        // chainage (m), heading (deg). Long gentle bends only (radius of curvature > 4 km).
        static readonly double[] KeyS = { 0, 1800, 2700, 4400, 5300, 8600, 9400, 12700, 13600, 14300, 15300, 15840 };
        static readonly double[] KeyH = { 0, 0, 3, 3, -2, -2, 1, 1, 9, 9, -4, -4 };

        /// <summary>Chainage of the two crossroads.</summary>
        public static readonly double[] Crossroads = { 3300, 9900 };

        const double Step = 4.0;
        const double Before = 2000.0;                      // straight run-in behind the start
        const double After = 6000.0;                       // straight run-out beyond the goal
        static double[] tabX, tabZ;
        static int count;

        static PathDef() { Build(); }

        static void Build()
        {
            count = (int)Math.Ceiling((Config.PathLength + Before + After) / Step) + 1;
            tabX = new double[count]; tabZ = new double[count];
            // integrate from s = 0 in both directions (midpoint rule; the heading is smooth)
            int i0 = (int)(Before / Step);
            tabX[i0] = 0; tabZ[i0] = 0;
            for (int i = i0 + 1; i < count; i++)
            {
                double h = HeadingRad((i - i0 - 0.5) * Step);
                tabX[i] = tabX[i - 1] + Math.Sin(h) * Step;
                tabZ[i] = tabZ[i - 1] + Math.Cos(h) * Step;
            }
            for (int i = i0 - 1; i >= 0; i--)
            {
                double h = HeadingRad((i - i0 + 0.5) * Step);
                tabX[i] = tabX[i + 1] - Math.Sin(h) * Step;
                tabZ[i] = tabZ[i + 1] - Math.Cos(h) * Step;
            }
        }

        public static double HeadingDeg(double s)
        {
            if (s <= KeyS[0]) return KeyH[0];
            int n = KeyS.Length;
            if (s >= KeyS[n - 1]) return KeyH[n - 1];
            for (int i = 1; i < n; i++)
            {
                if (s <= KeyS[i])
                {
                    double u = (s - KeyS[i - 1]) / (KeyS[i] - KeyS[i - 1]);
                    u = u * u * u * (u * (u * 6 - 15) + 10);       // smootherstep: continuous curvature
                    return KeyH[i - 1] + (KeyH[i] - KeyH[i - 1]) * u;
                }
            }
            return KeyH[n - 1];
        }

        public static double HeadingRad(double s) => HeadingDeg(s) * Math.PI / 180.0;

        /// <summary>Centreline position (x, z) at chainage s, double precision.</summary>
        public static void Position(double s, out double x, out double z)
        {
            double f = (s + Before) / Step;
            if (f < 0) f = 0;
            if (f > count - 1.001) f = count - 1.001;
            int i = (int)f; double u = f - i;
            x = tabX[i] + (tabX[i + 1] - tabX[i]) * u;
            z = tabZ[i] + (tabZ[i + 1] - tabZ[i]) * u;
        }

        /// <summary>World position of a point given in road coordinates: chainage s and lateral offset (right +).</summary>
        public static void RoadToWorld(double s, double lateral, out double x, out double z)
        {
            Position(s, out x, out z);
            double h = HeadingRad(s);
            x += Math.Cos(h) * lateral;
            z -= Math.Sin(h) * lateral;
        }

        /// <summary>Same as RoadToWorld, as a Vector3 relative to a world origin given in doubles (y = height).</summary>
        public static Vector3 RoadToLocal(double s, double lateral, float y, double originX, double originZ)
        {
            RoadToWorld(s, lateral, out double x, out double z);
            return new Vector3((float)(x - originX), y, (float)(z - originZ));
        }

        public static Vector3 Forward(double s)
        {
            double h = HeadingRad(s);
            return new Vector3((float)Math.Sin(h), 0f, (float)Math.Cos(h));
        }

        public static Vector3 Right(double s)
        {
            double h = HeadingRad(s);
            return new Vector3((float)Math.Cos(h), 0f, -(float)Math.Sin(h));
        }

        /// <summary>Signed curvature (1/m) for the steering wheel.</summary>
        public static float Curvature(double s)
        {
            return (float)((HeadingRad(s + 5) - HeadingRad(s - 5)) / 10.0);
        }
    }
}
