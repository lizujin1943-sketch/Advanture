using System;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Cosmetic tuning only; never participates in gameplay or state encoding.</summary>
    [CreateAssetMenu(menuName = "Hook Island/Fish water effects")]
    public sealed class FishWaterEffectsSettings : ScriptableObject
    {
        public const string ResourcePath = "FishWaterEffects";
        public Shader particleShader;
        public Color bubbleTint = new Color(.56f, .91f, .97f, .52f);
        public Color foamTint = new Color(.81f, .97f, 1, .76f);
        public Color dropletTint = new Color(.74f, .96f, 1, .94f);
        [Min(.04f)] public float surfaceHeight = .075f;
        [Range(1, 15)] public int observationGapTicks = 12;
        [Range(2, 8)] public int swimBubbleCount = 5;
        [Range(4, 16)] public int takeoffDroplets = 8;
        [Range(4, 20)] public int landingDroplets = 12;
        [Range(.5f, 2.5f)] public float rippleLifetime = 1.8f;
        [Range(1, 2)] public float landingRippleScale = 1.3f;
        [Range(.2f, .6f)] public float crownLifetime = .42f;
        [Range(.5f, 1.5f)] public float effectScale = 1;
        [Header("Surface swimming piranhas")]
        [Range(-.1f, .1f)] public float piranhaBodyHeight = 0;
        [Range(.2f, 1)] public float piranhaWakeLifetime = .5f;
        [Range(0, 1.5f)] public float piranhaWakeStrength = .8f;

        public static FishWaterEffectsSettings LoadRequired()
        {
            var settings = Resources.Load<FishWaterEffectsSettings>(ResourcePath);
            if (settings == null || settings.particleShader == null)
                throw new InvalidOperationException("Missing FishWaterEffects visual settings or particle shader.");
            return settings;
        }
    }
}
