using System;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    public enum FishPose { Idle, Swim, Burst, Leap, Dive, Hooked, Flop, Bite }
    public enum FishShadowShape { Streamlined, Rounded, Long }

    /// <summary>Authored art only. IDs match the simulation catalogue; clips never contain game events.</summary>
    [Serializable]
    public sealed class FishVisualDefinition
    {
        public string id;
        public GameObject model;
        [Min(.01f)] public float baseLengthMetres;
        public FishShadowShape shadowShape;
        public AnimationClip idle, swim, burst, leap, dive, hooked, flop, bite;
        public bool Complete => model != null && baseLengthMetres > 0 && idle != null && swim != null && burst != null &&
            leap != null && dive != null && hooked != null && flop != null;

        public AnimationClip Clip(FishPose pose)
        {
            switch (pose)
            {
                case FishPose.Idle: return idle;
                case FishPose.Swim: return swim;
                case FishPose.Burst: return burst;
                case FishPose.Leap: return leap;
                case FishPose.Dive: return dive;
                case FishPose.Hooked: return hooked;
                case FishPose.Flop: return flop;
                case FishPose.Bite: return bite;
                default: throw new ArgumentOutOfRangeException(nameof(pose));
            }
        }
    }

    [CreateAssetMenu(menuName = "Hook Island/Fish visual catalogue")]
    public sealed class FishVisualCatalog : ScriptableObject
    {
        public const string ResourcePath = "FishVisuals";
        [Min(.01f)] public float displayScale = 2;
        public Shader shadowShader;
        public Color shadowTint = new Color(.025f, .19f, .23f, .64f);
        public FishVisualDefinition[] species = Array.Empty<FishVisualDefinition>();
        public FishVisualDefinition piranha;

        public bool Complete
        {
            get
            {
                if (displayScale <= 0 || shadowShader == null || species == null || species.Length != FishCatalog.All.Count || piranha == null ||
                    piranha.id != "piranha" || !piranha.Complete || piranha.bite == null) return false;
                for (int i = 0; i < species.Length; i++)
                    if (species[i] == null || !species[i].Complete || species[i].id != FishCatalog.All[i].Id) return false;
                return true;
            }
        }

        public static FishVisualCatalog LoadRequired()
        {
            var result = Resources.Load<FishVisualCatalog>(ResourcePath);
            if (result == null || !result.Complete)
                throw new InvalidOperationException("Missing animated fish art. Run Tools/Hook Island/Install Animated Fish.");
            return result;
        }
    }
}
