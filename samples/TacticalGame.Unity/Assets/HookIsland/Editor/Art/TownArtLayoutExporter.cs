using System;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEditor;
using UnityEngine;

namespace Advanture.HookIsland.Editor
{
    /// <summary>
    /// 把正式地图的地面、建筑和障碍导出给 Blender。
    /// 美术使用同一份整数坐标的米制转换，避免在建模脚本里重新维护一套碰撞布局。
    /// </summary>
    public static class TownArtLayoutExporter
    {
        public static string RepositoryRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        public static string SourceDirectory => Path.Combine(RepositoryRoot, "source-assets/environments/drowned-bell-town");

        [MenuItem("Tools/Hook Island/Town Art/Export Authoritative Layout")]
        public static void Export()
        {
            Directory.CreateDirectory(SourceDirectory);
            File.WriteAllText(Path.Combine(SourceDirectory, "layout.json"), CreateJson() + "\n");
            Debug.Log("TOWN_ART_LAYOUT_EXPORTED " + SourceDirectory);
        }

        /// <summary>按固定顺序生成，导入美术时可以重新比较指纹并发现过期的布局。</summary>
        public static string CreateJson()
        {
            var mesh = DrownedBellTownSceneBuilder.BuildGroundMesh();
            try
            {
                var vertices = mesh.vertices;
                var ground = new Rectangle[vertices.Length / 4];
                for (int i = 0; i < ground.Length; i++)
                {
                    var a = vertices[i * 4];
                    var b = vertices[i * 4 + 2];
                    ground[i] = new Rectangle { x = a.x, z = a.z, width = b.x - a.x, depth = b.z - a.z };
                }
                return JsonUtility.ToJson(new Layout
                {
                    ground = ground,
                    buildings = TownLayout.Buildings.Select(b => new Building
                    {
                        id = b.Id, name = b.Name, kind = b.Kind.ToString(), x = b.X / 1000f, z = b.Z / 1000f,
                        width = b.Width / 1000f, depth = b.Depth / 1000f, enterable = b.Enterable,
                        entranceX = b.Entrance.X / 1000f, entranceZ = b.Entrance.Z / 1000f
                    }).ToArray(),
                    obstacles = TownLayout.Obstacles.Select((o, i) => new Obstacle
                    {
                        id = i, x = o.X / 1000f, z = o.Z / 1000f, width = o.Width / 1000f,
                        depth = o.Depth / 1000f, height = o.Height / 1000f,
                        building = TownLayout.Buildings.FirstOrDefault(b => b.Kind != TownBuildingKind.Dock &&
                            Math.Abs(o.X - b.X) <= b.Width / 2 + 250 && Math.Abs(o.Z - b.Z) <= b.Depth / 2 + 250)?.Id ?? 0
                    }).ToArray(),
                    areas = TownLayout.EncounterAreas.Select(a => new Area
                    {
                        id = a.Id, x = a.X / 1000f, z = a.Z / 1000f, width = a.Width / 1000f, depth = a.Depth / 1000f
                    }).ToArray(),
                    roads = TownLayout.Roads.Select(r => new Road
                    {
                        ax = r.Start.X / 1000f, az = r.Start.Z / 1000f, bx = r.End.X / 1000f, bz = r.End.Z / 1000f,
                        halfWidth = r.HalfWidth / 1000f
                    }).ToArray(),
                    ports = TownLayout.SpawnPorts.Select(p => new Port
                    {
                        zone = p.ZoneId, x = p.X / 1000f, z = p.Z / 1000f, water = p.Water
                    }).ToArray(),
                    wood = TownLayout.WoodSegments.Select(s => new Wood
                    {
                        band = s.BandId, segment = s.SegmentId, x = s.X / 1000f, z = s.Z / 1000f,
                        dx = s.Direction.X / 1000f, dz = s.Direction.Z / 1000f
                    }).ToArray()
                }, true);
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }

        [Serializable] private sealed class Layout
        {
            public int version = 1;
            public float groundHeight = .45f;
            public Rectangle[] ground;
            public Building[] buildings;
            public Obstacle[] obstacles;
            public Area[] areas;
            public Road[] roads;
            public Port[] ports;
            public Wood[] wood;
        }
        [Serializable] private struct Rectangle { public float x, z, width, depth; }
        [Serializable] private struct Building
        {
            public int id;
            public string name, kind;
            public float x, z, width, depth, entranceX, entranceZ;
            public bool enterable;
        }
        [Serializable] private struct Obstacle { public int id, building; public float x, z, width, depth, height; }
        [Serializable] private struct Area { public int id; public float x, z, width, depth; }
        [Serializable] private struct Road { public float ax, az, bx, bz, halfWidth; }
        [Serializable] private struct Port { public int zone; public float x, z; public bool water; }
        [Serializable] private struct Wood { public int band, segment; public float x, z, dx, dz; }
    }
}
