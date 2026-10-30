using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    [CreateAssetMenu(menuName="Hook Island/Platform Settings")]
    public sealed class PlatformSettings : ScriptableObject
    {
        public FishingSettings fishing;
        [Min(1)] public int wood_tile_cost=4;
        [Min(1)] public int fishbone_tile_cost=4;
        [Tooltip("Reserved cells for buildings / solid props; coordinates are in the 2 m grid.")]
        public Vector2Int[] blockedCells=new Vector2Int[0];
        public IslandMap CreateMap()
        {
            var map=IslandMap.CreatePlatform(wood_tile_cost,fishbone_tile_cost);
            var basis=new IslandMap(map.Shore,map.Pier,map.Spawns,map.FishSpots,
                new PlatformConfig(wood_tile_cost,fishbone_tile_cost,blockedCells.Select(p=>new Point(p.x,p.y))));
            return OceanRules.CreateMap(basis,fishing!=null?fishing.CreateConfig():new FishingConfig(enemies:new EnemyConfig()));
        }
    }
}
