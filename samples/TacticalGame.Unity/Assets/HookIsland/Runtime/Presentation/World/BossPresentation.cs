using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>确认态的 BOSS、危险区和唤醒标记。所有几何查询复用模拟规则，不反向修改世界。</summary>
    public sealed class BossPresentation : IDisposable
    {
        private readonly Transform root, beacon;
        private readonly BossAnimationView animation;
        private readonly Material overlayMaterial, beaconMaterial;
        private readonly Mesh overlayMesh;
        private readonly LineRenderer[] health = new LineRenderer[8];
        private readonly LineRenderer inkJet;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> indices = new List<int>();
        private readonly List<Color> colors = new List<Color>();
        private ulong lastStep = ulong.MaxValue;
        private BossState lastBoss;
        public int WarningQuads => vertices.Count / 4;

        public BossPresentation(Transform parent, Shader shader, BossSettings settings)
        {
            root = new GameObject("Abyssal challenge (confirmed)").transform; root.SetParent(parent, false);
            var creature = new GameObject("Sea boss anchor").transform; creature.SetParent(root, false);
            creature.localPosition = new Vector3(BossConfig.CenterX / 1000f, 0, BossConfig.CenterZ / 1000f);
            animation = new BossAnimationView(creature, settings);
            overlayMaterial = new Material(shader) { name = "Boss warnings", color = Color.white };
            beaconMaterial = new Material(Shader.Find("Hook Island/Baked Color Surface"))
                { name = "Abyssal beacon", color = new Color(.17f, .66f, .53f) };
            var area = new GameObject("Attack warnings and ink"); area.transform.SetParent(root, false);
            overlayMesh = new Mesh { name = "Confirmed boss danger areas" };
            area.AddComponent<MeshFilter>().sharedMesh = overlayMesh;
            area.AddComponent<MeshRenderer>().sharedMaterial = overlayMaterial;
            beacon = new GameObject("Awakening marker").transform; beacon.SetParent(root, false);
            beacon.localPosition = new Vector3(BossConfig.BeaconX / 1000f, PlatformGeometry.DeckHeight, BossConfig.BeaconZ / 1000f);
            var ring = MakeLine(beacon, "Beacon ring", .065f, new Color(.36f, 1, .79f));
            ring.positionCount = 49;
            for (int i = 0; i < 49; i++)
            { float a = i * Mathf.PI * 2 / 48; ring.SetPosition(i, new Vector3(Mathf.Cos(a) * .65f, .025f, Mathf.Sin(a) * .65f)); }
            // 一个小柱体标识入口，不阻挡角色、不提供玩法碰撞。
            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "Beacon core"; pillar.transform.SetParent(beacon, false);
            pillar.transform.localPosition = new Vector3(0, .28f, 0); pillar.transform.localScale = new Vector3(.22f, .28f, .22f);
            pillar.GetComponent<Renderer>().sharedMaterial = beaconMaterial; Release(pillar.GetComponent<Collider>());
            for (int i = 0; i < 8; i++) health[i] = MakeLine(root, "Limb " + i + " health", .07f, new Color(.37f, 1, .72f));
            inkJet = MakeLine(root, "Ink discharge", .28f, new Color(.015f, .22f, .17f, .85f));
            Clear();
        }

        public void Synchronize(HookState state)
        {
            if (state == null) { Clear(); return; }
            root.gameObject.SetActive(true);
            beacon.gameObject.SetActive(state.Boss.Phase == BossPhase.Dormant);
            animation.Synchronize(state.Boss, state.Steps);
            // 全灭会冻结世界 Steps；独立退场时钟仍须刷新危险区和断肢显示。
            if (lastStep == state.Steps && lastBoss != null && state.Boss.Phase == lastBoss.Phase &&
                state.Boss.PhaseTicks == lastBoss.PhaseTicks && state.Boss.NoTargetTicks == lastBoss.NoTargetTicks) return;
            lastStep = state.Steps; lastBoss = state.Boss;
            RefreshWarnings(state);
            UpdateLimbHealth(state.Boss);
            int inkAge = state.Boss.InkWindupTicks;
            inkJet.enabled = inkAge >= 36 && inkAge <= 54 && state.Boss.Damageable;
            if (inkJet.enabled)
            {
                inkJet.positionCount = 8;
                for (int i = 0; i < 8; i++)
                {
                    float t = i / 7f;
                    inkJet.SetPosition(i, new Vector3(Mathf.Sin(i * 2 + inkAge) * .22f * t,
                        Mathf.Lerp(2.6f, .52f, t) + Mathf.Sin(t * Mathf.PI) * 1.6f, BossConfig.CenterZ / 1000f - 2 - t * 15));
                }
            }
        }

        private void RefreshWarnings(HookState state)
        {
            vertices.Clear(); indices.Clear(); colors.Clear();
            var boss = state.Boss;
            // 按一米格绘制真实危险区域：开战前没有伤害色块，毁板后墨池仍可覆盖水面。
            if (boss.Damageable)
            {
                for (int x = -22; x <= 22; x++) for (int z = 6; z <= 48; z++)
                {
                    Color color = default; bool any = false;
                    int wx = x * 1000, wz = z * 1000;
                    if ((boss.InkTicks > 0 || boss.InkWindupTicks > 0 && boss.InkWindupTicks <= 36) && BossGeometry.InkContainsXZ(wx, wz))
                    { color = boss.InkTicks > 0 ? new Color(.015f, .10f, .08f, .72f) : new Color(.40f, .79f, .17f, .24f); any = true; }
                    foreach (var limb in boss.Limbs)
                    {
                        if (limb.Health == 0 || limb.Attack == BossAttack.None || limb.Id >= 4) continue;
                        bool hit = false;
                        if (limb.Attack == BossAttack.Slam && limb.AttackTicks <= 47)
                            hit = BossGeometry.SlamContainsXZ(limb.Id, wx, wz);
                        else if (limb.Attack == BossAttack.Sweep && limb.AttackTicks <= 60)
                            for (int t = 36; t <= 60 && !hit; t += 2) hit = BossGeometry.SweepContainsXZ(limb.Id, t, wx, wz);
                        if (!hit) continue;
                        bool impact = limb.Attack == BossAttack.Slam ? limb.AttackTicks >= 45 : limb.AttackTicks >= 36;
                        color = limb.Attack == BossAttack.Slam ? new Color(1, .14f, .06f, impact ? .78f : .38f) :
                            new Color(1, .63f, .12f, impact ? .63f : .26f); any = true;
                    }
                    if (any)
                    {
                        float y = PlatformRules.OnPlatform(state.Tiles, wx, wz) ? PlatformGeometry.DeckHeight + .025f : .15f;
                        Quad(x, y, z, .495f, color);
                    }
                }
            }
            overlayMesh.Clear(); overlayMesh.SetVertices(vertices); overlayMesh.SetColors(colors);
            overlayMesh.SetTriangles(indices, 0); overlayMesh.RecalculateBounds();
        }

        private void UpdateLimbHealth(BossState state)
        {
            foreach (var bar in health) bar.enabled = false;
            if (!state.Damageable) return;
            var proxies = BossGeometry.Proxies(state);
            foreach (var limb in state.Limbs)
            {
                if (limb.Health <= 0) continue;
                BossProxy? point = null; long farthest = 0;
                foreach (var proxy in proxies)
                {
                    if (proxy.LimbId != limb.Id || proxy.Y < 0) continue;
                    long distance = IslandMap.DistanceSquared(proxy.X, proxy.Z, BossConfig.CenterX, BossConfig.CenterZ);
                    if (distance > farthest) { farthest = distance; point = proxy; }
                }
                if (!point.HasValue) continue;
                var p = point.Value; var bar = health[limb.Id]; bar.enabled = true; bar.positionCount = 2;
                var start = new Vector3(p.X / 1000f - .6f, p.Y / 1000f + .6f, p.Z / 1000f);
                bar.SetPosition(0, start); bar.SetPosition(1, start + Vector3.right * (1.2f * limb.Health / limb.MaxHealth));
            }
        }

        private void Quad(float x, float y, float z, float radius, Color color)
        {
            int index = vertices.Count;
            vertices.Add(new Vector3(x - radius, y, z - radius)); vertices.Add(new Vector3(x - radius, y, z + radius));
            vertices.Add(new Vector3(x + radius, y, z + radius)); vertices.Add(new Vector3(x + radius, y, z - radius));
            for (int i = 0; i < 4; i++) colors.Add(color);
            indices.Add(index); indices.Add(index + 1); indices.Add(index + 2);
            indices.Add(index); indices.Add(index + 2); indices.Add(index + 3);
        }
        private LineRenderer MakeLine(Transform parent, string name, float width, Color color)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false; line.sharedMaterial = overlayMaterial; line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color; line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }
        public void Clear() { animation.Clear(); root.gameObject.SetActive(false); lastStep = ulong.MaxValue; lastBoss = null; }
        public void Dispose() { animation.Dispose(); Release(root.gameObject); Release(overlayMesh); Release(overlayMaterial); Release(beaconMaterial); }
        private static void Release(Object value) { if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
