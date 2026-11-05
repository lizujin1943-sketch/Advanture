using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>
    /// 按确认帧取样已烘焙的 Blender 动作。先取样全身待机，再只覆盖正在进攻的骨链。
    /// FBX 每个片段可能包含全身曲线，因此不能直接轮流播放，必须保留不属于本动作的姿态。
    /// </summary>
    public sealed class BossAnimationView : IDisposable
    {
        private struct Pose { public Vector3 Position, Scale; public Quaternion Rotation; }
        private readonly GameObject model;
        private readonly GameObject animationRoot;
        private readonly Transform[] joints;
        private readonly Pose[] composed;
        private readonly Dictionary<string, AnimationClip> clips;
        private readonly List<SkinnedMeshRenderer> faces = new List<SkinnedMeshRenderer>();
        private readonly List<float[]> faceWeights = new List<float[]>();
        private readonly Dictionary<int, Renderer[]> limbs = new Dictionary<int, Renderer[]>();
        private readonly Dictionary<int, GameObject[]> stumps = new Dictionary<int, GameObject[]>();
        private readonly Transform[] debris = new Transform[8];
        private readonly Vector3[] debrisOrigins = new Vector3[8];
        private readonly List<Mesh>[] debrisMeshes = new List<Mesh>[8];
        private readonly Transform parent;
        private ulong lastStep = ulong.MaxValue;
        private BossState lastBoss;
        private static readonly string[] LimbNames = { "A01", "A02", "A03", "A04", "S01", "S02", "S03", "S04" };

        public BossAnimationView(Transform parent, BossSettings settings)
        {
            this.parent = parent;
            model = Object.Instantiate(settings.modelPrefab, parent, false);
            model.name = "AbyssalWatcher (confirmed animation)";
            var animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null) throw new InvalidOperationException("BOSS 模型缺少 Animator 骨架入口。");
            animationRoot = animator.gameObject;
            animator.applyRootMotion = false;
            animator.enabled = false; // 时间完全由确认帧决定，Animator 不再自行推进一次。
            joints = animationRoot.GetComponentsInChildren<Transform>(true);
            composed = new Pose[joints.Length];
            clips = settings.clips.Where(c => c != null).ToDictionary(c => ClipKey(c.name), c => c);
            foreach (var key in new[] { "AW_Idle", "AW_Emerge", "AW_Death", "AW_Enrage", "AW_EnragedIdle", "AW_Ink" })
                if (!clips.ContainsKey(key)) throw new InvalidOperationException("BOSS 缺少动作 " + key);
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.updateWhenOffscreen = true;
                if (skin.sharedMesh != null && skin.sharedMesh.blendShapeCount > 0)
                { faces.Add(skin); faceWeights.Add(new float[skin.sharedMesh.blendShapeCount]); }
            }
            for (int i = 0; i < 8; i++)
            {
                string id = LimbNames[i];
                limbs[i] = renderers.Where(r => r.name == "AW_Arm_" + id || r.name == "AW_Membrane_" + id).ToArray();
                stumps[i] = joints.Where(t => t.name == "AW_Stump_" + id).Select(t => t.gameObject).ToArray();
                debrisMeshes[i] = new List<Mesh>();
            }
            Clear();
        }

        private static string ClipKey(string name)
        { int p = name.IndexOf("AW_", StringComparison.Ordinal); return p >= 0 ? name.Substring(p) : name; }

        public void Synchronize(BossState boss, ulong steps)
        {
            bool visible = boss.Active;
            model.SetActive(visible);
            if (!visible) { ClearDebris(); lastStep = ulong.MaxValue; return; }
            // 全队失败时普通世界时钟冻结，BOSS 仍用自己的计数完成死亡或退场。
            // 不能只比较 Steps，否则此时会永远停在同一个动作姿态。
            if (steps == lastStep && lastBoss != null && boss.Phase == lastBoss.Phase &&
                boss.PhaseTicks == lastBoss.PhaseTicks && boss.NoTargetTicks == lastBoss.NoTargetTicks) return;
            lastStep = steps; lastBoss = boss;
            string basis = boss.Phase == BossPhase.Emerge ? "AW_Emerge" : boss.Phase == BossPhase.Enrage ? "AW_Enrage" :
                boss.Phase == BossPhase.Dying ? "AW_Death" : boss.Enraged ? "AW_EnragedIdle" : "AW_Idle";
            bool loop = boss.Phase == BossPhase.Combat;
            double seconds = boss.PhaseTicks / 30d;
            Sample(basis, seconds, loop); Capture(null);
            CaptureFace();
            // 每层只写自身骨链；同时 A01 下砸、A02 横扫时，两条都保留各自的时间。
            if (boss.Phase != BossPhase.Dying && boss.Phase != BossPhase.Emerge)
            {
                foreach (var limb in boss.Limbs)
                {
                    string id = LimbNames[limb.Id];
                    if (limb.Health == 0)
                    { Sample("AW_Sever_" + id, Math.Min(limb.SeverTicks, 9) / 30d, false); Capture("DEF_" + id + "_"); }
                    else if (limb.Attack != BossAttack.None)
                    { Sample("AW_" + limb.Attack + "_" + id, limb.AttackTicks / 30d, false); Capture("DEF_" + id + "_"); }
                }
                if (boss.InkWindupTicks > 0)
                { int inkTick=boss.InkWindupTicks; if(boss.TownArena)inkTick=inkTick<=60?inkTick*36/60:36+inkTick-60;
                    Sample("AW_Ink", inkTick / 30d, false); CaptureHead(); CaptureFace(); }
            }
            Restore(); RestoreFace();
            if(boss.TownArena)RotateAttackChains(boss);
            foreach (var limb in boss.Limbs)
            {
                bool severed = limb.Health == 0 && limb.SeverTicks >= 9;
                foreach (var renderer in limbs[limb.Id]) renderer.enabled = !severed;
                foreach (var stump in stumps[limb.Id]) stump.SetActive(severed);
                UpdateDebris(limb, severed);
            }
        }

        // Rotate the root of each sampled attacking chain around the model origin, exactly
        // as BossPoseData rotates its world-space collision samples. Child joints inherit it.
        private void RotateAttackChains(BossState boss)
        {
            foreach(var limb in boss.Limbs)
            {
                if(limb.Id>=4 || limb.Attack==BossAttack.None || limb.Health==0)continue;
                string prefix="DEF_"+LimbNames[limb.Id]+"_";
                var from=BossGeometry.AttackDirection(limb.Id);var to=BossGeometry.AttackDirection(boss,limb.Id);
                float angle=Vector3.SignedAngle(new Vector3(from.X,0,from.Z),new Vector3(to.X,0,to.Z),Vector3.up);
                var turn=Quaternion.AngleAxis(angle,Vector3.up);
                foreach(var joint in joints)
                {
                    if(!joint.name.StartsWith(prefix,StringComparison.Ordinal) || joint.parent!=null && joint.parent.name.StartsWith(prefix,StringComparison.Ordinal))continue;
                    Vector3 local=model.transform.InverseTransformPoint(joint.position);
                    joint.position=model.transform.TransformPoint(turn*local);
                    joint.rotation=model.transform.rotation*turn*Quaternion.Inverse(model.transform.rotation)*joint.rotation;
                }
            }
        }

        private void Sample(string key, double time, bool loop)
        {
            if (!clips.TryGetValue(key, out var clip)) throw new InvalidOperationException("BOSS 缺少动作 " + key);
            float t = (float)(loop && clip.length > 0 ? time % clip.length : Math.Min(time, clip.length));
            clip.SampleAnimation(animationRoot, t);
        }
        private void Capture(string prefix)
        {
            for (int i = 0; i < joints.Length; i++)
                if (prefix == null || joints[i].name.StartsWith(prefix, StringComparison.Ordinal))
                    composed[i] = new Pose { Position = joints[i].localPosition, Rotation = joints[i].localRotation, Scale = joints[i].localScale };
        }
        private void CaptureHead()
        {
            for (int i = 0; i < joints.Length; i++)
                if (joints[i].name == "DEF_Head" || joints[i].name.StartsWith("DEF_Eye", StringComparison.Ordinal) ||
                    joints[i].name.StartsWith("DEF_Mouth", StringComparison.Ordinal) || joints[i].name.StartsWith("DEF_Petal", StringComparison.Ordinal))
                    composed[i] = new Pose { Position = joints[i].localPosition, Rotation = joints[i].localRotation, Scale = joints[i].localScale };
        }
        private void Restore()
        {
            for (int i = 0; i < joints.Length; i++)
            { joints[i].localPosition = composed[i].Position; joints[i].localRotation = composed[i].Rotation; joints[i].localScale = composed[i].Scale; }
        }
        private void CaptureFace()
        { for (int i = 0; i < faces.Count; i++) for (int j = 0; j < faceWeights[i].Length; j++) faceWeights[i][j] = faces[i].GetBlendShapeWeight(j); }
        private void RestoreFace()
        { for (int i = 0; i < faces.Count; i++) for (int j = 0; j < faceWeights[i].Length; j++) faces[i].SetBlendShapeWeight(j, faceWeights[i][j]); }

        /// <summary>断离瞬间烘一份纯显示网格，脱离原骨架后下沉。它没有伤害或碰撞职责。</summary>
        private void UpdateDebris(BossLimbState limb, bool severed)
        {
            int id = limb.Id;
            if (!severed || limb.SeverTicks >= 78) { ClearDebris(id); return; }
            if (debris[id] == null)
            {
                var root = new GameObject("Detached " + LimbNames[id]).transform; root.SetParent(parent, false);
                debris[id] = root; debrisOrigins[id] = root.localPosition;
                foreach (var renderer in limbs[id])
                {
                    if (!(renderer is SkinnedMeshRenderer skin)) continue;
                    var mesh = new Mesh { name = "Detached snapshot " + id };
                    skin.BakeMesh(mesh); debrisMeshes[id].Add(mesh);
                    var part = new GameObject(renderer.name).transform; part.SetParent(root, false);
                    part.position = renderer.transform.position; part.rotation = renderer.transform.rotation;
                    part.localScale = renderer.transform.lossyScale;
                    part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    part.gameObject.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                }
            }
            float age = (limb.SeverTicks - 9) / 30f;
            debris[id].localPosition = debrisOrigins[id] + Vector3.down * (age * .4f + age * age * 1.4f);
        }
        private void ClearDebris(int id)
        { if (debris[id] != null) Release(debris[id].gameObject); debris[id] = null; foreach (var mesh in debrisMeshes[id]) Release(mesh); debrisMeshes[id].Clear(); }
        private void ClearDebris() { for (int i = 0; i < 8; i++) ClearDebris(i); }
        public void Clear() { model.SetActive(false); ClearDebris(); lastStep = ulong.MaxValue; lastBoss = null; }
        public void Dispose() { ClearDebris(); Release(model); }
        private static void Release(Object value) { if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
