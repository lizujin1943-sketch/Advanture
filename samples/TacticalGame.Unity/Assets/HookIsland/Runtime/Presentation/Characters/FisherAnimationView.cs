using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Advanture.HookIsland
{
    /// <summary>Owns a cosmetic animation graph. It consumes observations, never gameplay state.</summary>
    public sealed class FisherAnimationView : IDisposable
    {
        private readonly PlayableGraph graph;
        private readonly AnimationMixerPlayable mixer;
        private readonly AnimationClipPlayable[] clips;
        private readonly float[] lengths;
        private readonly float castDuration;
        private int observedCasts;
        private float clock, castAge = float.PositiveInfinity, walkWeight, reelWeight;
        public bool IsCasting => castAge < castDuration;
        public int PresentedCasts { get; private set; }
        public Transform LineGuide { get; }

        public FisherAnimationView(Animator animator, FisherCharacterDefinition definition, int initialCasts)
        {
            if (animator == null || definition == null || !definition.Complete)
                throw new ArgumentException("The fisher needs its authored model and four animation clips.");
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
                if (bone.name == "LineGuide") { LineGuide = bone; break; }
            if (LineGuide == null) throw new ArgumentException("The fisher rig is missing its LineGuide socket.");
            observedCasts = initialCasts; // Restored / late-join history is not a new cast.
            castDuration = Mathf.Clamp(definition.castDuration, .3f, 1f);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("Fisher cosmetic animation");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 4);
            var authored = new[] { definition.idle, definition.walk, definition.reel, definition.cast };
            clips = new AnimationClipPlayable[authored.Length]; lengths = new float[authored.Length];
            for (int i = 0; i < authored.Length; i++)
            {
                clips[i] = AnimationClipPlayable.Create(graph, authored[i]);
                clips[i].SetApplyFootIK(false); clips[i].SetApplyPlayableIK(false);
                clips[i].SetSpeed(0); lengths[i] = Mathf.Max(.001f, authored[i].length);
                graph.Connect(clips[i], 0, mixer, i);
            }
            AnimationPlayableOutput.Create(graph, "Fisher pose", animator).SetSourcePlayable(mixer);
            graph.Play(); Evaluate(false);
        }

        public void Present(float deltaTime, int casts, bool moving, bool reeling, bool interrupted)
        {
            float dt = Mathf.Clamp(deltaTime, 0, .1f);
            bool newCast = casts > observedCasts;
            // A rejected local prediction can lower this counter. Cancel it, then allow the
            // next legitimate cast with the same number; acknowledgement alone never replays it.
            if (casts < observedCasts) castAge = float.PositiveInfinity;
            observedCasts = casts;
            if (interrupted) castAge = float.PositiveInfinity;
            else if (newCast) { castAge = 0; PresentedCasts++; }
            else castAge += dt;
            clock += dt;
            float blend = 1 - Mathf.Exp(-14 * dt);
            walkWeight = Mathf.Lerp(walkWeight, moving && !interrupted ? 1 : 0, blend);
            reelWeight = Mathf.Lerp(reelWeight, reeling && !interrupted ? 1 : 0, blend);
            if (interrupted) walkWeight = reelWeight = 0;
            Evaluate(interrupted);
        }

        private void Evaluate(bool interrupted)
        {
            float castWeight = IsCasting ? 1 - Mathf.SmoothStep(0, 1, (castAge / castDuration - .82f) / .18f) : 0;
            float baseWeight = 1 - castWeight;
            mixer.SetInputWeight(0, baseWeight * (1 - reelWeight) * (1 - walkWeight));
            mixer.SetInputWeight(1, baseWeight * (1 - reelWeight) * walkWeight);
            mixer.SetInputWeight(2, baseWeight * reelWeight);
            mixer.SetInputWeight(3, castWeight);
            for (int i = 0; i < 3; i++) clips[i].SetTime(interrupted ? 0 : clock % lengths[i]);
            clips[3].SetTime(IsCasting ? castAge / castDuration * lengths[3] : 0);
            graph.Evaluate(0);
        }

        public void Dispose() { if (graph.IsValid()) graph.Destroy(); }
    }
}
