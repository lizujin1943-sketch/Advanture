using System;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    public enum EldritchPose { Idle, Move, Windup, Attack, Recover, Hit, Emerge, Death }

    [Serializable]
    public sealed class EldritchVisualDefinition
    {
        public EldritchKind kind;
        public GameObject model;
        public AnimationClip[] clips = Array.Empty<AnimationClip>();

        public bool Complete
        {
            get
            {
                if (model == null || clips == null || clips.Length != 8) return false;
                foreach (var clip in clips)
                    if (clip == null || clip.length <= 0) return false;
                return true;
            }
        }
    }

    /// <summary>Authored Blender meshes and in-place clips; contains no simulation rules.</summary>
    [CreateAssetMenu(menuName = "Hook Island/Eldritch visual catalogue")]
    public sealed class EldritchVisualCatalog : ScriptableObject
    {
        public const string ResourcePath = "EldritchVisuals";
        public EldritchVisualDefinition[] species = Array.Empty<EldritchVisualDefinition>();

        public bool Complete
        {
            get
            {
                if (species == null || species.Length != EldritchCatalog.SpeciesCount) return false;
                for (int i = 0; i < species.Length; i++)
                    if (species[i] == null || species[i].kind != (EldritchKind)i || !species[i].Complete) return false;
                return true;
            }
        }

        public static EldritchVisualCatalog LoadRequired()
        {
            var asset = Resources.Load<EldritchVisualCatalog>(ResourcePath);
            if (asset == null || !asset.Complete)
                throw new InvalidOperationException("Missing animated enemy art. Run Tools/Hook Island/Install Animated Eldritch.");
            return asset;
        }
    }
}
