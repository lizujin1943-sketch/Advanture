using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>A client-local, flat foot marker. Its forward crest is part of the ring's outline.</summary>
    public sealed class LocalPlayerGroundMarker : IDisposable
    {
        private const float Radius = .43f;
        private const float InnerRadius = Radius * .82f;
        private const float Border = .009f;
        private const float EdgeSoftness = .003f;
        private const float Feather = .006f;
        private static readonly Color Fill = new Color32(121, 233, 87, 255);
        private static readonly Color Outline = new Color32(33, 78, 50, 255);

        private readonly Transform root;
        private readonly Mesh mesh;

        // The presentation owns the shared unlit material; this view owns its mesh and object.
        public LocalPlayerGroundMarker(Transform parent, Material material)
        {
            root = new GameObject("Local player ground marker").transform;
            root.SetParent(parent, false);
            // The ocean reflection already excludes UI; the foot cue is not a world prop.
            root.gameObject.layer = LayerMask.NameToLayer("UI");
            mesh = CreateMesh();
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Hide();
        }

        public void Present(Vector3 surfacePosition, Vector3 displayedForward, float sizeScale)
        {
            displayedForward.y = 0;
            var rotation = displayedForward.sqrMagnitude > .000001f
                ? Quaternion.LookRotation(displayedForward)
                : root.rotation;
            root.SetPositionAndRotation(surfacePosition, rotation);
            root.localScale = Vector3.one * sizeScale;
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (root.gameObject.activeSelf) root.gameObject.SetActive(false);
        }

        private static Mesh CreateMesh()
        {
            const int shoulderSteps = 16;
            const int circleSteps = 112;
            const int bands = 8;
            float shoulderAngle = 35 * Mathf.Deg2Rad;
            var shoulder = new Vector2(Mathf.Sin(shoulderAngle), Mathf.Cos(shoulderAngle));
            // Tangent-matched shoulders stay round; the two curved sides meet at a short tip.
            var shoulderControl = shoulder + new Vector2(-Mathf.Cos(shoulderAngle), Mathf.Sin(shoulderAngle)) * .20f;
            var tipControl = new Vector2(.10f, 1.20f);
            var tip = new Vector2(0, 1.38f);
            var contour = new List<Vector2>(circleSteps + shoulderSteps * 2);
            for (int i = 0; i < shoulderSteps; i++)
                contour.Add(Bezier(tip, tipControl, shoulderControl, shoulder, i / (float)shoulderSteps));
            for (int i = 0; i < circleSteps; i++)
            {
                float angle = Mathf.Lerp(shoulderAngle, Mathf.PI * 2 - shoulderAngle, i / (float)circleSteps);
                contour.Add(new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)));
            }
            for (int i = 0; i < shoulderSteps; i++)
            {
                var point = Bezier(shoulder, shoulderControl, tipControl, tip, i / (float)shoulderSteps);
                contour.Add(new Vector2(-point.x, point.y));
            }

            int count = contour.Count;
            var vertices = new Vector3[count * bands];
            var colors = new Color[vertices.Length];
            var triangles = new int[count * (bands - 1) * 6];
            var clear = new Color(Outline.r, Outline.g, Outline.b, 0);
            var bandColors = new[] { clear, Outline, Outline, Fill, Fill, Outline, Outline, clear };
            // Mesh vertex colors bypass the sRGB conversion applied to material colors.
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                for (int i = 0; i < bandColors.Length; i++) bandColors[i] = bandColors[i].linear;
            for (int i = 0; i < count; i++)
            {
                Vector2 radial = contour[i].normalized;
                float outer = contour[i].magnitude * Radius;
                for (int band = 0; band < bands; band++)
                {
                    float radius;
                    switch (band)
                    {
                        case 0: radius = outer + Feather; break;
                        case 1: radius = outer; break;
                        case 2: radius = outer - Border; break;
                        case 3: radius = outer - Border - EdgeSoftness; break;
                        case 4: radius = InnerRadius + Border + EdgeSoftness; break;
                        case 5: radius = InnerRadius + Border; break;
                        case 6: radius = InnerRadius; break;
                        default: radius = InnerRadius - Feather; break;
                    }
                    int index = band * count + i;
                    vertices[index] = new Vector3(radial.x * radius, 0, radial.y * radius);
                    colors[index] = bandColors[band];
                }
            }
            int triangle = 0;
            for (int band = 0; band < bands - 1; band++)
                for (int i = 0; i < count; i++)
                {
                    int next = (i + 1) % count;
                    int outer = band * count + i, outerNext = band * count + next;
                    int inner = (band + 1) * count + i, innerNext = (band + 1) * count + next;
                    triangles[triangle++] = outer;
                    triangles[triangle++] = outerNext;
                    triangles[triangle++] = inner;
                    triangles[triangle++] = outerNext;
                    triangles[triangle++] = innerNext;
                    triangles[triangle++] = inner;
                }
            var result = new Mesh { name = "Rounded directional player ring" };
            result.vertices = vertices;
            result.colors = colors;
            result.triangles = triangles;
            result.RecalculateNormals();
            result.RecalculateBounds();
            result.UploadMeshData(true);
            return result;
        }

        private static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float u = 1 - t;
            return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
        }

        public void Dispose()
        {
            Release(root.gameObject);
            Release(mesh);
        }

        private static void Release(Object value)
        {
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }
    }
}
