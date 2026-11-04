using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>只读手持与飞行状态。预览、警示和爆炸都是表现，不通过碰撞回调造成伤害。</summary>
    public sealed class GrenadeView : IDisposable
    {
        private readonly Transform root;
        private readonly Material body,metal,lineMaterial,flashMaterial,waterMaterial;
        private readonly LineRenderer arc,landing,blastRange;
        private readonly Dictionary<int,Transform> held=new Dictionary<int,Transform>();
        private readonly Dictionary<int,Projectile> flying=new Dictionary<int,Projectile>();
        private readonly List<Burst> bursts=new List<Burst>();
        private readonly AudioSource audio;
        private readonly AudioClip explosion,ping;
        private sealed class Projectile {internal Transform Model;internal LineRenderer Warning;}
        private sealed class Burst {internal Transform Root;internal float Start;internal bool Water;internal List<Transform> Streaks=new List<Transform>();}
        public GrenadeView(Transform parent,Shader lines)
        {
            root=new GameObject("Grenades (presentation only)").transform;root.SetParent(parent,false);
            body=new Material(Shader.Find("Hook Island/Baked Color Surface")){color=new Color(.27f,.32f,.12f)};
            metal=new Material(body){color=new Color(.12f,.14f,.16f)};
            lineMaterial=new Material(lines);flashMaterial=new Material(lines){color=new Color(1,.58f,.12f)};waterMaterial=new Material(lines){color=new Color(.65f,.93f,1)};
            arc=Line("Throw arc",.018f);landing=Line("Landing uncertainty",.026f);blastRange=Line("Blast preview",.012f);
            HidePreview();audio=root.gameObject.AddComponent<AudioSource>();audio.spatialBlend=0;audio.volume=.5f;
            explosion=Sound(false);ping=Sound(true);
        }
        private LineRenderer Line(string name,float width,Transform parent=null)
        {
            var g=new GameObject(name);g.transform.SetParent(parent??root,false);var l=g.AddComponent<LineRenderer>();
            l.sharedMaterial=lineMaterial;l.useWorldSpace=true;l.startWidth=l.endWidth=width;l.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return l;
        }
        private static void ColorLine(LineRenderer line,Color color){line.startColor=line.endColor=color;}
        private static void Circle(LineRenderer line,Vector3 center,float radius)
        {
            const int count=64;line.positionCount=count+1;
            for(int i=0;i<=count;i++){float a=i*Mathf.PI*2/count;line.SetPosition(i,center+new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius));}
        }
        public void Preview(GrenadeState plan,Vector3 target,bool valid)
        {
            Color c=valid?Color.white:new Color(1,.2f,.15f);landing.enabled=blastRange.enabled=true;ColorLine(landing,c);ColorLine(blastRange,new Color(c.r,c.g,c.b,.28f));
            target.y+=.065f;Circle(landing,target,GrenadeConfig.Default.Scatter/1000f);Circle(blastRange,target,GrenadeConfig.Default.Radius/1000f);
            arc.enabled=plan.FlightTicks>0;
            if(!arc.enabled)return;ColorLine(arc,new Color(c.r,c.g,c.b,.75f));arc.positionCount=33;
            for(int i=0;i<=32;i++){GrenadeRules.Position(plan,i,32,out int x,out int y,out int z);arc.SetPosition(i,new Vector3(x,y,z)/1000f);}
        }
        public void HidePreview(){arc.enabled=landing.enabled=blastRange.enabled=false;}
        public void Synchronize(HookState state,FishingPresentation players,ushort local,Camera camera,bool nearView)
        {
            if(state==null)return;
            foreach(var p in state.Players)
            {
                bool carrying=state.Tackle?.Inventory.ActiveItem(p.Slot)?.Kind==ItemKind.Throwable && !p.Vitals.Downed && !p.Unavailable && !state.RoundFinished;
                if(!held.TryGetValue(p.Slot,out var model) && carrying){model=Model("Held grenade "+p.Slot);held.Add(p.Slot,model);}
                if(model==null)continue;model.gameObject.SetActive(carrying);if(!carrying)continue;
                Vector3 feet=players.PlayerPosition(p.Slot);var facing=new Vector3(p.AimX/1000f-feet.x,0,p.AimZ/1000f-feet.z).normalized;
                if(facing.sqrMagnitude<.01f)facing=Vector3.forward;
                bool close=p.Slot==local && nearView;
                if(close){model.position=camera.transform.TransformPoint(new Vector3(.24f,-.25f,.55f));model.rotation=camera.transform.rotation*Quaternion.Euler(10,0,-20);model.localScale=Vector3.one;}
                else
                {
                    float prepare=p.Grenade.Active?1-p.Grenade.RemainingTicks/(float)GrenadeConfig.Default.ActionTicks:0;
                    model.position=feet+Vector3.up*(1+Mathf.Sin(prepare*Mathf.PI)*.2f)+facing*(.3f+prepare*.2f)+Vector3.Cross(Vector3.up,facing)*.22f;
                    model.rotation=Quaternion.LookRotation(facing)*Quaternion.Euler(-prepare*80,0,-10);model.localScale=Vector3.one;
                }
            }
            foreach(var pair in held)if(pair.Key>=state.Players.Count)pair.Value.gameObject.SetActive(false);
            var ids=new HashSet<int>(state.Grenades.Select(g=>g.Id));
            foreach(int id in flying.Keys.Where(k=>!ids.Contains(k)).ToArray()){Object.Destroy(flying[id].Model.gameObject);Object.Destroy(flying[id].Warning.gameObject);flying.Remove(id);}
            foreach(var g in state.Grenades)
            {
                if(!flying.TryGetValue(g.Id,out var v)){v=new Projectile{Model=Model("Flying grenade "+g.Id),Warning=Line("Grenade danger "+g.Id,.035f)};flying.Add(g.Id,v);}
                v.Model.position=new Vector3(g.X,g.Y,g.Z)/1000f;
                v.Model.rotation=Quaternion.Euler(g.Phase==GrenadePhase.Landed?85:g.Age*31,g.Id*27,g.Phase==GrenadePhase.Landed?20:g.Age*17);
                v.Warning.enabled=g.Phase==GrenadePhase.Landed;
                if(v.Warning.enabled)
                {
                    float remaining=(g.LandedTick+(ulong)GrenadeConfig.Default.FuseTicks-state.Steps)/(float)GrenadeConfig.Default.FuseTicks;
                    ColorLine(v.Warning,new Color(1,.15f,.06f,Mathf.Lerp(.35f,.85f,(Mathf.Sin(Time.unscaledTime*24)+1)*.5f)));
                    Circle(v.Warning,new Vector3(g.X/1000f,g.Water?.06f:g.Y/1000f+.04f,g.Z/1000f),GrenadeConfig.Default.Radius/1000f*(.96f+.04f*remaining));
                }
            }
            for(int i=bursts.Count-1;i>=0;i--)
            {
                var b=bursts[i];float age=Time.unscaledTime-b.Start;
                if(age>.65f){Object.Destroy(b.Root.gameObject);bursts.RemoveAt(i);continue;}
                float fade=1-age/.65f;
                for(int n=0;n<b.Streaks.Count;n++)
                {
                    float a=n*Mathf.PI*2/b.Streaks.Count;var direction=new Vector3(Mathf.Cos(a),b.Water?1.2f:.25f+(n%3)*.3f,Mathf.Sin(a));
                    b.Streaks[n].localPosition=direction*(age*7);
                    b.Streaks[n].localScale=Vector3.one*(b.Water?.14f:.18f)*fade;
                }
            }
        }
        public void Confirm(HookGameplayEvent e,Vector3 listener)
        {
            float distance=Vector2.Distance(new Vector2(e.X/1000f,e.Z/1000f),new Vector2(listener.x,listener.z));
            if(e.Kind==HookGameplayEventKind.GrenadeLanded && distance<12)audio.PlayOneShot(ping,.55f*(1-distance/15));
            if(e.Kind!=HookGameplayEventKind.GrenadeExploded)return;
            if(distance<28)audio.PlayOneShot(explosion,Mathf.Clamp01(1-distance/30));
            var b=new Burst{Root=new GameObject(e.Value==1?"Water explosion":"Grenade explosion").transform,Start=Time.unscaledTime,Water=e.Value==1};
            b.Root.SetParent(root,false);b.Root.position=new Vector3(e.X/1000f,e.Value==1?.1f:e.Y/1000f,e.Z/1000f);
            for(int n=0;n<24;n++){var shard=Primitive(PrimitiveType.Sphere,b.Root,Vector3.zero,Vector3.one*.16f,b.Water?waterMaterial:flashMaterial);b.Streaks.Add(shard);}
            bursts.Add(b);
        }
        private Transform Model(string name)
        {
            var t=new GameObject(name).transform;t.SetParent(root,false);
            Primitive(PrimitiveType.Capsule,t,Vector3.zero,new Vector3(.15f,.12f,.15f),body);
            for(int i=0;i<3;i++)Primitive(PrimitiveType.Cylinder,t,new Vector3(0,-.065f+i*.065f,0),new Vector3(.156f,.008f,.156f),metal);
            Primitive(PrimitiveType.Cylinder,t,new Vector3(0,.135f,0),new Vector3(.075f,.023f,.075f),metal);
            var lever=Primitive(PrimitiveType.Cube,t,new Vector3(.045f,.052f,0),new Vector3(.033f,.22f,.055f),metal);lever.localRotation=Quaternion.Euler(0,0,20);
            var ring=Line("Pull ring",.009f,t);ring.useWorldSpace=false;ring.positionCount=25;ColorLine(ring,new Color(.65f,.69f,.7f));
            for(int n=0;n<=24;n++){float a=n*Mathf.PI*2/24;ring.SetPosition(n,new Vector3(-.055f+Mathf.Cos(a)*.043f,.145f+Mathf.Sin(a)*.043f,0));}
            return t;
        }
        private static Transform Primitive(PrimitiveType type,Transform parent,Vector3 position,Vector3 size,Material material)
        {
            var g=GameObject.CreatePrimitive(type);g.transform.SetParent(parent,false);g.transform.localPosition=position;g.transform.localScale=size;
            g.GetComponent<Collider>().enabled=false;Object.Destroy(g.GetComponent<Collider>());g.GetComponent<Renderer>().sharedMaterial=material;return g.transform;
        }
        private static AudioClip Sound(bool warning)
        {
            const int rate=22050;var samples=new float[warning?2205:13230];var random=new System.Random(319);
            for(int n=0;n<samples.Length;n++){float t=n/(float)rate,envelope=Mathf.Pow(1-n/(float)samples.Length,warning?2:3);samples[n]=warning?Mathf.Sin(t*2*Mathf.PI*1200)*envelope*.35f:((float)random.NextDouble()*2-1+Mathf.Sin(t*2*Mathf.PI*55))*.45f*envelope;}
            var clip=AudioClip.Create(warning?"Grenade landing warning":"Grenade blast",samples.Length,1,rate,false);clip.SetData(samples,0);return clip;
        }
        public void Clear()
        {
            HidePreview();foreach(var p in held.Values)Object.Destroy(p.gameObject);held.Clear();
            foreach(var p in flying.Values){Object.Destroy(p.Model.gameObject);Object.Destroy(p.Warning.gameObject);}flying.Clear();
            foreach(var b in bursts)Object.Destroy(b.Root.gameObject);bursts.Clear();audio.Stop();
        }
        public void Dispose(){Clear();Object.Destroy(root.gameObject);foreach(var m in new[]{body,metal,lineMaterial,flashMaterial,waterMaterial})Object.Destroy(m);Object.Destroy(explosion);Object.Destroy(ping);}
    }
}
