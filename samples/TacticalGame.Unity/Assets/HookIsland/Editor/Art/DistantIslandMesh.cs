using System.Collections.Generic;
using UnityEngine;

namespace Advanture.HookIsland.Editor
{
    /// <summary>Builds small static scenery assets in the editor; never creates gameplay terrain.</summary>
    internal sealed class DistantIslandMesh
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();

        public static Mesh Create(int variant)
        {
            var builder = new DistantIslandMesh();
            builder.Land(variant, Vector3.zero, 1);
            builder.Land(variant + 5, new Vector3(17, 0, -2), .45f);
            builder.Palm(new Vector3(-5, 1.5f, 1), 4.2f, .45f + variant);
            builder.Palm(new Vector3(5.8f, 1.1f, -.5f), 3.8f, 2.3f + variant);
            builder.Palm(new Vector3(3.5f, 2.7f, 1.5f), 4.6f, 4.5f + variant);
            var mesh = new Mesh { name = "Distant island " + variant };
            mesh.SetVertices(builder.vertices);
            mesh.SetColors(builder.colors);
            mesh.SetTriangles(builder.triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Land(int variant, Vector3 offset, float scale)
        {
            const int count = 14;
            float phase = variant * 1.37f;
            float[] widths = { 13.2f, 12.7f, 10.2f, 8.5f, 4.6f };
            float[] depths = { 7.8f, 7.5f, 5.7f, 4.8f, 3.1f };
            float[] heights = { -.8f, .035f, .32f, 1.25f, 4.8f };
            Color[] palette =
            {
                new Color(.25f,.63f,.62f), new Color(.68f,.76f,.59f),
                new Color(.72f,.47f,.39f), new Color(.9f,.78f,.54f), new Color(.49f,.31f,.36f)
            };
            var rings = new Vector3[widths.Length, count];
            for (int ring = 0; ring < widths.Length; ring++)
            for (int n = 0; n < count; n++)
            {
                float angle = n * Mathf.PI * 2 / count;
                float outline = 1 + Mathf.Sin(angle * 3 + phase) * .12f + Mathf.Cos(angle * 5 - phase) * .07f;
                float y = heights[ring];
                if (ring >= 3) y *= .8f + .28f * Mathf.Sin(angle * 2 + phase);
                rings[ring, n] = offset + new Vector3(Mathf.Cos(angle) * widths[ring] * outline - ring * .32f,
                    y, Mathf.Sin(angle) * depths[ring] * outline) * scale;
            }
            for (int ring = 0; ring < widths.Length - 1; ring++)
            for (int n = 0; n < count; n++)
            {
                int next = (n + 1) % count;
                Color color = palette[ring];
                Color upper = palette[ring + 1];
                Quad(rings[ring, n], rings[ring + 1, n], rings[ring + 1, next], rings[ring, next], color, upper);
            }
            var peak = offset + new Vector3(-2.3f, 7.1f + variant * .5f, .4f) * scale;
            for (int n = 0; n < count; n++)
                Triangle(rings[4, n], peak, rings[4, (n + 1) % count], palette[4] * (.94f + .12f * Mathf.Cos(n * 2.7f)));
        }

        private void Palm(Vector3 foot, float height, float phase)
        {
            var trunk = new Color(.42f, .44f, .32f);
            var leaf = new Color(.22f, .48f, .36f);
            Vector3 lean = new Vector3(Mathf.Cos(phase), 0, Mathf.Sin(phase)) * height * .2f;
            for (int step = 0; step < 4; step++)
            {
                float a = step / 4f, b = (step + 1) / 4f;
                Vector3 lower = foot + Vector3.up * (height * a) + lean * a * a;
                Vector3 upper = foot + Vector3.up * (height * b) + lean * b * b;
                for (int side = 0; side < 5; side++)
                {
                    float angle = side * Mathf.PI * 2 / 5;
                    float next = (side + 1) * Mathf.PI * 2 / 5;
                    Vector3 v = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    Vector3 w = new Vector3(Mathf.Cos(next), 0, Mathf.Sin(next));
                    Quad(lower + v * (.16f - a * .055f), upper + v * (.16f - b * .055f),
                        upper + w * (.16f - b * .055f), lower + w * (.16f - a * .055f), trunk, trunk);
                }
            }
            Vector3 crown = foot + Vector3.up * height + lean;
            for (int n = 0; n < 7; n++)
            {
                float angle = phase + n * Mathf.PI * 2 / 7;
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 side = Vector3.Cross(Vector3.up, outward) * .42f;
                Vector3 middle = crown + outward * 1.1f + Vector3.up * .32f;
                Vector3 tip = crown + outward * 2.4f - Vector3.up * .6f;
                Triangle(crown, middle + side, middle, leaf);
                Triangle(crown, middle, middle - side, leaf);
                Triangle(middle, middle + side, tip, leaf);
                Triangle(middle - side, middle, tip, leaf);
                // Two-sided geometry keeps these tiny silhouettes visible in sky reflections.
                Triangle(middle, middle + side, crown, leaf);
                Triangle(middle - side, middle, crown, leaf);
                Triangle(tip, middle + side, middle, leaf);
                Triangle(tip, middle, middle - side, leaf);
            }
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color lower, Color upper)
        {
            // Broad colour facets retain the painted low-poly rock style at a distance.
            float variation = Mathf.Sin((a.x + c.z) * 12.7f) * .1f;
            Triangle(a, b, c, Color.Lerp(lower, upper, .3f + variation));
            Triangle(a, c, d, Color.Lerp(lower, upper, .7f - variation));
        }

        private void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color) { Triangle(a, b, c, color, color, color); }

        private void Triangle(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
        {
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            colors.Add(ca.linear); colors.Add(cb.linear); colors.Add(cc.linear);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        }
    }
}
