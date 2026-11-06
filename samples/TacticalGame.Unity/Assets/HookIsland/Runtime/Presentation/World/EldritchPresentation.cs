using System;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Only confirmed snapshots drive opponents, telegraphs and health. No authority writes.</summary>
    public sealed class EldritchPresentation : IDisposable
    {
        private readonly View[] views;
        public EldritchPresentation(Transform parent,Shader lineShader,EldritchConfig config,bool town=false)
        {
            views=new View[config.Enabled?(town?TownEncounters.Population:EldritchCatalog.Population):0];
            for(int i=0;i<views.Length;i++)views[i]=new View(parent,lineShader,config,town?TownEncounters.Kind(i):i<10?(EldritchKind)i:EldritchKind.DeepOne,i);
        }
        public void Synchronize(HookState state)
        {for(int i=0;i<views.Length;i++){if(i<state.Eldritch.Creatures.Count)views[i].Sync(state.Eldritch.Creatures[i],state);else views[i].Clear();}}
        public void Clear(){foreach(var view in views)view.Clear();}
        public void Dispose(){foreach(var view in views)view.Dispose();}
        private sealed class View : IDisposable
        {
            private readonly Transform root;
            private readonly EldritchModel model;
            private readonly EldritchConfig config;
            private readonly EldritchDefinition definition;
            private readonly LineRenderer warning,inner,effect,health;
            private readonly Material line;
            public View(Transform parent,Shader shader,EldritchConfig config,EldritchKind kind,int id)
            {
                this.config=config;definition=EldritchCatalog.Get(kind);
                root=new GameObject($"Eldritch {id:00} — {definition.Name} (confirmed)").transform;root.SetParent(parent,false);
                model=new EldritchModel(root,kind);line=new Material(shader??Shader.Find("Sprites/Default")){name="Eldritch telegraphs"};
                warning=Line("Attack warning",.045f,new Color(1,.37f,.14f));inner=Line("Safe inner ring",.055f,new Color(.3f,1,.72f));
                effect=Line("Ability effect",.1f,new Color(.45f,1,.87f));health=Line("Enemy health",.045f,new Color(.58f,.91f,.79f));
                root.gameObject.SetActive(false);
            }
            private LineRenderer Line(string name,float width,Color color)
            {
                var g=new GameObject(name);g.transform.SetParent(root,false);var l=g.AddComponent<LineRenderer>();l.sharedMaterial=line;
                l.useWorldSpace=true;l.startWidth=l.endWidth=width;l.startColor=l.endColor=color;l.positionCount=0;
                l.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return l;
            }
            public void Sync(EldritchCreature c,HookState state)
            {
                float death=c.Phase==EldritchPhase.Dead?Mathf.Clamp01((config.RespawnTicks-c.Timer)/24f):0;
                // Town deaths are permanent encounter receipts. Their respawn clock never advances.
                bool visible=c.Visible && (c.Phase!=EldritchPhase.Dead || !state.Town.Enabled && death<1);root.gameObject.SetActive(visible);if(!visible)return;
                float floor=(state.Town.Enabled?TownLayout.OnGround(state.Tiles,c.X,c.Z):PlatformRules.OnPlatform(state.Tiles,c.X,c.Z)) ? .45f : 0;
                float height=definition.Habitat==EldritchHabitat.Ground?floor:definition.Habitat==EldritchHabitat.Hover?floor+.35f:.02f;
                if(c.Phase==EldritchPhase.Emerging)height-=.8f*c.Timer/EldritchCatalog.EmergenceTicks;
                // Keep root and telegraphs on the same confirmed position; cosmetic joint motion is separate.
                root.position=new Vector3(c.X/1000f,height,c.Z/1000f);
                root.rotation=Quaternion.LookRotation(new Vector3(c.DirectionX,0,c.DirectionZ));model.Animate(c,state.Steps,death);
                bool windup=c.Phase==EldritchPhase.Windup,active=c.Phase==EldritchPhase.Active;
                warning.enabled=windup;effect.enabled=active;inner.enabled=(windup || active) && definition.Ability==EldritchAbility.Ring;
                health.enabled=c.Damageable;health.positionCount=2;health.SetPosition(0,root.position+new Vector3(-.35f,2.15f,0));health.SetPosition(1,root.position+new Vector3(-.35f+.7f*c.Health/EldritchCatalog.MaxHealth(c),2.15f,0));
                if(c.Phase==EldritchPhase.Emerging){warning.enabled=true;Circle(warning,new Vector3(c.X/1000f,floor+.045f,c.Z/1000f),.75f);return;}
                var origin=new Vector3(c.X/1000f,floor+.05f,c.Z/1000f);
                var target=new Vector3(c.EffectX/1000f,(state.Town.Enabled?TownLayout.OnGround(state.Tiles,c.EffectX,c.EffectZ):PlatformRules.OnPlatform(state.Tiles,c.EffectX,c.EffectZ)) ? .5f : .05f,c.EffectZ/1000f);
                if(inner.enabled)Circle(inner,origin,EldritchCatalog.RingInnerRadius/1000f);
                if(!windup && !active)return;
                var trace=windup?warning:effect;
                if(definition.Ability==EldritchAbility.Beam || definition.Ability==EldritchAbility.Charge)
                {
                    Capsule(trace,origin,target,definition.Radius/1000f);
                    if(active && definition.Ability==EldritchAbility.Beam){trace.startWidth=trace.endWidth=.18f;}
                    else trace.startWidth=trace.endWidth=windup ? .045f : .08f;
                }
                else if(definition.Ability==EldritchAbility.Mend && c.Ally>=0 && c.Ally<state.Eldritch.Creatures.Count)
                {
                    var a=state.Eldritch.Creatures[c.Ally];trace.loop=false;trace.positionCount=3;
                    trace.SetPosition(0,root.position+Vector3.up);trace.SetPosition(1,(root.position+new Vector3(a.X/1000f,1,a.Z/1000f))*.5f+Vector3.up*1.5f);trace.SetPosition(2,new Vector3(a.X/1000f,1,a.Z/1000f));
                }
                else Circle(trace,definition.Ability==EldritchAbility.Miasma || definition.Ability==EldritchAbility.Blink?target:origin,definition.Radius/1000f);
                if(definition.Ability==EldritchAbility.Miasma)trace.startColor=trace.endColor=new Color(.65f,.9f,.16f);
                else if(definition.Ability==EldritchAbility.Mend)trace.startColor=trace.endColor=new Color(.25f,1,.65f);
            }
            private static void Circle(LineRenderer line,Vector3 center,float radius)
            {
                line.loop=true;line.positionCount=64;
                for(int i=0;i<64;i++){float a=i*Mathf.PI*2/64;line.SetPosition(i,center+new Vector3(Mathf.Sin(a)*radius,0,Mathf.Cos(a)*radius));}
            }
            private static void Capsule(LineRenderer line,Vector3 from,Vector3 to,float radius)
            {
                Vector3 forward=(to-from).normalized,side=new Vector3(forward.z,0,-forward.x);line.loop=true;line.positionCount=34;
                for(int i=0;i<=16;i++){float a=i*Mathf.PI/16;line.SetPosition(i,to+side*Mathf.Cos(a)*radius+forward*Mathf.Sin(a)*radius);line.SetPosition(i+17,from-side*Mathf.Cos(a)*radius-forward*Mathf.Sin(a)*radius);}
            }
            public void Clear()=>root.gameObject.SetActive(false);
            public void Dispose(){model.Dispose();EldritchModel.Release(root.gameObject);EldritchModel.Release(line);}
        }
    }
}
