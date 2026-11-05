using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Dynamic wood uses authoritative identity but authored rotated positions. No Unity collider drives gameplay.</summary>
    public sealed class TownPresentation : IDisposable
    {
        private readonly Transform root;
        private readonly Material wood,trim;
        private readonly Dictionary<(int,int),GameObject> tiles=new Dictionary<(int,int),GameObject>();
        public TownPresentation(Transform parent)
        {
            root=new GameObject("Town dynamic terrain (confirmed)").transform;root.SetParent(parent,false);
            var shader=Shader.Find("Hook Island/Baked Color Surface");
            wood=new Material(shader){color=new Color(.57f,.32f,.15f)};trim=new Material(shader){color=new Color(.27f,.16f,.10f)};
            Reset();
        }
        public static Vector3 SegmentPosition(TownWoodSegment s)=>new Vector3(s.X/1000f,0,s.Z/1000f);
        public static Quaternion SegmentRotation(TownWoodSegment s)=>Quaternion.LookRotation(new Vector3(s.Direction.X,0,s.Direction.Z));
        public void Synchronize(HookState state)=>Sync(state.Tiles);
        public void Reset()=>Sync(TownLayout.InitialTiles());
        private void Sync(IReadOnlyList<PlatformTile> confirmed)
        {
            var active=new HashSet<(int,int)>();
            foreach(var tile in confirmed)
            {
                var key=(tile.X,tile.Z);active.Add(key);
                if(!tiles.TryGetValue(key,out var go))
                {
                    go=PlatformGeometry.CreateTile(root,TileKind.Wood,wood,trim);go.name=$"Town wood {tile.X},{tile.Z}";
                    go.transform.localPosition=new Vector3(tile.X*2,0,tile.Z*2);
                    foreach(var s in TownLayout.WoodSegments)if(s.CellX==tile.X && s.CellZ==tile.Z)
                    {go.transform.localPosition=SegmentPosition(s);go.transform.localRotation=SegmentRotation(s);go.name=$"Wood band {s.BandId+1} segment {s.SegmentId+1}";break;}
                    tiles.Add(key,go);
                }
                go.SetActive(true);
            }
            foreach(var item in tiles)if(!active.Contains(item.Key))item.Value.SetActive(false);
        }
        public void Dispose(){Release(root.gameObject);Release(wood);Release(trim);}
        private static void Release(UnityEngine.Object o){if(Application.isPlaying)UnityEngine.Object.Destroy(o);else UnityEngine.Object.DestroyImmediate(o);}
    }
}
