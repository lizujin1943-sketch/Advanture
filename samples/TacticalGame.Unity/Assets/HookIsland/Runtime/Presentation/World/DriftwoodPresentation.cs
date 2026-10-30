using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Floating-resource visuals. Bobbing is cosmetic; positions and ownership come from simulation.</summary>
    internal sealed class DriftwoodPresentation : IDisposable
    {
        private readonly Transform root;
        private readonly List<Transform> objects = new List<Transform>();
        private readonly Material wood, rope;

        public DriftwoodPresentation(Transform parent)
        {
            root = new GameObject("Floating resources").transform;
            root.SetParent(parent, false);
            wood = CreateMaterial("Floating wood", new Color(.56f, .29f, .10f));
            rope = CreateMaterial("Floating rope", new Color(.88f, .71f, .41f));
            for (int i = 0; i < DriftwoodMotion.Count; i++)
            {
                var item = new GameObject("Floating wood " + i).transform;
                item.SetParent(root, false);
                float length = i % 3 == 0 ? 1.05f : i % 3 == 1 ? 1.5f : 1.9f;
                int logs = i % 3 == 2 ? 3 : 1;
                for (int j = 0; j < logs; j++)
                    PlatformGeometry.Part(item, "Log", new Vector3((j - (logs - 1) * .5f) * .22f, 0, 0), new Vector3(.22f, .23f, length), wood);
                for (int j = 0; j < 2; j++)
                    PlatformGeometry.Part(item, "Rope band", new Vector3(0, .015f, (j == 0 ? -.28f : .28f) * length), new Vector3(logs * .22f + .025f, .25f, .055f), rope);
                item.rotation = Quaternion.Euler(0, 25 + i * 39, 0);
                item.gameObject.SetActive(false);
                objects.Add(item);
            }
        }

        public void Synchronize(IReadOnlyList<Driftwood> state, float cosmeticTime)
        {
            for (int i = 0; i < objects.Count; i++)
            {
                bool visible = state != null && i < state.Count && state[i].Phase != DriftwoodPhase.Waiting;
                objects[i].gameObject.SetActive(visible);
                if (!visible) continue;
                var item = state[i];
                float y = item.Phase == DriftwoodPhase.Hooked ? .65f : .13f + Mathf.Sin(cosmeticTime * 1.7f + i) * .035f;
                objects[i].position = new Vector3(item.X / 1000f, y, item.Z / 1000f);
            }
        }

        private static Material CreateMaterial(string name, Color color)
        {
            var material = new Material(Shader.Find("Hook Island/Baked Color Surface")) { name = name, color = color };
            material.SetFloat("_Ambient", .4f);
            material.SetFloat("_SunStrength", .85f);
            return material;
        }

        public void Dispose()
        {
            if (root != null) Object.Destroy(root.gameObject);
            Object.Destroy(wood);
            Object.Destroy(rope);
        }
    }
}
