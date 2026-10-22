using UnityEngine;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>Low poly tile meshes share one continuous collider, including the bone lattice.</summary>
    public static class PlatformGeometry
    {
        public const float DeckHeight=.45f;
        public static GameObject CreateTile(Transform parent,TileKind kind,Material surface,Material trim)
        {
            var tile=new GameObject(kind==TileKind.Wood?"tile_wood":"tile_fishbone");tile.transform.SetParent(parent,false);
            var collider=tile.AddComponent<BoxCollider>();collider.center=new Vector3(0,.30f,0);collider.size=new Vector3(2,.30f,2);
            for(int i=0;i<2;i++)
            {
                float edge=i==0?-.92f:.92f;
                Part(tile.transform,"Edge beam",new Vector3(edge,.29f,0),new Vector3(.16f,.26f,2),trim);
                Part(tile.transform,"Cross beam",new Vector3(0,.26f,edge),new Vector3(1.84f,.20f,.16f),trim);
            }
            if(kind==TileKind.Wood)
            {
                for(int i=0;i<6;i++)
                {
                    float z=-.825f+i*.33f;
                    Part(tile.transform,"Deck plank",new Vector3(0,.38f,z),new Vector3(1.98f,.14f,.31f),surface);
                    for(int end=0;end<2;end++)Part(tile.transform,"Peg",new Vector3(end==0?-.83f:.83f,.454f,z),new Vector3(.045f,.012f,.045f),trim);
                }
            }
            else
            {
                Part(tile.transform,"Spine",new Vector3(0,.39f,0),new Vector3(.22f,.12f,1.96f),trim);
                for(int i=0;i<7;i++)
                {
                    float z=-.84f+i*.28f;
                    Part(tile.transform,"Bone tread",new Vector3(0,.40f,z),new Vector3(1.96f,.10f,.15f),surface);
                    for(int side=0;side<2;side++)
                    {
                        var rib=Part(tile.transform,"Angled rib",new Vector3(side==0?-.47f:.47f,.32f,z+.05f),new Vector3(.94f,.12f,.10f),trim);
                        rib.localRotation=Quaternion.Euler(0,side==0?-20:20,0);
                    }
                }
            }
            return tile;
        }
        public static Transform Part(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);
            go.transform.localPosition=position;go.transform.localScale=scale;go.GetComponent<Renderer>().sharedMaterial=material;
            var collider=go.GetComponent<Collider>();collider.enabled=false;
            if(Application.isPlaying)Object.Destroy(collider);else Object.DestroyImmediate(collider);
            return go.transform;
        }
    }
}
