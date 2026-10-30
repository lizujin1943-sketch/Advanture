using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Authored base attributes. The room serializes a pure C# copy at creation.</summary>
    [CreateAssetMenu(menuName = "Hook Island/Character Settings")]
    public sealed class CharacterSettings : ScriptableObject
    {
        [Range(1, 10000)] public int health = 100;
        [Range(.001f, 30)] public float landMoveMetresPerSecond = 3;
        [Range(0, 10000)] public int armor;
        [Range(1, 10000)] public int hunger = 100;
        public CharacterConfig CreateConfig() => new CharacterConfig(health,
            Mathf.RoundToInt(landMoveMetresPerSecond * 1000), armor, hunger);
    }
}
