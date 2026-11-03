using System.Linq;
using Advanture.HookIsland.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland.Tests
{
    /// <summary>验证实际导入资源的动作隔离与断肢，而不只检查脚本能否编译。</summary>
    public sealed class BossPresentationTests
    {
        [Test] public void VerticalAimPreservesDirectionAndBounds()
        {
            Assert.That(FishingPlayerInput.QuantizeVerticalAim(new Vector3(0, 3, 4)), Is.EqualTo(600));
            Assert.That(FishingPlayerInput.QuantizeVerticalAim(Vector3.up), Is.EqualTo(995));
            Assert.That(FishingPlayerInput.QuantizeVerticalAim(Vector3.down), Is.EqualTo(-995));
        }

        [Test] public void ConcurrentArmClipsDoNotOverwriteEachOtherAndSeveredArmStaysGoneDuringDeath()
        {
            var settings = AssetDatabase.LoadAssetAtPath<BossSettings>("Assets/HookIsland/Data/BossSettings.asset");
            Assert.That(settings, Is.Not.Null, "Run the explicit challenge installer before presentation acceptance.");
            var root = new GameObject("Boss animation acceptance");
            var view = new BossAnimationView(root.transform, settings);
            try
            {
                var limbs = Enumerable.Range(0, 8).Select(i => new BossLimbState(i, 300, 300)).ToArray();
                limbs[0] = new BossLimbState(0, 300, 300, BossAttack.Slam, 36);
                view.Synchronize(State(limbs), 100);
                var a = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "DEF_A01_12");
                Vector3 position = a.position; Quaternion rotation = a.rotation;
                limbs[1] = new BossLimbState(1, 300, 300, BossAttack.Sweep, 45);
                view.Synchronize(State(limbs), 101);
                Assert.That(Vector3.Distance(position, a.position), Is.LessThan(.002f), "A02 action changed A01 geometry");
                Assert.That(Quaternion.Angle(rotation, a.rotation), Is.LessThan(.02f), "A02 action reset A01 rotation");
                limbs[0] = new BossLimbState(0, 0, 300, severTicks: 78);
                view.Synchronize(State(limbs), 102);
                var arm = root.GetComponentsInChildren<Renderer>(true).Single(r => r.name == "AW_Arm_A01");
                Assert.That(arm.enabled, Is.False);
                view.Synchronize(new BossState(BossPhase.Dying, maxHealth: 2400, participantCount: 1, phaseTicks: 80, limbs: limbs), 103);
                Assert.That(arm.enabled, Is.False, "death animation resurrected missing limb");
                var head = root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "DEF_Head");
                Vector3 dyingPosition = head.position;
                // 全队失败后世界帧号不变，BOSS 的独立死亡计数仍必须更新画面。
                view.Synchronize(new BossState(BossPhase.Dying, maxHealth: 2400, participantCount: 1, phaseTicks: 120, limbs: limbs), 103);
                Assert.That(Vector3.Distance(dyingPosition, head.position), Is.GreaterThan(.01f), "frozen world tick froze boss death animation");
            }
            finally { view.Dispose(); Object.DestroyImmediate(root); }
        }

        private static BossState State(BossLimbState[] limbs) => new BossState(BossPhase.Combat, 2000, 2400, 1,
            phaseTicks: 100, limbs: limbs);

        [Test] public void ImportedAnimationAndAuthoritativeJointSamplesShareCoordinatesAndClocks()
        {
            var settings = AssetDatabase.LoadAssetAtPath<BossSettings>("Assets/HookIsland/Data/BossSettings.asset");
            var root = new GameObject("Boss geometry acceptance");
            root.transform.position = new Vector3(0, 0, BossConfig.CenterZ / 1000f);
            var view = new BossAnimationView(root.transform, settings);
            try
            {
                var names = new[] { "A01", "A02", "A03", "A04", "S01", "S02", "S03", "S04" };
                var joints = root.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name);
                for (int scenario = 0; scenario < 8; scenario++)
                {
                    var limbs = Enumerable.Range(0, 8).Select(i => new BossLimbState(i, 300, 300)).ToArray();
                    if (scenario >= 4)
                    {
                        limbs[0] = new BossLimbState(0, 300, 300, BossAttack.Slam, 36);
                        limbs[2] = new BossLimbState(2, 300, 300, BossAttack.Sweep, 47);
                    }
                    var phase = scenario == 1 ? BossPhase.Emerge : scenario == 2 ? BossPhase.Enrage :
                        scenario == 3 ? BossPhase.Dying : BossPhase.Combat;
                    var state = new BossState(phase, 2000, 2400, 1, phaseTicks: 67, limbs: limbs,
                        inkWindupTicks: scenario == 5 || scenario == 7 ? 50 : 0, enraged: scenario >= 6);
                    view.Synchronize(state, (ulong)(100 + scenario));
                    var proxies = BossGeometry.Proxies(state);
                    for (int id = 0; id < 8; id++)
                    {
                        // 末点是骨尾，没有同名 Transform；其余采样点对应每根骨的起点。
                        var points = proxies.Where(p => p.LimbId == id).ToArray();
                        for (int joint = 0; joint < points.Length - 1; joint++)
                        {
                            var p = points[joint]; var expected = new Vector3(p.X, p.Y, p.Z) / 1000f;
                            Assert.That(Vector3.Distance(joints["DEF_" + names[id] + "_" + joint.ToString("00")].position, expected),
                                Is.LessThan(.008f), "Animation/collision mismatch in scenario " + scenario + ", limb " + id + ", joint " + joint);
                        }
                    }
                    var eye = proxies.Single(p => p.Eye);
                    // 碰撞采样记录眼球网格中心，而非骨骼轴心，两者在源模型中本就有偏移。
                    var skin = joints["AW_Eye_Center"].GetComponent<SkinnedMeshRenderer>();
                    var baked = new Mesh();
                    try
                    {
                        skin.BakeMesh(baked); var vertices = baked.vertices;
                        var bounds = new Bounds(skin.transform.TransformPoint(vertices[0]), Vector3.zero);
                        foreach (var vertex in vertices) bounds.Encapsulate(skin.transform.TransformPoint(vertex));
                        Assert.That(Vector3.Distance(bounds.center, new Vector3(eye.X, eye.Y, eye.Z) / 1000f),
                            Is.LessThan(.008f), "Ink or phase layer moved the visible eye away from the weak point in scenario " + scenario);
                    }
                    finally { Object.DestroyImmediate(baked); }
                }
            }
            finally { view.Dispose(); Object.DestroyImmediate(root); }
        }
    }
}
