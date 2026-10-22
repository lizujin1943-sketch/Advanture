using System;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Low-poly equipped ring and surface wake. This class never mutates gameplay state.</summary>
    public sealed class SwimmingAppearance : IDisposable
    {
        public const float FeetInWater=-.31f;
        private readonly Transform ring;
        private readonly LineRenderer ripple;
        private readonly Material ringMaterial,rippleMaterial;
        private readonly Mesh mesh;
        private int shownLevel=-1;
        public SwimmingAppearance(Transform body,Transform root,Shader lineShader)
        {
            ring=new GameObject("Equipped swim ring").transform;ring.SetParent(body,false);ring.localPosition=new Vector3(0,.72f,0);
            mesh=CreateRingMesh();ring.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
            ringMaterial=new Material(Shader.Find("Hook Island/Baked Color Surface")){name="Swim ring material"};
            ring.gameObject.AddComponent<MeshRenderer>().sharedMaterial=ringMaterial;
            var go=new GameObject("Swimming wake");go.transform.SetParent(root,false);ripple=go.AddComponent<LineRenderer>();
            rippleMaterial=new Material(lineShader){name="Swimmer wake material"};ripple.sharedMaterial=rippleMaterial;
            ripple.useWorldSpace=true;ripple.loop=true;ripple.positionCount=24;ripple.startWidth=ripple.endWidth=.025f;
            ripple.startColor=ripple.endColor=new Color(.82f,1,1,.7f);ripple.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        public void Present(int level,bool water,bool moving,Vector3 position)
        {
            ring.gameObject.SetActive(level>0);ripple.enabled=water;
            if(level!=shownLevel)
            {
                shownLevel=level;ringMaterial.color=Color.Lerp(new Color(1,.51f,.12f),new Color(.25f,.88f,.91f),Mathf.Clamp01((level-1)/9f));
            }
            if(!water)return;
            float radius=.4f+(moving?.035f:.015f)*Mathf.Sin(Time.time*5);
            for(int i=0;i<24;i++)
            {float a=i*Mathf.PI*2/24;ripple.SetPosition(i,new Vector3(position.x+Mathf.Cos(a)*radius,.065f,position.z+Mathf.Sin(a)*radius));}
        }
        public static Mesh CreateRingMesh()
        {
            const int segments=16,sides=6;var vertices=new Vector3[segments*sides*6];var colors=new Color[vertices.Length];var tris=new int[vertices.Length];
            int index=0;
            for(int n=0;n<segments;n++)for(int s=0;s<sides;s++)
            {
                int[] ns={n,n+1,n+1,n,n+1,n};int[] ss={s,s,s+1,s,s+1,s+1};
                for(int k=0;k<6;k++)
                {
                    float a=ns[k]*Mathf.PI*2/segments,b=ss[k]*Mathf.PI*2/sides;
                    float r=.65f+Mathf.Cos(b)*.18f;
                    vertices[index]=new Vector3(Mathf.Cos(a)*r,Mathf.Sin(b)*.18f,Mathf.Sin(a)*r);
                    colors[index]=(n/2)%2==0?Color.white:new Color(.72f,.82f,.9f);tris[index]=index;index++;
                }
            }
            for(int i=0;i<tris.Length;i+=3){int t=tris[i+1];tris[i+1]=tris[i+2];tris[i+2]=t;}
            var m=new Mesh{name="Low polygon swim ring"};m.vertices=vertices;m.colors=colors;m.triangles=tris;m.RecalculateNormals();m.RecalculateBounds();return m;
        }
        public void Dispose(){Release(ring.gameObject);Release(ripple.gameObject);Release(mesh);Release(ringMaterial);Release(rippleMaterial);}
        private static void Release(Object value){if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
    }
}
