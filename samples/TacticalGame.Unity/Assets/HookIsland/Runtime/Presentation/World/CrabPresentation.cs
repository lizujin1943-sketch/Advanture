using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Confirmed enemy view. Joint animation/telegraphs never apply damage or modify the simulation.</summary>
    public sealed class CrabPresentation : IDisposable
    {
        private readonly View[] views;
        public CrabPresentation(Transform parent,Shader lineShader,CrabConfig config)
        {
            views=Enumerable.Range(0,config.Enabled?config.Count:0).Select(i=>new View(parent,lineShader,config,i)).ToArray();
        }
        public void Synchronize(HookState state)
        {
            for(int i=0;i<views.Length;i++)
                if(i<state.Crabs.Count)views[i].Synchronize(state.Crabs[i],state.Steps);
                else views[i].Clear();
        }
        public void Clear(){foreach(var view in views)view.Clear();}
        public void Dispose(){foreach(var view in views)view.Dispose();}

        private sealed class View : IDisposable
        {
            private readonly CrabConfig config;
            private readonly Transform root,model;
            private readonly Quaternion modelRestRotation;
            private readonly Vector3 modelRestScale;
            private readonly List<(Transform joint,Quaternion rest,int side,int index)> legs=new List<(Transform,Quaternion,int,int)>();
            private readonly List<(Transform joint,Quaternion rest,int side)> claws=new List<(Transform,Quaternion,int)>();
            private readonly List<(Transform joint,Quaternion rest,int side)> pincers=new List<(Transform,Quaternion,int)>();
            private readonly List<Material> materials=new List<Material>();
            private readonly LineRenderer warning,health;
            private bool placed;
            private uint generation;
            public View(Transform parent,Shader lineShader,CrabConfig config,int slot)
            {
                this.config=config;root=new GameObject($"Shore crab {slot} (confirmed)").transform;root.SetParent(parent,false);
                var prefab=Resources.Load<GameObject>("Enemies/ShoreCrab");
                if(prefab==null)throw new InvalidOperationException("Missing authored ShoreCrab model.");
                model=Object.Instantiate(prefab,root,false).transform;model.name="Blender crab";
                modelRestRotation=AuthoredFacing(model,root);model.localRotation=modelRestRotation;modelRestScale=model.localScale;
                foreach(var t in model.GetComponentsInChildren<Transform>(true))
                {
                    if(t.name.StartsWith("Leg_")){int index=int.Parse(t.name.Substring(t.name.Length-1));legs.Add((t,t.localRotation,t.name.Contains("_L_")?-1:1,index));}
                    if(t.name=="Claw_L" || t.name=="Claw_R")claws.Add((t,t.localRotation,t.name.EndsWith("L")?-1:1));
                    if(t.name.StartsWith("Pincer_"))pincers.Add((t,t.localRotation,t.name.EndsWith("Inner")?-1:1));
                }
                var palette=new Dictionary<string,Material>();
                foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials=renderer.sharedMaterials.Select(original=>{
                        string name=original.name;
                        if(!palette.TryGetValue(name,out var material))
                        {
                            Color color=name.Contains("Cream")?new Color(.95f,.73f,.39f):name.Contains("Orange")?new Color(.95f,.32f,.075f):
                                name.Contains("Dark")?new Color(.035f,.045f,.052f):name.Contains("Glint")?new Color(.98f,.96f,.85f):new Color(.66f,.13f,.055f);
                            material=new Material(Shader.Find("Hook Island/Baked Color Surface")){name=name,color=color};
                            material.SetFloat("_Ambient",.4f);material.SetFloat("_SunStrength",.85f);materials.Add(material);palette.Add(name,material);
                        }
                        return material;
                    }).ToArray();
                }
                var line=new Material(lineShader){name="Crab telegraph"};materials.Add(line);
                warning=Line("Pinch warning",line,new Color(1,.3f,.08f),.035f);
                health=Line("Crab health",line,new Color(.95f,.35f,.15f),.045f);
                root.gameObject.SetActive(false);
            }
            private static Quaternion AuthoredFacing(Transform model,Transform root)
            {
                var joints=model.GetComponentsInChildren<Transform>(true);
                var eyes=joints.Where(t=>t.name=="Eye" || t.name.StartsWith("Eye.",StringComparison.Ordinal)).ToArray();
                var shell=joints.Single(t=>t.name=="Carapace");
                if(eyes.Length!=2)throw new InvalidOperationException("Missing authored crab face markers.");
                Vector3 eyeCenter=eyes.Aggregate(Vector3.zero,(sum,t)=>sum+t.position)/eyes.Length;
                Vector3 forward=root.InverseTransformPoint(eyeCenter)-root.InverseTransformPoint(shell.position);forward.y=0;
                if(forward.sqrMagnitude<.000001f)throw new InvalidOperationException("Invalid authored crab forward axis.");
                // 实际 FBX 的眼、嘴与双钳在 Blender -Y 一侧；导入后按真实几何校准，不能假设模型的 +Z 是脸。
                // 只转模型子层，逻辑根节点、攻击方向和预警扇区仍共同指向权威 Direction。
                return Quaternion.AngleAxis(-Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg,Vector3.up)*model.localRotation;
            }
            private LineRenderer Line(string name,Material material,Color color,float width)
            {
                var g=new GameObject(name);g.transform.SetParent(root,false);var line=g.AddComponent<LineRenderer>();
                line.sharedMaterial=material;line.useWorldSpace=false;line.startColor=line.endColor=color;line.startWidth=line.endWidth=width;
                line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return line;
            }
            public void Synchronize(CrabState c,ulong steps)
            {
                bool visible=c.Visible && (c.Phase!=CrabPhase.Dead || c.Timer>config.RespawnTicks-18);
                root.gameObject.SetActive(visible);if(!visible){placed=false;return;}
                var target=new Vector3(c.X/1000f,.45f,c.Z/1000f);float climb=0;
                if(c.Phase==CrabPhase.Climbing || c.Phase==CrabPhase.Leaving)
                {
                    climb=(float)c.Timer/config.ClimbTicks;if(c.Phase==CrabPhase.Leaving)climb=1-climb;
                    target=Vector3.Lerp(target,new Vector3(c.ShoreX/1000f,-.28f,c.ShoreZ/1000f),climb);
                }
                root.position=placed && generation==c.Generation?Vector3.Lerp(root.position,target,1-Mathf.Exp(-25*Time.unscaledDeltaTime)):target;
                generation=c.Generation;placed=true;
                if(c.DirectionX!=0 || c.DirectionZ!=0)root.rotation=Quaternion.LookRotation(new Vector3(c.DirectionX,0,c.DirectionZ));
                float raised=c.Phase==CrabPhase.Windup?1-(float)c.Timer/config.WindupTicks:0;
                float pinch=c.Phase==CrabPhase.Pinching?1-(float)c.Timer/CrabConfig.PinchTicks:c.Phase==CrabPhase.Recovering?(float)c.Timer/config.RecoveryTicks:0;
                foreach(var a in claws)a.joint.localRotation=a.rest*Quaternion.Euler(-raised*40-pinch*10,a.side*(raised*25-pinch*20),0);
                foreach(var a in pincers)a.joint.localRotation=a.rest*Quaternion.Euler(0,a.side*(raised*24-pinch*28),0);
                bool walking=c.Phase==CrabPhase.Chasing || c.Phase==CrabPhase.Retreating || c.Phase==CrabPhase.Climbing;
                foreach(var leg in legs){float stride=walking?Mathf.Sin((float)(steps%300)*.65f+leg.index*Mathf.PI+leg.side)*18:0;leg.joint.localRotation=leg.rest*Quaternion.Euler(stride,0,leg.side*stride*.5f);}
                float death=c.Phase==CrabPhase.Dead?Mathf.Clamp01((config.RespawnTicks-c.Timer)/18f):0;
                model.localRotation=Quaternion.Euler(climb*25,0,death*150)*modelRestRotation;model.localScale=modelRestScale*(1-death*.35f);
                warning.enabled=c.Phase==CrabPhase.Windup;
                if(warning.enabled)
                {
                    warning.positionCount=19;warning.SetPosition(0,new Vector3(0,.025f,0));
                    for(int i=0;i<17;i++){float angle=(-45+i*90f/16)*Mathf.Deg2Rad;warning.SetPosition(i+1,new Vector3(Mathf.Sin(angle)*1.4f,.025f,Mathf.Cos(angle)*1.4f));}
                    warning.SetPosition(18,new Vector3(0,.025f,0));
                }
                health.enabled=c.Health>0;health.positionCount=2;
                health.SetPosition(0,new Vector3(-.4f,.95f,0));health.SetPosition(1,new Vector3(-.4f+.8f*c.Health/config.Health,.95f,0));
            }
            public void Clear(){placed=false;root.gameObject.SetActive(false);}
            public void Dispose(){Release(root.gameObject);foreach(var m in materials)Release(m);}
            private static void Release(Object o){if(Application.isPlaying)Object.Destroy(o);else Object.DestroyImmediate(o);}
        }
    }
}
