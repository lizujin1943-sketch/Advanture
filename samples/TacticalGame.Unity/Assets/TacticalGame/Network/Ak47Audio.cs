using UnityEngine;

namespace Advanture.TacticalGame.NetworkView
{
    /// <summary>Plays the weapon's confirmed presentation events. The owning view owns all sources.</summary>
    internal sealed class Ak47Audio
    {
        private const string ResourcePath = "Weapons/AK47/Audio/";
        private const float HearingRange = 220f;

        // Each group of five uses the complete bank. Adjacent takes also differ at the loop boundary.
        private static readonly int[] TakeOrder =
        {
            1, 3, 0, 4, 2, 4, 0, 2, 1, 3, 2, 4, 1, 3, 0, 3, 1, 4, 0, 2, 0, 2, 3, 1, 4
        };

        // Unity maps the curve's 0..1 domain to maxDistance. Both perspectives stay on the emitter
        // so a moving listener hears the mix change even after a shot has started.
        private static readonly AnimationCurve CloseRolloff = new AnimationCurve(
            At(0, 1f), At(3, 1f), At(9, .78f), At(18, .40f), At(32, .09f), At(48, 0f), At(HearingRange, 0f));
        private static readonly AnimationCurve FarRolloff = new AnimationCurve(
            At(0, 0f), At(4, 0f), At(12, .20f), At(26, .58f), At(42, .56f), At(80, .37f),
            At(145, .21f), At(190, .09f), At(HearingRange, 0f));

        private static ClipBank sharedClips;
        private readonly ClipBank clips;
        private readonly AudioSource close;
        private readonly AudioSource far;
        private readonly AudioSource handling;

        internal Ak47Audio(GameObject owner, bool firstPerson)
        {
            clips = sharedClips ?? (sharedClips = new ClipBank());
            close = AddSource(owner, firstPerson, firstPerson ? .62f : .38f, firstPerson ? 52 : 68);
            handling = AddSource(owner, firstPerson, firstPerson ? .57f : .35f, 120);
            if (firstPerson) return;

            SetRange(close, CloseRolloff);
            far = AddSource(owner, false, .63f, 88);
            SetRange(far, FarRolloff);
            handling.rolloffMode = AudioRolloffMode.Linear;
            handling.minDistance = 1f;
            handling.maxDistance = 12f;
        }

        internal void PlayShot(ulong sequence)
        {
            if (!Application.isPlaying || !close.isActiveAndEnabled) return;
            int index = TakeOrder[(int)(sequence % (ulong)TakeOrder.Length)];
            Play(close, clips.Close[index]);
            if (far != null) Play(far, clips.Far[index]);
        }

        internal void PlayReload(int cue)
        {
            if (!Application.isPlaying || !handling.isActiveAndEnabled || (uint)cue >= (uint)clips.Reload.Length) return;
            Play(handling, clips.Reload[cue]);
        }

        internal void Stop()
        {
            if (close != null) close.Stop();
            if (far != null) far.Stop();
            if (handling != null) handling.Stop();
        }

        private static void Play(AudioSource source, AudioClip clip)
        {
            // PlayOneShot lets each previous take ring out on the same source.
            if (clip != null) source.PlayOneShot(clip);
        }

        private static Keyframe At(float metres, float gain) => new Keyframe(metres / HearingRange, gain);

        private static void SetRange(AudioSource source, AnimationCurve rolloff)
        {
            source.minDistance = 1f;
            source.maxDistance = HearingRange;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, rolloff);
        }

        private static AudioSource AddSource(GameObject owner, bool firstPerson, float volume, int priority)
        {
            var source = owner.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = firstPerson ? 0f : 1f;
            source.spread = 0f;
            source.dopplerLevel = 0f;
            source.bypassReverbZones = true;
            source.volume = volume;
            source.priority = priority;
            return source;
        }

        private sealed class ClipBank
        {
            internal readonly AudioClip[] Close = new AudioClip[5];
            internal readonly AudioClip[] Far = new AudioClip[5];
            internal readonly AudioClip[] Reload;

            internal ClipBank()
            {
                for (int i = 0; i < Close.Length; i++)
                {
                    string suffix = (i + 1).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
                    Close[i] = Load("ShotClose" + suffix);
                    Far[i] = Load("ShotFar" + suffix);
                }
                Reload = new[] { Load("ReloadShift"), Load("ReloadContact"), Load("ReloadSettle") };
            }

            private static AudioClip Load(string name)
            {
                var clip = Resources.Load<AudioClip>(ResourcePath + name);
                if (clip == null) Debug.LogError("Missing AK presentation audio: " + name);
                return clip;
            }
        }
    }
}
