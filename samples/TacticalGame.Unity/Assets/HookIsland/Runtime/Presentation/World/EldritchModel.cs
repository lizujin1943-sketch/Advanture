using System;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Samples authored in-place poses from confirmed combat phases. Never moves authority.</summary>
    public sealed class EldritchModel : IDisposable
    {
        public Transform Root { get; }
        private readonly PlayableGraph graph;
        private readonly AnimationMixerPlayable mixer;
        private readonly AnimationClipPlayable[] clips;
        private readonly float[] lengths, weights;
        private readonly EldritchDefinition definition;
        private EldritchCreature previous;
        private ulong previousStep;
        private bool initialized, moving;
        private float interpolation, hitAge = 1, strikeAge = 1;

        public EldritchModel(Transform parent, EldritchKind kind)
        {
            definition = EldritchCatalog.Get(kind);
            var visual = EldritchVisualCatalog.LoadRequired().species[(int)kind];
            Root = Object.Instantiate(visual.model, parent, false).transform;
            Root.name = definition.Name;
            var animator = Root.GetComponentInChildren<Animator>();
            if (animator == null) throw new InvalidOperationException("Enemy model has no Animator: " + kind);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create("Eldritch cosmetic animation");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, visual.clips.Length);
            clips = new AnimationClipPlayable[visual.clips.Length];
            lengths = new float[clips.Length]; weights = new float[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i] = AnimationClipPlayable.Create(graph, visual.clips[i]);
                clips[i].SetApplyFootIK(false); clips[i].SetApplyPlayableIK(false);
                clips[i].SetSpeed(0); lengths[i] = visual.clips[i].length;
                graph.Connect(clips[i], 0, mixer, i);
            }
            AnimationPlayableOutput.Create(graph, "Enemy pose", animator).SetSourcePlayable(mixer);
            graph.Play();
        }

        public void ResetObservation()
        {
            initialized = false; moving = false; interpolation = 0; hitAge = strikeAge = 1;
            Array.Clear(weights, 0, weights.Length);
        }

        public void Animate(EldritchCreature creature, ulong steps, float death = 0, float deltaTime = 0)
        {
            float dt = Mathf.Clamp(deltaTime, 0, .1f);
            bool reset = !initialized || steps < previousStep || creature.Generation != previous.Generation;
            if (reset) ResetObservation();
            bool observation = reset || steps != previousStep;
            if (observation)
            {
                moving = !reset && (creature.X != previous.X || creature.Z != previous.Z);
                if (!reset && creature.Health < previous.Health && creature.Alive) hitAge = 0;
                if (creature.Phase == EldritchPhase.Active &&
                    (reset || previous.Phase != EldritchPhase.Active || previous.AttackSerial != creature.AttackSerial)) strikeAge = 0;
                previous = creature; previousStep = steps; interpolation = 0;
            }
            else interpolation = Mathf.Min(interpolation + dt, 1f / HookGame.Rate);
            hitAge += dt; strikeAge += dt;
            Root.localScale = Vector3.one * (creature.IsMinion ? .65f : 1);
            var pose = SelectPose(creature, moving);
            // One-tick attacks still need a visible follow-through. This never extends damage or warnings.
            bool followThrough = creature.Phase == EldritchPhase.Recovering && definition.ActiveTicks == 1 && strikeAge < .22f;
            if (followThrough) pose = EldritchPose.Attack;
            int selected = (int)pose;
            float progress = followThrough ? .32f + .68f * strikeAge / .22f : PhaseProgress(creature, pose, death);
            float seconds = pose == EldritchPose.Idle || pose == EldritchPose.Move
                ? ((steps % 18000) / (float)HookGame.Rate + interpolation + creature.Id * .193f) % lengths[selected]
                : Mathf.Clamp01(progress) * lengths[selected];
            clips[selected].SetTime(seconds);

            // Contact poses match the active frame immediately; other transitions soften over ~0.1 s.
            bool snap = reset || dt == 0 || pose == EldritchPose.Attack || pose == EldritchPose.Death;
            float blend = snap ? 1 : 1 - Mathf.Exp(-24 * dt);
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = Mathf.Lerp(weights[i], i == selected ? 1 : 0, blend);
                mixer.SetInputWeight(i, weights[i]);
            }
            // A short hit reaction is cosmetic; ongoing attack clocks retain their phase.
            float hitWeight = creature.Damageable && pose != EldritchPose.Attack && hitAge < .25f
                ? .55f * Mathf.Sin(hitAge / .25f * Mathf.PI) : 0;
            if (hitWeight > 0)
            {
                for (int i = 0; i < weights.Length; i++) mixer.SetInputWeight(i, weights[i] * (1 - hitWeight));
                clips[(int)EldritchPose.Hit].SetTime(hitAge / .25f * lengths[(int)EldritchPose.Hit]);
                mixer.SetInputWeight((int)EldritchPose.Hit, hitWeight);
            }
            graph.Evaluate(0);
            initialized = true;
        }

        private static EldritchPose SelectPose(EldritchCreature creature, bool moving)
        {
            switch (creature.Phase)
            {
                case EldritchPhase.Emerging: return EldritchPose.Emerge;
                case EldritchPhase.Windup: return EldritchPose.Windup;
                case EldritchPhase.Active: return EldritchPose.Attack;
                case EldritchPhase.Recovering: return EldritchPose.Recover;
                case EldritchPhase.Dead: return EldritchPose.Death;
                default: return moving ? EldritchPose.Move : EldritchPose.Idle;
            }
        }

        private float PhaseProgress(EldritchCreature creature, EldritchPose pose, float death)
        {
            float remaining = creature.Timer - interpolation * HookGame.Rate;
            switch (pose)
            {
                case EldritchPose.Emerge: return 1 - remaining / EldritchCatalog.EmergenceTicks;
                case EldritchPose.Windup: return 1 - remaining / definition.WindupTicks;
                case EldritchPose.Attack: return .32f + .68f * (1 - remaining / definition.ActiveTicks);
                case EldritchPose.Recover: return 1 - remaining / definition.RecoveryTicks;
                case EldritchPose.Death: return death;
                default: return 0;
            }
        }

        public void Dispose() { if (graph.IsValid()) graph.Destroy(); Release(Root.gameObject); }
        internal static void Release(Object value) { if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
