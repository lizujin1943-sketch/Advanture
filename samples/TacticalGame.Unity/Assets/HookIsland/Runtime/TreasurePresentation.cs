using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland
{
    internal sealed class TreasurePresentation : IDisposable
    {
        private readonly Transform root;
        private readonly Camera camera;
        private readonly Dictionary<int,Transform> objects=new Dictionary<int,Transform>();
        private readonly Material wood,rare,band;
        public int Count=>objects.Count;
        public TreasurePresentation(Transform parent,Camera camera)
        {
            this.camera=camera;root=new GameObject("Treasure chests").transform;root.SetParent(parent,false);
            wood=Material("Chest oak",new Color(.42f,.23f,.10f));rare=Material("Rare chest",new Color(.23f,.25f,.48f));
            band=Material("Chest brass",new Color(.90f,.64f,.25f));
        }
        private static Material Material(string name,Color color)
        {return new Material(Shader.Find("Hook Island/Baked Color Surface")){name=name,color=color};}
        public void Synchronize(HookState state)
        {
            if(state==null){Clear();return;}
            var visible=state.Treasure.Chests.Where(c=>c.Phase!=TreasurePhase.Waiting).ToArray();
            foreach(int id in objects.Keys.Where(id=>!visible.Any(c=>c.Id==id)).ToArray())
            {Object.Destroy(objects[id].gameObject);objects.Remove(id);}
            foreach(var c in visible)
            {
                if(!objects.TryGetValue(c.Id,out var item))
                {
                    item=new GameObject("Treasure "+c.Id).transform;item.SetParent(root,false);
                    var material=c.Kind==TreasureKind.Rare?rare:wood;
                    PlatformGeometry.Part(item,"Chest body",new Vector3(0,.25f,0),new Vector3(.8f,.5f,.55f),material);
                    PlatformGeometry.Part(item,"Lid",new Vector3(0,.55f,0),new Vector3(.84f,.16f,.59f),material);
                    foreach(float x in new[]{-.27f,.27f})PlatformGeometry.Part(item,"Band",new Vector3(x,.33f,0),new Vector3(.07f,.64f,.61f),band);
                    PlatformGeometry.Part(item,"Clasp",new Vector3(0,.39f,-.31f),new Vector3(.13f,.19f,.05f),band);
                    item.localScale=Vector3.one*(c.Kind==TreasureKind.Large?1.35f:.85f);objects.Add(c.Id,item);
                }
                float y=c.Phase==TreasurePhase.Landed?PlatformGeometry.DeckHeight:.02f+Mathf.Sin(Time.time*1.7f+c.Id)*.035f;
                item.position=new Vector3(c.X/1000f,y,c.Z/1000f);
                item.rotation=Quaternion.Euler(0,c.Id*37,0);
            }
        }
        public void Draw(HookState state,int nearest,float scale,GUIStyle style)
        {
            if(state==null)return;
            foreach(var c in state.Treasure.Chests)
            {
                if(!objects.TryGetValue(c.Id,out var view))continue;
                var screen=camera.WorldToScreenPoint(view.position+Vector3.up*1.05f);if(screen.z<=0)continue;
                string label=c.Kind==TreasureKind.Rare?"稀有小箱":c.Kind==TreasureKind.Small?"小宝箱":"大宝箱";
                if(c.Kind==TreasureKind.Large)
                    label+=c.Phase==TreasurePhase.Landed?(c.Id==nearest?" · E 开启":" · 靠近开启"):" · "+state.Players.Count(p=>p.HookedChest==c.Id+1)+"/2 根钩";
                GUI.Label(new Rect(screen.x/scale-86,(Screen.height-screen.y)/scale,210,28),label,style);
            }
        }
        public void Clear(){foreach(var item in objects.Values)Object.Destroy(item.gameObject);objects.Clear();}
        public void Dispose(){Clear();Object.Destroy(root.gameObject);Object.Destroy(wood);Object.Destroy(rare);Object.Destroy(band);}
    }
}
