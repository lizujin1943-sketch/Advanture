using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Read-only presentation of confirmed enemy state; no physics or damage decisions.</summary>
    public sealed class EnemyPresentation:IDisposable
    {
        private readonly Transform root;
        private readonly Camera camera;
        private readonly EnemyConfig config;
        private readonly CatchEscapePresentation escapes;
        private readonly Mesh bodyMesh,finMesh;
        private readonly Material red,dark,ivory,teal,purple,aqua,warning,lineMaterial;
        private readonly Dictionary<int,Transform> fish=new Dictionary<int,Transform>();
        private readonly Dictionary<uint,Transform> balls=new Dictionary<uint,Transform>();
        private readonly Transform[] impacts=new Transform[4];
        private readonly int[] health={-1,-1,-1,-1};
        private readonly float[] flashUntil=new float[4];
        private Transform monster,charge,weightLabel;
        private LineRenderer aimLine,chargeRing;
        private int interruptions;
        private float interruptedUntil;
        public int RenderedPiranhas=>fish.Values.Count(f=>f.gameObject.activeSelf);
        public int RenderedMonsters=>monster!=null && monster.gameObject.activeSelf?1:0;
        public int RenderedWaterballs=>balls.Count;
        public bool WindupVisible=>aimLine!=null && aimLine.enabled;
        public bool ImpactVisible=>impacts.Any(t=>t!=null && t.gameObject.activeSelf);
        public EnemyPresentation(Transform parent,Camera camera,Shader lineShader,EnemyConfig config)
        {
            this.camera=camera;this.config=config;root=new GameObject("Water enemies (confirmed)").transform;root.SetParent(parent,false);
            escapes=new CatchEscapePresentation(root,lineShader);
            bodyMesh=BodyMesh();finMesh=FinMesh();
            red=Surface("Piranha coral",new Color(.9f,.16f,.1f));dark=Surface("Enemy mouth",new Color(.1f,.09f,.2f));
            ivory=Surface("Enemy teeth",new Color(1,.94f,.72f));teal=Surface("Monster hide",new Color(.11f,.42f,.43f));
            purple=Surface("Monster fins",new Color(.37f,.22f,.55f));aqua=Surface("Waterball",new Color(.35f,.91f,1));
            warning=Surface("Windup charge",new Color(1,.46f,.05f));lineMaterial=new Material(lineShader){name="Enemy telegraph"};
        }
        public void Synchronize(HookState state)
        {
            foreach(var f in state.Enemies.Piranhas)
            {
                if(!fish.TryGetValue(f.Id,out var view))
                {
                    view=Node("Piranha "+f.Id,root);fish.Add(f.Id,view);
                    Part(view,"Body",bodyMesh,red,Vector3.zero,new Vector3(.62f,.45f,.95f));
                    Part(view,"Belly",bodyMesh,ivory,new Vector3(0,-.12f,.16f),new Vector3(.48f,.22f,.55f));
                    Part(view,"Tail",finMesh,red,new Vector3(0,0,-.7f),new Vector3(.7f,.6f,.5f));
                    Part(view,"Dorsal fin",finMesh,dark,new Vector3(0,.25f,-.05f),new Vector3(.12f,.7f,.65f));
                    Part(view,"Jaw",bodyMesh,dark,new Vector3(0,.01f,.44f),new Vector3(.47f,.19f,.2f));
                    for(int i=-1;i<=1;i++)Part(view,"Fang",finMesh,ivory,new Vector3(i*.12f,.05f,.52f),new Vector3(.08f,-.2f,.13f));
                    for(int i=-1;i<=1;i+=2)Part(view,"Eye",bodyMesh,ivory,new Vector3(i*.27f,.16f,.22f),Vector3.one*.14f);
                    view.localScale=Vector3.one*(.58f+.08f*f.WeightGrams/1000f);
                }
                view.gameObject.SetActive(f.RespawnTicks==0);
                escapes.Present(f.Id,f.Escape,view,0f);
                if(f.Escape.Active)continue;
                float height=f.Owner>=0 && !state.Players[f.Owner].Swimming.InWater?1.25f:.12f;
                var next=new Vector3(f.X/1000f,height+Mathf.Sin(Time.time*4+f.Id)*.035f,f.Z/1000f);
                var motion=next-view.position;motion.y=0;
                if(motion.sqrMagnitude>.000001f)view.rotation=Quaternion.LookRotation(motion);
                view.position=next;
                if(f.Target>=0)
                {
                    var target=state.Players[f.Target];var direction=new Vector3(target.X-f.X,0,target.Z-f.Z);
                    if(direction.sqrMagnitude>1)view.rotation=Quaternion.LookRotation(direction);
                }
            }
            if(config.MonsterEnabled)
            {
                if(monster==null)CreateMonster();
                var m=state.Enemies.Monster;bool windup=m.Phase==MonsterPhase.Windup;
                monster.gameObject.SetActive(windup || m.DiveTicks>0);
                float emergence=windup?Mathf.Clamp01((config.WindupTicks-m.Timer)/(float)Mathf.Min(15,config.WindupTicks)):m.DiveTicks/15f;
                monster.position=new Vector3(m.X/1000f,Mathf.Lerp(-1.6f,.1f,emergence),m.Z/1000f);
                weightLabel.gameObject.SetActive(windup);
                if(m.DirectionX!=0 || m.DirectionZ!=0)monster.rotation=Quaternion.LookRotation(new Vector3(m.DirectionX,0,m.DirectionZ));
                weightLabel.rotation=camera.transform.rotation;
                if(state.Enemies.Interruptions>interruptions){interruptions=state.Enemies.Interruptions;interruptedUntil=Time.unscaledTime+.6f;}
                float progress=windup?1-m.Timer/(float)config.WindupTicks:0;
                charge.gameObject.SetActive(windup || Time.unscaledTime<interruptedUntil);
                charge.localScale=Vector3.one*(windup?.18f+progress*.5f:.17f);
                chargeRing.enabled=aimLine.enabled=windup;
                if(windup)
                {
                    var direction=new Vector3(m.DirectionX,0,m.DirectionZ).normalized;
                    var start=monster.position+Vector3.up*.2f;
                    aimLine.SetPosition(0,start+direction*.9f);aimLine.SetPosition(1,start+direction*Mathf.Min(10,config.WaterballRange/1000f));
                    var color=Color.Lerp(new Color(1,.8f,.1f,.4f),new Color(1,.22f,.06f,.95f),progress);
                    aimLine.startColor=aimLine.endColor=color;
                    Ring(chargeRing,start,1.3f+progress*.4f,color);
                }
            }
            var present=new HashSet<uint>(state.Enemies.Waterballs.Select(b=>b.Id));
            foreach(var id in balls.Keys.Where(id=>!present.Contains(id)).ToArray()){Destroy(balls[id].gameObject);balls.Remove(id);}
            foreach(var b in state.Enemies.Waterballs)
            {
                if(!balls.TryGetValue(b.Id,out var view))
                {
                    view=Node("Waterball "+b.Id,root);balls.Add(b.Id,view);
                    Part(view,"Water",bodyMesh,aqua,Vector3.zero,Vector3.one*config.WaterballRadius/500f);
                    Part(view,"Foam",bodyMesh,ivory,new Vector3(0,.08f,.03f),Vector3.one*config.WaterballRadius/950f);
                    Part(view,"Wake",finMesh,aqua,new Vector3(0,0,-.4f),new Vector3(.27f,.16f,.8f));
                }
                view.position=new Vector3(b.X/1000f,.65f,b.Z/1000f);view.rotation=Quaternion.LookRotation(new Vector3(b.DirectionX,0,b.DirectionZ));
            }
            foreach(var p in state.Players)
            {
                if(health[p.Slot]>=0 && p.Vitals.Health<health[p.Slot])flashUntil[p.Slot]=Time.unscaledTime+.45f;
                health[p.Slot]=p.Vitals.Health;
                if(impacts[p.Slot]==null)
                {
                    impacts[p.Slot]=Node("Player hit "+p.Slot,root);
                    MakeLine(impacts[p.Slot],"Impact",.055f,25);
                }
                bool visible=Time.unscaledTime<flashUntil[p.Slot];impacts[p.Slot].gameObject.SetActive(visible);
                if(visible)Ring(impacts[p.Slot].GetComponentInChildren<LineRenderer>(),new Vector3(p.X/1000f,.3f,p.Z/1000f),
                    .55f+(.45f-flashUntil[p.Slot]+Time.unscaledTime),new Color(1,.14f,.07f,1));
            }
        }
        private void CreateMonster()
        {
            monster=Node("Water monster",root);
            Part(monster,"Body",bodyMesh,teal,new Vector3(0,.35f,0),new Vector3(1.9f,1.4f,2));
            Part(monster,"Muzzle",bodyMesh,purple,new Vector3(0,.37f,.87f),new Vector3(1.1f,.7f,.65f));
            Part(monster,"Mouth",bodyMesh,dark,new Vector3(0,.42f,1.13f),new Vector3(.7f,.43f,.14f));
            for(int i=-1;i<=1;i+=2)
            {
                Part(monster,"Eye",bodyMesh,ivory,new Vector3(i*.55f,.95f,.5f),Vector3.one*.32f);
                Part(monster,"Pupil",bodyMesh,dark,new Vector3(i*.55f,.97f,.63f),Vector3.one*.16f);
                Part(monster,"Horn",finMesh,purple,new Vector3(i*.66f,1.15f,.05f),new Vector3(.3f,.85f,.4f));
                Part(monster,"Side fin",finMesh,purple,new Vector3(i*.98f,.35f,-.3f),new Vector3(.9f,.4f,.9f));
                Part(monster,"Fang",finMesh,ivory,new Vector3(i*.22f,.55f,1.18f),new Vector3(.12f,-.28f,.16f));
            }
            charge=Part(monster,"Charging water",bodyMesh,warning,new Vector3(0,.45f,1.25f),Vector3.one*.2f);
            aimLine=MakeLine(root,"Windup direction",.12f,2);chargeRing=MakeLine(root,"Windup ring",.06f,25);
            var label=weightLabel=Node("Monster weight",monster);label.localPosition=new Vector3(0,1.85f,0);
            var text=label.gameObject.AddComponent<TextMesh>();text.text="Lv"+config.MonsterLevel+" · "+(config.MonsterWeightGrams/1000f).ToString("0.#")+" kg";
            text.characterSize=.12f;text.fontSize=48;text.anchor=TextAnchor.MiddleCenter;text.color=new Color(1,.92f,.62f);
            label.rotation=camera.transform.rotation;
        }
        private Transform Part(Transform parent,string name,Mesh mesh,Material material,Vector3 position,Vector3 scale)
        {
            var t=Node(name,parent);t.localPosition=position;t.localScale=scale;t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;return t;
        }
        private static Transform Node(string name,Transform parent){var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
        private LineRenderer MakeLine(Transform parent,string name,float width,int points)
        {
            var t=Node(name,parent);var line=t.gameObject.AddComponent<LineRenderer>();line.sharedMaterial=lineMaterial;line.useWorldSpace=true;
            line.positionCount=points;line.startWidth=line.endWidth=width;line.enabled=false;return line;
        }
        private static void Ring(LineRenderer line,Vector3 position,float radius,Color color)
        {
            line.enabled=true;line.startColor=line.endColor=color;
            for(int i=0;i<25;i++){float angle=i*Mathf.PI/12;line.SetPosition(i,position+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius));}
        }
        private static Material Surface(string name,Color color)
        {var m=new Material(Shader.Find("Hook Island/Baked Color Surface")){name=name,color=color};m.SetFloat("_Ambient",.45f);return m;}
        private static Mesh BodyMesh()
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            const int sides=10,rings=6;
            for(int y=0;y<=rings;y++)for(int x=0;x<=sides;x++)
            {float a=y*Mathf.PI/rings,b=x*Mathf.PI*2/sides;float radius=y==0 || y==rings?0:Mathf.Sin(a);vertices.Add(new Vector3(radius*Mathf.Sin(b),Mathf.Cos(a),radius*Mathf.Cos(b))*.5f);}
            for(int y=0;y<rings;y++)for(int x=0;x<sides;x++)
            {
                int i=y*(sides+1)+x;
                // Each pole has one triangle per segment; a second, degenerate face reverses its averaged normal.
                if(y>0)triangles.AddRange(new[]{i,i+sides+1,i+1});
                if(y<rings-1)triangles.AddRange(new[]{i+1,i+sides+1,i+sides+2});
            }
            var mesh=new Mesh{name="Enemy rounded body"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static Mesh FinMesh()
        {
            var mesh=new Mesh{name="Enemy fin"};mesh.vertices=new[]{new Vector3(-.5f,0,-.5f),new Vector3(.5f,0,-.5f),new Vector3(0,1,0),new Vector3(0,0,.5f)};
            mesh.triangles=new[]{0,2,1,0,3,2,1,2,3,0,1,3};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        public void Clear()
        {
            escapes.Clear();
            foreach(var f in fish.Values)Destroy(f.gameObject);fish.Clear();foreach(var b in balls.Values)Destroy(b.gameObject);balls.Clear();
            if(monster!=null)monster.gameObject.SetActive(false);if(aimLine!=null)aimLine.enabled=false;if(chargeRing!=null)chargeRing.enabled=false;
            for(int i=0;i<4;i++){health[i]=-1;flashUntil[i]=0;if(impacts[i]!=null)impacts[i].gameObject.SetActive(false);}
            interruptions=0;interruptedUntil=0;
        }
        public void Dispose(){escapes.Dispose();Destroy(root.gameObject);foreach(var m in new[]{red,dark,ivory,teal,purple,aqua,warning,lineMaterial})Destroy(m);Destroy(bodyMesh);Destroy(finMesh);}
        private static void Destroy(Object value){if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
    }
}
