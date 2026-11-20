using System.Collections.Generic;
using Advanture.HookIsland.Simulation;

namespace Advanture.Tactical.Simulation;

/// <summary>把要塞的破洞、地形高度和遮挡交给沉钟镇的怪物系统。</summary>
internal sealed class CompoundEncounterTerrain : IEldritchTerrain
{
    internal static readonly EldritchConfig Settings = new EldritchConfig(speciesMask: 1023 & ~(1 << (int)EldritchKind.DrownedBell));
    private readonly TacticalMap map;
    private readonly int[] eyes = new int[4];

    internal CompoundEncounterTerrain(TacticalMap map) => this.map = map;
    internal void Observe(TacticalPlayer player) => eyes[player.Slot] = map.FeetHeight(player) + 1650;

    public bool CanStand(EldritchHabitat habitat, int x, int z) =>
        habitat != EldritchHabitat.Water && map.CanStand(x, z);
    public bool Segment(EldritchHabitat habitat, int ax, int az, int bx, int bz) =>
        habitat != EldritchHabitat.Water && map.CanWalk(ax, az, bx, bz);
    public bool Visible(int ax, int az, int bx, int bz) =>
        ClearSight(ax, map.GroundHeight(ax, az) + 700, az, bx, map.GroundHeight(bx, bz) + 700, bz);

    private bool ClearSight(int ax, int ay, int az, int bx, int by, int bz)
    {
        const long end = (long)FixedAim.Scale * 1000;
        int dx = bx - ax, dy = by - ay, dz = bz - az;
        if (map.TraceTerrain(ax, ay, az, dx, dy, dz, end) < end) return false;
        foreach (var wall in map.Obstacles)
        {
            if (TacticalGame.Intersect(ax, ay, az, dx, dy, dz, wall.MinX, wall.MinY, wall.MinZ,
                wall.MaxX, wall.MaxY, wall.MaxZ, end, out _)) return false;
        }
        return true;
    }

    public bool TrySpawn(EldritchCreature creature, IReadOnlyList<Fisher> players, IReadOnlyList<EldritchCreature> roster, out Point point)
    {
        // 只在上次真正生成后轮换入口。世代数随快照保存，回滚后选择的入口一致。
        ulong generations = 0;
        foreach (var other in roster) generations += other.Generation;
        var ports = map.MonsterEntrances;
        for (int n = 0; n < ports.Count; n++)
        {
            var port = ports[(int)((generations + (uint)n) % (uint)ports.Count)];
            if (!CanStand(EldritchCatalog.Get(creature.Kind).Habitat, port.X, port.Z)) continue;
            if (Guarded(port, players) || Occupied(port, roster)) continue;
            point = port;
            return true;
        }
        point = default;
        return false;
    }

    private bool Guarded(Point port, IReadOnlyList<Fisher> players)
    {
        int height = map.GroundHeight(port.X, port.Z) + 700;
        foreach (var player in players)
        {
            if (player.Unavailable || player.WaitingToJoin) continue;
            if (IslandMap.DistanceSquared(player.X, player.Z, port.X, port.Z) < 8000L * 8000 ||
                ClearSight(player.X, eyes[player.Slot], player.Z, port.X, height, port.Z)) return true;
        }
        return false;
    }

    private static bool Occupied(Point port, IReadOnlyList<EldritchCreature> roster)
    {
        foreach (var other in roster)
        {
            if (other.Damageable && IslandMap.DistanceSquared(port.X, port.Z, other.X, other.Z) < 1000L * 1000)
                return true;
        }
        return false;
    }

    public int Target(EldritchCreature creature, IReadOnlyList<Fisher> players, out Point goal)
    {
        int selected = -1;
        long nearest = long.MaxValue;
        goal = new Point(creature.X, creature.Z);
        foreach (var player in players)
        {
            if (!EldritchSystem.Eligible(player)) continue;
            long distance = IslandMap.DistanceSquared(creature.X, creature.Z, player.X, player.Z);
            if (distance >= nearest || !map.Navigation!.Value.TryRoute(creature.X, creature.Z, player.X, player.Z, out var next)) continue;
            nearest = distance;
            selected = player.Slot;
            goal = next;
        }
        return selected;
    }
}
