using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>确认状态中的地面物品显示。不参与碰撞、拾取和数量结算，离开房间时全部清理。</summary>
    internal sealed class GroundItemPresentation : IDisposable
    {
        private readonly Transform root;
        private readonly Dictionary<int,Transform> objects=new Dictionary<int,Transform>();
        private readonly Dictionary<ItemKind,Material> materials=new Dictionary<ItemKind,Material>();
        private readonly Material band;
        private readonly Mesh ringMesh;
        private FryingPanArt panArt;
        public GroundItemPresentation(Transform parent)
        {
            root=new GameObject("Ground inventory").transform;root.SetParent(parent,false);
            materials.Add(ItemKind.Wood,Material("Wood bundle",new Color(.56f,.32f,.12f)));
            materials.Add(ItemKind.Fishbone,Material("Bone bundle",new Color(.9f,.88f,.7f)));
            materials.Add(ItemKind.Backpack,Material("Backpack",new Color(.2f,.55f,.43f)));
            materials.Add(ItemKind.Rod,Material("Spare rod",new Color(.24f,.36f,.58f)));
            materials.Add(ItemKind.Apparel,Material("Apparel bundle",new Color(.46f,.42f,.68f)));
            band=Material("Bundle rope",new Color(.94f,.73f,.35f));
            materials.Add(ItemKind.SwimRing,Material("Swim ring",new Color(1,.51f,.12f)));
            ringMesh=SwimmingAppearance.CreateRingMesh();
        }
        public void Synchronize(InventoryState inventory)
        {
            if(inventory==null){Clear();return;}
            var ground=inventory.Items.Where(i=>i.Location==ItemLocation.Ground).ToArray();
            var present=new HashSet<int>(ground.Select(i=>i.Id));
            foreach(int id in objects.Keys.Where(id=>!present.Contains(id)).ToArray())
            {Object.Destroy(objects[id].gameObject);objects.Remove(id);}
            foreach(var item in ground)
            {
                if(!objects.TryGetValue(item.Id,out var view))
                {
                    view=new GameObject("Ground item "+item.Id).transform;view.SetParent(root,false);
                    if(item.Kind==ItemKind.Weapon)
                    {
                        if(panArt==null)panArt=new FryingPanArt(Shader.Find("Sprites/Default"));
                        var pan=panArt.Create(view);pan.localPosition=new Vector3(0,0,-.3f);
                    }
                    else if(item.Kind==ItemKind.SwimRing)
                    {
                        view.gameObject.AddComponent<MeshFilter>().sharedMesh=ringMesh;
                        view.gameObject.AddComponent<MeshRenderer>().sharedMaterial=materials[item.Kind];
                        view.localScale=Vector3.one*.5f;
                    }
                    else
                    {
                    Vector3 size=item.Kind==ItemKind.Rod?new Vector3(.18f,.13f,.75f):new Vector3(.46f,.32f,.4f);
                    PlatformGeometry.Part(view,item.Definition.Name,Vector3.zero,size,materials[item.Kind]);
                    PlatformGeometry.Part(view,"Strap",new Vector3(0,.012f,0),new Vector3(.08f,size.y+.03f,size.z+.025f),band);
                    }
                    objects.Add(item.Id,view);
                }
                // 地面判定仍使用原毫米坐标；少量视觉偏移让同地块的两种材料不会完全遮在一起。
                float offset=item.Kind==ItemKind.Fishbone?.22f:item.Kind==ItemKind.Wood?-.22f:0;
                view.position=new Vector3(item.WorldX/1000f+offset,PlatformGeometry.DeckHeight+.20f,item.WorldZ/1000f);
                view.rotation=Quaternion.Euler(0,item.Id*37%180,0);
            }
        }
        private static Material Material(string name,Color color)
        {
            var result=new Material(Shader.Find("Hook Island/Baked Color Surface")){name=name,color=color};
            result.SetFloat("_Ambient",.4f);result.SetFloat("_SunStrength",.85f);return result;
        }
        public void Clear(){foreach(var view in objects.Values)Object.Destroy(view.gameObject);objects.Clear();}
        public void Dispose(){Clear();panArt?.Dispose();Object.Destroy(root.gameObject);foreach(var material in materials.Values)Object.Destroy(material);Object.Destroy(band);Object.Destroy(ringMesh);}
    }
}
