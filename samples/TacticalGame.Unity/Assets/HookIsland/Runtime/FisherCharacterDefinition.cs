using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Authored appearance and clips. This asset has no gameplay or network settings.</summary>
    [CreateAssetMenu(menuName = "Hook Island/Fisher character")]
    public sealed class FisherCharacterDefinition : ScriptableObject
    {
        public GameObject model;
        public AnimationClip idle, walk, cast, reel;
        [Range(.3f, 1f)] public float castDuration = .6f;
        public const string ResourcePath = "FisherCharacter";
        public bool Complete => model != null && idle != null && walk != null && cast != null && reel != null;
    }
}
