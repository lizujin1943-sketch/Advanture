using System;
using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Shared cosmetic geometry and audio. Moving parts never own ammunition.</summary>
    public sealed class DesertEagleArt : IDisposable
    {
        public sealed class Model
        {
            public Transform Root,Slide,Magazine,Muzzle;
        }
        private readonly List<Object> resources=new List<Object>();
        private readonly Mesh bevel,cylinder;
        private readonly Material silver,grip,black,accent;
        public Material Effects {get;}
        public AudioClip Shot {get;}
        public AudioClip Reload {get;}
        public AudioClip Loaded {get;}
        public AudioClip Empty {get;}
        public AudioClip Impact {get;}
        public AudioClip WaterImpact {get;}
        public DesertEagleArt(Shader lineShader)
        {
            bevel=BevelledBox();cylinder=Cylinder();resources.Add(bevel);resources.Add(cylinder);
            silver=Surface("Deagle brushed silver",new Color(.71f,.76f,.80f),.75f,.46f);
            grip=Surface("Deagle charcoal grip",new Color(.13f,.16f,.18f),.12f,.22f);
            black=Surface("Deagle bore and sights",new Color(.035f,.045f,.05f),.55f,.3f);
            accent=Surface("Deagle brass",new Color(.93f,.65f,.22f),.65f,.4f);
            Effects=new Material(lineShader){name="Confirmed firearm effects"};resources.Add(Effects);
            Shot=Sound("Desert Eagle report",.25f,95,1);Reload=Sound("Magazine release",.13f,1350,.30f);
            Loaded=Sound("Magazine lock",.11f,1800,.38f);Empty=Sound("Empty chamber",.055f,2400,.18f);Impact=Sound("Bullet contact",.09f,950,.25f);
            WaterImpact=WaterSound();
        }
        public Model Create(Transform parent)
        {
            var m=new Model{Root=Node("Desert Eagle",parent)};
            Part(m.Root,"Angular silver frame",bevel,silver,new Vector3(0,.045f,.085f),new Vector3(.069f,.065f,.265f));
            var stock=Part(m.Root,"Charcoal pistol grip",bevel,grip,new Vector3(0,-.055f,-.012f),new Vector3(.060f,.138f,.080f));stock.localRotation=Quaternion.Euler(-15,0,0);
            for(int n=0;n<5;n++)Part(stock,"Grip rib",bevel,black,new Vector3(0,-.28f+n*.13f,-.45f),new Vector3(.91f,.055f,.15f));
            m.Slide=Node("Moving slide",m.Root);
            Part(m.Slide,"Thick bevelled slide",bevel,silver,new Vector3(0,.096f,.095f),new Vector3(.078f,.071f,.318f));
            Part(m.Slide,"Upper barrel rib",bevel,silver,new Vector3(0,.138f,.127f),new Vector3(.042f,.017f,.245f));
            for(int n=0;n<6;n++)for(int side=-1;side<=1;side+=2)
                Part(m.Slide,"Rear slide serration",bevel,grip,new Vector3(side*.039f,.096f,-.038f+n*.009f),new Vector3(.0025f,.048f,.0035f));
            Part(m.Slide,"Ejection port",bevel,black,new Vector3(.040f,.111f,.075f),new Vector3(.003f,.026f,.047f));
            Part(m.Slide,"Chamber",bevel,silver,new Vector3(.042f,.112f,.075f),new Vector3(.003f,.018f,.033f));
            Part(m.Slide,"Rear sight",bevel,black,new Vector3(0,.145f,-.045f),new Vector3(.062f,.017f,.016f));
            Part(m.Slide,"Front sight",bevel,black,new Vector3(0,.158f,.222f),new Vector3(.012f,.025f,.012f));
            Part(m.Slide,"Sight bead",bevel,accent,new Vector3(0,.166f,.215f),new Vector3(.008f,.007f,.005f));
            Part(m.Root,"Barrel muzzle",cylinder,silver,new Vector3(0,.096f,.259f),new Vector3(.039f,.039f,.021f));
            Part(m.Root,"Dark bore",cylinder,black,new Vector3(0,.096f,.271f),new Vector3(.023f,.023f,.003f));
            Part(m.Root,"Trigger guard front",bevel,silver,new Vector3(0,-.012f,.104f),new Vector3(.031f,.064f,.017f));
            Part(m.Root,"Trigger guard base",bevel,silver,new Vector3(0,-.042f,.067f),new Vector3(.031f,.017f,.088f));
            var trigger=Part(m.Root,"Trigger",bevel,black,new Vector3(0,-.007f,.063f),new Vector3(.011f,.040f,.013f));trigger.localRotation=Quaternion.Euler(18,0,0);
            Part(m.Root,"Slide release",bevel,accent,new Vector3(-.038f,.044f,.030f),new Vector3(.008f,.013f,.038f));
            m.Magazine=Node("Removable magazine",m.Root);
            Part(m.Magazine,"Magazine body",bevel,grip,new Vector3(0,-.065f,-.016f),new Vector3(.043f,.118f,.058f));
            Part(m.Magazine,"Magazine floor plate",bevel,silver,new Vector3(0,-.126f,-.018f),new Vector3(.065f,.017f,.083f));
            m.Muzzle=Node("Muzzle",m.Root);m.Muzzle.localPosition=new Vector3(0,.096f,.275f);
            m.Root.gameObject.SetActive(false);return m;
        }
        public Transform Casing(Transform parent)=>Part(parent,"Ejected brass",cylinder,accent,Vector3.zero,new Vector3(.013f,.013f,.028f));
        private Material Surface(string name,Color color,float metallic,float smoothness)
        {var m=new Material(Shader.Find("Standard")){name=name,color=color};m.SetFloat("_Metallic",metallic);m.SetFloat("_Glossiness",smoothness);resources.Add(m);return m;}
        private static Transform Node(string name,Transform parent){var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
        private static Transform Part(Transform parent,string name,Mesh mesh,Material mat,Vector3 position,Vector3 scale)
        {var t=Node(name,parent);t.localPosition=position;t.localScale=scale;t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;t.gameObject.AddComponent<MeshRenderer>().sharedMaterial=mat;return t;}
        private static Mesh BevelledBox()
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();
            var ring=new[]{new Vector2(-.36f,-.5f),new Vector2(.36f,-.5f),new Vector2(.5f,-.36f),new Vector2(.5f,.36f),new Vector2(.36f,.5f),new Vector2(-.36f,.5f),new Vector2(-.5f,.36f),new Vector2(-.5f,-.36f)};
            void Face(Vector3 a,Vector3 b,Vector3 c){int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles.AddRange(new[]{i,i+1,i+2});}
            for(int n=0;n<8;n++)
            {
                var a=ring[n];var b=ring[(n+1)%8];Vector3 p=new Vector3(a.x,a.y,-.5f),q=new Vector3(b.x,b.y,-.5f),r=new Vector3(b.x,b.y,.5f),s=new Vector3(a.x,a.y,.5f);
                Face(p,q,r);Face(p,r,s);Face(new Vector3(0,0,-.5f),q,p);Face(new Vector3(0,0,.5f),s,r);
            }
            var mesh=new Mesh{name="Deagle bevelled extrusion"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static Mesh Cylinder()
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();const int count=16;
            for(int n=0;n<count;n++)
            {
                float a=n*Mathf.PI*2/count,b=(n+1)*Mathf.PI*2/count;var p=new Vector3(Mathf.Cos(a)*.5f,Mathf.Sin(a)*.5f,-.5f);var q=new Vector3(Mathf.Cos(b)*.5f,Mathf.Sin(b)*.5f,-.5f);
                var r=q+Vector3.forward;var s=p+Vector3.forward;int i=vertices.Count;vertices.AddRange(new[]{p,q,r,s,new Vector3(0,0,-.5f),new Vector3(0,0,.5f)});
                triangles.AddRange(new[]{i,i+1,i+2,i,i+2,i+3,i+4,i+1,i,i+5,i+3,i+2});
            }
            var mesh=new Mesh{name="Deagle muzzle and casing"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private AudioClip Sound(string name,float duration,float frequency,float volume)
        {
            const int rate=22050;var samples=new float[(int)(rate*duration)];uint noise=893;
            for(int n=0;n<samples.Length;n++)
            {float t=n/(float)rate;noise=unchecked(noise*1664525+1013904223);float random=(noise>>8)/8388608f-1;samples[n]=volume*(random*Mathf.Exp(-t*(frequency<200?55:85))*.75f+Mathf.Sin(t*frequency*Mathf.PI*2)*Mathf.Exp(-t*(frequency<200?18:65))*.5f);}
            var clip=AudioClip.Create(name,samples.Length,1,rate,false);clip.SetData(samples,0);resources.Add(clip);return clip;
        }
        private AudioClip WaterSound()
        {
            const int rate=22050;var samples=new float[(int)(rate*.28f)];uint noise=479;
            for(int n=0;n<samples.Length;n++)
            {
                float t=n/(float)rate;noise=unchecked(noise*1664525+1013904223);
                float spray=((noise>>8)/8388608f-1)*Mathf.Exp(-t*22)*.14f;
                float drop=Mathf.Sin((340*t-300*t*t)*Mathf.PI*2)*Mathf.Exp(-t*28)*.20f;
                samples[n]=(spray+drop)*Mathf.Clamp01(t/.003f);
            }
            var clip=AudioClip.Create("Small bullet water splash",samples.Length,1,rate,false);
            clip.SetData(samples,0);resources.Add(clip);return clip;
        }
        public void Dispose(){foreach(var resource in resources)Release(resource);resources.Clear();}
        private static void Release(Object value){if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
    }
}
