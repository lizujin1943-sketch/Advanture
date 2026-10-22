using System;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Read-only giant, lantern and bank marker. No Unity collider participates in rules.</summary>
    public sealed class FinalePresentation:IDisposable
    {
        private readonly Transform root,body,marker;
        private readonly Material hide,belly,gold,dark,line;
        private readonly FinaleConfig config;
        public int RenderedGiants=>body.gameObject.activeSelf?1:0;
        public bool ShoreMarkerVisible=>marker.gameObject.activeSelf;
        public FinalePresentation(Transform parent,Shader lineShader,FinaleConfig config)
        {
            this.config=config;root=new GameObject("Finale (confirmed)").transform;root.SetParent(parent,false);
            hide=Surface("Giant hide",new Color(.12f,.32f,.37f));belly=Surface("Giant belly",new Color(.62f,.77f,.71f));
            gold=Surface("Lantern glow",new Color(1,.80f,.30f));dark=Surface("Giant eyes",new Color(.025f,.09f,.12f));
            line=new Material(lineShader){name="Lantern stalk",color=new Color(.95f,.72f,.26f)};
            body=new GameObject("Lantern giant fish").transform;body.SetParent(root,false);
            Part(body,"Body",PrimitiveType.Sphere,new Vector3(0,0,-.05f),new Vector3(2.16f,1.18f,2.05f),hide);
            Part(body,"Belly",PrimitiveType.Sphere,new Vector3(0,-.16f,.18f),new Vector3(1.78f,.72f,1.55f),belly);
            Part(body,"Tail",PrimitiveType.Cube,new Vector3(0,.04f,-1),new Vector3(.85f,.48f,.28f),hide);
            Part(body,"Mouth",PrimitiveType.Sphere,new Vector3(0,.06f,.98f),new Vector3(.86f,.28f,.16f),dark);
            for(int i=-1;i<=1;i+=2)
            {
                Part(body,"Fin",PrimitiveType.Cube,new Vector3(i*.98f,-.10f,-.18f),new Vector3(.28f,.12f,.72f),hide);
                Part(body,"Eye",PrimitiveType.Sphere,new Vector3(i*.67f,.30f,.68f),Vector3.one*.24f,belly);
                Part(body,"Pupil",PrimitiveType.Sphere,new Vector3(i*.69f,.32f,.78f),Vector3.one*.12f,dark);
            }
            Part(body,"Lantern",PrimitiveType.Sphere,new Vector3(0,1.35f,.65f),Vector3.one*.38f,gold);
            var stalk=new GameObject("Lantern stalk").AddComponent<LineRenderer>();stalk.transform.SetParent(body,false);stalk.useWorldSpace=false;
            stalk.sharedMaterial=line;stalk.startWidth=stalk.endWidth=.045f;stalk.positionCount=4;
            stalk.SetPositions(new[]{new Vector3(0,.48f,.15f),new Vector3(0,1.25f,.1f),new Vector3(0,1.53f,.42f),new Vector3(0,1.35f,.65f)});
            marker=new GameObject("Suggested shore").transform;marker.SetParent(root,false);
            Part(marker,"Shore marker",PrimitiveType.Cylinder,Vector3.zero,new Vector3(.58f,.012f,.58f),gold);
            body.localScale=Vector3.one*(config.BodyRadius/1200f);Clear();
        }
        public void Synchronize(HookState state)
        {
            if(state==null){Clear();return;}
            var s=state.Finale;bool surfaced=s.Phase==FinalePhase.Surfaced;
            body.gameObject.SetActive(surfaced || s.Phase==FinalePhase.Victory);marker.gameObject.SetActive(surfaced);
            if(!body.gameObject.activeSelf)return;
            body.position=new Vector3(s.X/1000f,.13f+Mathf.Sin(state.Steps*.12f)*.025f,s.Z/1000f);
            var d=FinaleRules.Direction(s.Direction);body.rotation=Quaternion.LookRotation(new Vector3(-d.X,0,-d.Z));
            var anchor=FinaleRules.Anchor(config,s);marker.position=new Vector3(anchor.X/1000f,PlatformGeometry.DeckHeight+.03f,anchor.Z/1000f);
        }
        private static Material Surface(string name,Color color)=>new Material(Shader.Find("Hook Island/Baked Color Surface")){name=name,color=color};
        private static void Part(Transform parent,string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
        {
            var part=GameObject.CreatePrimitive(type);part.name=name;part.transform.SetParent(parent,false);
            part.transform.localPosition=position;part.transform.localScale=scale;part.GetComponent<Renderer>().sharedMaterial=material;
            Object.Destroy(part.GetComponent<Collider>());
        }
        public void Clear(){body.gameObject.SetActive(false);marker.gameObject.SetActive(false);}
        public void Dispose(){Object.Destroy(root.gameObject);Object.Destroy(hide);Object.Destroy(belly);Object.Destroy(gold);Object.Destroy(dark);Object.Destroy(line);}
    }
}
