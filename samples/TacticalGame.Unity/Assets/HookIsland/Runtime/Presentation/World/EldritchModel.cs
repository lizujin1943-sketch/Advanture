using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Original low-poly creature silhouettes. Geometry and joints are presentation only.</summary>
    public sealed class EldritchModel : IDisposable
    {
        public Transform Root {get;}
        private readonly Mesh orb,spike;
        private readonly Material skin,armor,glow,voidMaterial,bone;
        private readonly List<(Transform joint,Quaternion rest,float phase,float strength)> joints=new List<(Transform,Quaternion,float,float)>();
        private readonly EldritchKind kind;
        public EldritchModel(Transform parent,EldritchKind kind)
        {
            this.kind=kind;Root=new GameObject(EldritchCatalog.Get(kind).Name).transform;Root.SetParent(parent,false);
            orb=Sphere();spike=Cone();
            Color[] palette={new Color(.12f,.46f,.4f),new Color(.29f,.26f,.48f),new Color(.22f,.37f,.38f),new Color(.3f,.12f,.43f),
                new Color(.12f,.38f,.48f),new Color(.34f,.4f,.15f),new Color(.29f,.22f,.42f),new Color(.38f,.16f,.31f),new Color(.12f,.22f,.32f),new Color(.22f,.26f,.43f)};
            skin=Material("Abyss hide",palette[(int)kind]);armor=Material("Ancient shell",palette[(int)kind]*.64f);
            glow=Material("Bioluminescence",kind==EldritchKind.BlightSac?new Color(.7f,1,.16f):kind==EldritchKind.ChoirPriest?new Color(.5f,1,.7f):new Color(.35f,.95f,.92f));
            bone=Material("Salt ivory",new Color(.84f,.73f,.48f));voidMaterial=Material("Abyssal black",new Color(.025f,.035f,.055f));
            Build();
        }
        private static Material Material(string name,Color color)
        {
            var shader=Shader.Find("Hook Island/Baked Color Surface")??Shader.Find("Universal Render Pipeline/Lit")??Shader.Find("Standard");
            var m=new Material(shader){name=name,color=color};
            if(m.HasProperty("_Ambient"))m.SetFloat("_Ambient",.58f);
            if(m.HasProperty("_SunStrength"))m.SetFloat("_SunStrength",.8f);
            return m;
        }
        private Transform Part(string name,Vector3 position,Vector3 scale,Material material,Transform parent=null,bool pointed=false)
        {
            var g=new GameObject(name);g.transform.SetParent(parent??Root,false);g.transform.localPosition=position;g.transform.localScale=scale;
            g.AddComponent<MeshFilter>().sharedMesh=pointed?spike:orb;g.AddComponent<MeshRenderer>().sharedMaterial=material;return g.transform;
        }
        private void Link(string name,Vector3 a,Vector3 b,float radius,Material material,Transform parent=null,bool pointed=false)
        {
            var t=Part(name,(a+b)*.5f,new Vector3(radius,(b-a).magnitude*.5f,radius),material,parent,pointed);
            t.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);
        }
        private Transform Limb(string name,Vector3 start,Vector3 bend,Vector3 tip,float thickness,int index)
        {
            var t=new GameObject(name).transform;t.SetParent(Root,false);t.localPosition=start;
            Link("Upper",Vector3.zero,bend-start,thickness,skin,t);Link("Curved tip",bend-start,tip-start,thickness*.72f,skin,t,true);
            joints.Add((t,t.localRotation,index*1.7f,1));return t;
        }
        private void Eye(Vector3 at,float size,Transform parent=null)
        {
            Part("Eye socket",at,new Vector3(size*1.28f,size*1.12f,size*.62f),voidMaterial,parent);
            Part("Luminous iris",at+Vector3.forward*size*.42f,new Vector3(size,size*.9f,size*.33f),glow,parent);
            Part("Slit pupil",at+Vector3.forward*size*.69f,new Vector3(size*.22f,size*.73f,size*.12f),voidMaterial,parent);
            Part("Eye glint",at+new Vector3(-size*.27f,size*.3f,size*.77f),Vector3.one*size*.16f,bone,parent);
        }
        private void Crown(float y,float radius,int points)
        {
            for(int i=0;i<points;i++)
            {
                float a=i*Mathf.PI*2/points;Vector3 v=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));
                Link("Crown spine",new Vector3(0,y,0)+v*radius,new Vector3(0,y+.45f,0)+v*(radius+.18f),.09f,bone,null,true);
            }
        }
        private void Tentacles(int n,float y,float radius,float length)
        {
            for(int i=0;i<n;i++)
            {
                float a=i*Mathf.PI*2/n;Vector3 v=new Vector3(Mathf.Sin(a),0,Mathf.Cos(a));
                Limb("Tendril "+i,new Vector3(0,y,0)+v*radius,new Vector3(0,y-length*.55f,0)+v*(radius+.22f),
                    new Vector3(0,y-length,0)+v*(radius+.1f),.095f,i);
            }
        }
        private void Build()
        {
            switch(kind)
            {
                case EldritchKind.DeepOne:
                    Part("Hunched torso",new Vector3(0,.82f,0),new Vector3(.33f,.48f,.25f),skin);
                    Part("Gill head",new Vector3(0,1.27f,.16f),new Vector3(.34f,.28f,.27f),skin);
                    Eye(new Vector3(-.17f,1.32f,.37f),.1f);Eye(new Vector3(.17f,1.32f,.37f),.1f);
                    for(int side=-1;side<=1;side+=2)
                    {
                        Limb("Webbed arm "+side,new Vector3(side*.28f,1.06f,0),new Vector3(side*.48f,.62f,.08f),new Vector3(side*.42f,.36f,.32f),.11f,side);
                        Limb("Bent leg "+side,new Vector3(side*.18f,.55f,0),new Vector3(side*.24f,.23f,-.1f),new Vector3(side*.23f,.05f,.26f),.13f,side+3);
                        Link("Gill fin",new Vector3(side*.25f,1.26f,0),new Vector3(side*.47f,1.44f,-.2f),.12f,bone,null,true);
                    }
                    Tentacles(4,1.15f,.08f,.26f);break;
                case EldritchKind.ReefRam:
                    Part("Low armored body",new Vector3(0,.55f,-.07f),new Vector3(.4f,.3f,.58f),skin);
                    Part("Horned skull",new Vector3(0,.59f,.45f),new Vector3(.37f,.3f,.27f),armor);
                    Eye(new Vector3(-.19f,.68f,.65f),.075f);Eye(new Vector3(.19f,.68f,.65f),.075f);
                    for(int s=-1;s<=1;s+=2)
                    {
                        Link("Forward horn",new Vector3(s*.25f,.69f,.54f),new Vector3(s*.34f,.9f,.98f),.12f,bone,null,true);
                        for(int k=0;k<2;k++)Limb("Running leg "+s+" "+k,new Vector3(s*.28f,.55f,k*.62f-.35f),new Vector3(s*.5f,.3f,k*.62f-.44f),new Vector3(s*.46f,.05f,k*.62f-.2f),.12f,k+s*2);
                    }
                    for(int i=0;i<4;i++)Link("Dorsal thorn",new Vector3(0,.75f,-.4f+i*.2f),new Vector3(0,1.12f,-.55f+i*.2f),.11f,bone,null,true);
                    break;
                case EldritchKind.ShellWarden:
                    Part("Heavy body",new Vector3(0,.68f,0),new Vector3(.4f,.61f,.36f),skin);
                    Part("Sacred spiral shell",new Vector3(0,.98f,.12f),new Vector3(.55f,.65f,.31f),armor);
                    for(int i=0;i<15;i++)
                    {float a=i*.7f,r=.41f-i*.021f;Part("Spiral ridge",new Vector3(Mathf.Sin(a)*r,.98f+Mathf.Cos(a)*r,.4f),new Vector3(.08f,.08f,.05f),bone);}
                    Eye(new Vector3(-.18f,1.58f,.15f),.085f);Eye(new Vector3(.18f,1.58f,.15f),.085f);
                    for(int s=-1;s<=1;s+=2)
                    {Limb("Shell foot",new Vector3(s*.2f,.4f,0),new Vector3(s*.37f,.15f,0),new Vector3(s*.38f,.07f,.3f),.17f,s);Limb("Hammer claw",new Vector3(s*.32f,.8f,0),new Vector3(s*.6f,.6f,.13f),new Vector3(s*.55f,.38f,.5f),.19f,s+2);}
                    break;
                case EldritchKind.RiftEye:
                    Part("Floating eye mantle",new Vector3(0,.85f,0),new Vector3(.53f,.47f,.36f),skin);
                    Eye(new Vector3(0,.87f,.34f),.33f);Crown(1.12f,.38f,5);Tentacles(6,.55f,.23f,.55f);break;
                case EldritchKind.DrownedBell:
                    Part("Bell hood",new Vector3(0,.95f,0),new Vector3(.55f,.55f,.44f),skin);
                    Part("Bell mouth",new Vector3(0,.59f,.04f),new Vector3(.48f,.13f,.43f),bone);
                    Part("Lantern core",new Vector3(0,.58f,.06f),new Vector3(.2f,.3f,.2f),glow);
                    Eye(new Vector3(0,1.08f,.4f),.13f);Tentacles(8,.55f,.3f,.6f);Crown(1.28f,.25f,3);break;
                case EldritchKind.BlightSac:
                    Part("Distended sac",new Vector3(0,.64f,0),new Vector3(.55f,.64f,.45f),skin);
                    for(int i=0;i<9;i++){float a=i*2.4f;Part("Glowing pustule",new Vector3(Mathf.Sin(a)*.44f,.35f+(i%3)*.3f,Mathf.Cos(a)*.37f),Vector3.one*.13f,glow);}
                    Part("Spore vent",new Vector3(0,1.16f,.25f),new Vector3(.23f,.19f,.16f),voidMaterial);Tentacles(5,.23f,.35f,.28f);break;
                case EldritchKind.ChoirPriest:
                    Part("Faceless hood",new Vector3(0,1.46f,0),new Vector3(.3f,.38f,.25f),armor);
                    Part("Hood void",new Vector3(0,1.43f,.2f),new Vector3(.19f,.26f,.085f),voidMaterial);
                    Link("Liturgical robe",new Vector3(0,.15f,0),new Vector3(0,1.2f,0),.43f,skin,null,true);
                    Part("Prayer sigil",new Vector3(0,1.05f,.36f),new Vector3(.12f,.18f,.05f),glow);Crown(1.65f,.26f,5);
                    for(int s=-1;s<=1;s+=2)Limb("Raised prayer arm",new Vector3(s*.24f,1.15f,0),new Vector3(s*.5f,1.05f,.1f),new Vector3(s*.33f,1.48f,.32f),.1f,s);
                    Tentacles(5,.4f,.2f,.45f);break;
                case EldritchKind.BroodMother:
                    Part("Brood carapace",new Vector3(0,.7f,-.1f),new Vector3(.57f,.5f,.5f),armor);
                    for(int i=0;i<5;i++)Part("Egg "+i,new Vector3((i%3-1)*.23f,.38f+(i/3)*.28f,.34f),new Vector3(.16f,.22f,.14f),glow);
                    for(int i=0;i<3;i++)Eye(new Vector3((i-1)*.22f,1.02f,.29f),.085f);
                    Tentacles(8,.55f,.39f,.6f);Crown(1.02f,.34f,4);break;
                case EldritchKind.RiftStalker:
                    Part("Slender torso",new Vector3(0,1.03f,0),new Vector3(.19f,.55f,.17f),skin);
                    Part("Split mask",new Vector3(.06f,1.62f,.04f),new Vector3(.23f,.3f,.18f),bone);Eye(new Vector3(.08f,1.64f,.2f),.1f);
                    Limb("Long scythe",new Vector3(-.15f,1.35f,0),new Vector3(-.56f,1.12f,.08f),new Vector3(-.63f,.2f,.4f),.085f,0);
                    Limb("Crooked hand",new Vector3(.15f,1.35f,0),new Vector3(.48f,1.58f,.08f),new Vector3(.4f,1.94f,.24f),.08f,1);
                    Limb("Left stilt",new Vector3(-.1f,.6f,0),new Vector3(-.3f,.28f,-.15f),new Vector3(-.35f,0,.2f),.085f,3);
                    Limb("Right stilt",new Vector3(.1f,.6f,0),new Vector3(.35f,.3f,.13f),new Vector3(.27f,0,.32f),.085f,4);break;
                case EldritchKind.StarHerald:
                    Part("Ancient core",new Vector3(0,.88f,0),new Vector3(.33f,.4f,.28f),armor);Eye(new Vector3(0,.92f,.28f),.2f);
                    for(int i=0;i<5;i++)
                    {
                        float a=i*Mathf.PI*2/5;Vector3 v=new Vector3(Mathf.Sin(a),Mathf.Cos(a),0);
                        Link("Star point",new Vector3(0,1.0f,0)+v*.25f,new Vector3(0,1.0f,0)+v*.83f,.22f,bone,null,true);
                        Part("Star rune",new Vector3(0,1.0f,.15f)+v*.43f,Vector3.one*.08f,glow);
                    }
                    Tentacles(5,.6f,.22f,.7f);break;
            }
        }
        public void Animate(EldritchCreature c,ulong steps,float death=0)
        {
            float t=(steps%18000)/30f;var d=EldritchCatalog.Get(kind);
            float charge=c.Phase==EldritchPhase.Windup?1-(float)c.Timer/d.WindupTicks:0;
            float action=c.Phase==EldritchPhase.Active?1:0;
            foreach(var j in joints)
                j.joint.localRotation=j.rest*Quaternion.Euler(Mathf.Sin(t*(d.Habitat==EldritchHabitat.Ground?7:2)+j.phase)*12-charge*25+action*18,0,Mathf.Cos(t*2+j.phase)*7);
            Root.localRotation=Quaternion.Euler(charge*-12+action*10,0,death*100);
            float pulse=1+Mathf.Sin(t*2+(int)kind)*.025f+charge*.08f;
            Root.localScale=Vector3.one*(c.IsMinion ? .65f : 1)*pulse*(1-death*.65f);
        }
        public void Dispose(){Release(Root.gameObject);Release(orb);Release(spike);Release(skin);Release(armor);Release(glow);Release(voidMaterial);Release(bone);}
        internal static void Release(Object value){if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
        private static Mesh Sphere()
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int y=0;y<7;y++)for(int x=0;x<12;x++)
            {
                Vector3 At(int a,int b){float v=a*Mathf.PI/7,u=b*2*Mathf.PI/12;return new Vector3(Mathf.Sin(v)*Mathf.Cos(u),Mathf.Cos(v),Mathf.Sin(v)*Mathf.Sin(u));}
                Add(At(y,x),At(y+1,x+1),At(y+1,x));Add(At(y,x),At(y,x+1),At(y+1,x+1));
            }
            void Add(Vector3 a,Vector3 b,Vector3 c){int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles.Add(i);triangles.Add(i+1);triangles.Add(i+2);}
            var mesh=new Mesh{name="Faceted organic form"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static Mesh Cone()
        {
            var v=new List<Vector3>();var t=new List<int>();
            for(int i=0;i<8;i++)
            {
                float a=i*Mathf.PI/4,b=(i+1)*Mathf.PI/4;var p=new Vector3(Mathf.Cos(a),-1,Mathf.Sin(a));var q=new Vector3(Mathf.Cos(b),-1,Mathf.Sin(b));
                int n=v.Count;v.Add(p);v.Add(Vector3.up);v.Add(q);v.Add(q);v.Add(Vector3.down);v.Add(p);for(int j=0;j<6;j++)t.Add(n+j);
            }
            var mesh=new Mesh{name="Eldritch tapered limb"};mesh.SetVertices(v);mesh.SetTriangles(t,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
