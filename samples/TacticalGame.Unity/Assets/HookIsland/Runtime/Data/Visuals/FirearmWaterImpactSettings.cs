using System;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Bullet-water presentation only; not part of the simulation or saved state.</summary>
    [CreateAssetMenu(menuName = "Hook Island/Firearm water impact")]
    public sealed class FirearmWaterImpactSettings : ScriptableObject
    {
        public const string ResourcePath = "FirearmWaterImpact";
        public FishWaterEffectsSettings surface;
        [Range(8, 64)] public int capacity = 32;
        [Range(4, 16)] public int droplets = 9;
        [Range(.15f, .6f)] public float splashHeight = .32f;
        [Range(.1f, .4f)] public float sprayRadius = .24f;
        [Range(.15f, .5f)] public float crownLifetime = .28f;
        [Range(.6f, 1.8f)] public float rippleLifetime = 1.15f;
        [Range(.6f, 2)] public float rippleDiameter = 1.35f;
        [Range(.05f, .2f)] public float secondRippleDelay = .12f;
        public Color splashTint = new Color(.78f, .95f, 1, .95f);
        public Color rippleTint = new Color(.86f, .98f, 1, .9f);

        public static FirearmWaterImpactSettings LoadRequired()
        {
            var settings = Resources.Load<FirearmWaterImpactSettings>(ResourcePath);
            if (settings == null || settings.surface == null || settings.surface.particleShader == null)
                throw new InvalidOperationException("Missing firearm water impact settings or surface shader.");
            return settings;
        }
    }
}
