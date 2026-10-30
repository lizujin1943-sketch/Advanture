using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Owns an authored fish instance and an in-place, manually sampled cosmetic pose graph.</summary>
    public sealed class FishModelView : IDisposable
    {
        private readonly GameObject model;
        private readonly PlayableGraph graph;
        private readonly AnimationMixerPlayable mixer;
        private readonly AnimationClipPlayable[] clips;
        private readonly float[] lengths;
        private readonly SkinnedMeshRenderer[] renderers;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private int grade;

        public FishModelView(Transform parent, FishVisualDefinition definition)
        {
            if (parent == null || definition == null || !definition.Complete)
                throw new ArgumentException("An authored fish model and all seven base clips are required.");
            model = Object.Instantiate(definition.model, parent, false);
            model.name = "Animated " + definition.id;
            var animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null) { Release(model); throw new ArgumentException("The fish prefab has no Animator."); }
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            graph = PlayableGraph.Create("Fish pose " + definition.id);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            int count = definition.bite != null ? 8 : 7;
            mixer = AnimationMixerPlayable.Create(graph, count);
            clips = new AnimationClipPlayable[count]; lengths = new float[count];
            for (int i = 0; i < count; i++)
            {
                var clip = definition.Clip((FishPose)i);
                clips[i] = AnimationClipPlayable.Create(graph, clip);
                clips[i].SetApplyFootIK(false); clips[i].SetApplyPlayableIK(false); clips[i].SetSpeed(0);
                lengths[i] = Mathf.Max(.001f, clip.length);
                graph.Connect(clips[i], 0, mixer, i);
            }
            AnimationPlayableOutput.Create(graph, "Fish pose", animator).SetSourcePlayable(mixer);
            graph.Play(); Sample(FishPose.Idle, 0); SetGrade(1);
        }

        public void Sample(FishPose pose, double time, bool normalized = false)
        {
            int selected = (int)pose;
            if (selected < 0 || selected >= clips.Length) throw new ArgumentOutOfRangeException(nameof(pose));
            bool loop = pose == FishPose.Idle || pose == FishPose.Swim || pose == FishPose.Burst || pose == FishPose.Hooked || pose == FishPose.Flop;
            double length = lengths[selected];
            double sampled = normalized ? Math.Max(0, Math.Min(1, time)) * length :
                loop ? Math.Max(0, time) % length : Math.Max(0, Math.Min(length, time));
            for (int i = 0; i < clips.Length; i++) mixer.SetInputWeight(i, i == selected ? 1 : 0);
            clips[selected].SetTime(sampled);
            graph.Evaluate(0);
        }

        public void SetGrade(int value)
        {
            if (value == grade) return;
            grade = value;
            // A subtle silver/gold tint retains authored species colours without cloning materials.
            properties.SetColor("_Color", value >= 3 ? new Color(1, .94f, .78f) : value == 2 ? new Color(.9f, .96f, 1) : Color.white);
            foreach (var renderer in renderers) renderer.SetPropertyBlock(properties);
        }

        public void Dispose()
        {
            if (graph.IsValid()) graph.Destroy();
            if (model != null) { model.SetActive(false); Release(model); }
        }
        private static void Release(Object value) { if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
