using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;

namespace Advanture.Tactical.Simulation;

/// <summary>从可站立的烘焙地形建立连通图。所有怪物共用通向同一玩家的路径。</summary>
internal sealed class CompoundNavigation
{
    private const int Spacing = 750;
    private readonly TacticalMap map;
    private readonly int width, depth;
    private readonly bool[] valid;
    private readonly byte[] links;
    private readonly int[] offsets;
    private readonly Dictionary<int, int[]> routes = new Dictionary<int, int[]>();
    private readonly Queue<int> recent = new Queue<int>();

    internal CompoundNavigation(TacticalMap map)
    {
        this.map = map;
        width = (map.Area.MaxX - map.Area.MinX + Spacing - 1) / Spacing;
        depth = (map.Area.MaxZ - map.Area.MinZ + Spacing - 1) / Spacing;
        valid = new bool[checked(width * depth)];
        links = new byte[valid.Length];
        offsets = new[] { 1, width, -1, -width };

        for (int cell = 0; cell < valid.Length; cell++)
        {
            var point = Center(cell);
            valid[cell] = map.CanStand(point.X, point.Z);
        }
        for (int cell = 0; cell < valid.Length; cell++)
        {
            if (!valid[cell]) continue;
            if (cell % width + 1 < width) Connect(cell, 0);
            if (cell / width + 1 < depth) Connect(cell, 1);
        }
    }

    private Point Center(int cell) => new Point(
        map.Area.MinX + (cell % width) * Spacing + Spacing / 2,
        map.Area.MinZ + (cell / width) * Spacing + Spacing / 2);

    private void Connect(int cell, int direction)
    {
        int other = cell + offsets[direction];
        if (!valid[other]) return;
        var start = Center(cell);
        var end = Center(other);
        if (!map.CanWalk(start.X, start.Z, end.X, end.Z)) return;
        links[cell] |= (byte)(1 << direction);
        links[other] |= (byte)(1 << (direction + 2));
    }

    private int Nearest(int x, int z)
    {
        int col = (x - map.Area.MinX) / Spacing, row = (z - map.Area.MinZ) / Spacing;
        long best = long.MaxValue;
        int found = -1;
        for (int r = Math.Max(0, row - 1); r <= Math.Min(depth - 1, row + 1); r++)
        {
            for (int c = Math.Max(0, col - 1); c <= Math.Min(width - 1, col + 1); c++)
            {
                int cell = r * width + c;
                if (!valid[cell]) continue;
                var point = Center(cell);
                long distance = IslandMap.DistanceSquared(x, z, point.X, point.Z);
                if (distance >= best || !map.CanWalk(x, z, point.X, point.Z)) continue;
                found = cell;
                best = distance;
            }
        }
        return found;
    }

    private int[] RoutesTo(int destination)
    {
        // 已完成的路表只读，可供确认态、预测态和从检查点恢复的实例重用。
        lock (routes)
        {
            if (routes.TryGetValue(destination, out var next)) return next;
            next = new int[valid.Length];
            Array.Fill(next, -1);
            var queue = new Queue<int>();
            next[destination] = destination;
            queue.Enqueue(destination);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                for (int dir = 0; dir < offsets.Length; dir++)
                {
                    if ((links[cell] & (1 << dir)) == 0) continue;
                    int adjacent = cell + offsets[dir];
                    if (next[adjacent] >= 0) continue;
                    next[adjacent] = cell;
                    queue.Enqueue(adjacent);
                }
            }
            if (recent.Count == 8) routes.Remove(recent.Dequeue());
            recent.Enqueue(destination);
            routes.Add(destination, next);
            return next;
        }
    }

    internal bool TryRoute(int x, int z, int targetX, int targetZ, out Point goal)
    {
        goal = new Point(targetX, targetZ);
        if (map.CanWalk(x, z, targetX, targetZ)) return true;
        int start = Nearest(x, z), end = Nearest(targetX, targetZ);
        if (start < 0 || end < 0) return false;
        var next = RoutesTo(end);
        if (next[start] < 0) return false;
        goal = Center(start);
        // 在能直达的几个路点中选择最远的，怪物可以沿斜线行走和转角。
        for (int i = 0, cell = start; i < 6 && cell != end; i++)
        {
            cell = next[cell];
            var point = Center(cell);
            if (!map.CanWalk(x, z, point.X, point.Z)) break;
            goal = point;
        }
        return true;
    }
}
