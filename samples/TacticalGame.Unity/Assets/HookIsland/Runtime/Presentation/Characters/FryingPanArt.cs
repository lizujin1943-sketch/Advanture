using System;
using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Shared lightweight pan geometry, materials and sounds. No colliders or combat rules.</summary>
    public sealed class FryingPanArt : IDisposable
    {
        public Mesh Mesh {get;}
        public Material[] Materials {get;}
        public Material TrailMaterial {get;}
        public AudioClip Swing {get;}
        public AudioClip Impact {get;}
        public FryingPanArt(Shader lineShader)
        {
            Mesh=BuildMesh();
            Materials=new[]{Material("Iron",new Color(.18f,.23f,.26f),.68f,.4f),Material("Pan interior",new Color(.055f,.075f,.09f),.25f,.23f),Material("Wooden grip",new Color(.48f,.23f,.075f),0,.27f)};
            TrailMaterial=new Material(lineShader){name="Pan swing ribbon"};
            Swing=Sound(false);Impact=Sound(true);
        }
        public Transform Create(Transform parent)
        {
            var go=new GameObject("Frying pan");go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh=Mesh;go.AddComponent<MeshRenderer>().sharedMaterials=Materials;
            return go.transform;
        }
        private static Material Material(string name,Color color,float metallic,float smoothness)
        {var m=new Material(Shader.Find("Standard")){name=name,color=color};m.SetFloat("_Metallic",metallic);m.SetFloat("_Glossiness",smoothness);return m;}
        private static Mesh BuildMesh()
        {
            const int segments=32;
            var vertices=new List<Vector3>();var tris=new[]{new List<int>(),new List<int>(),new List<int>()};
            float[] radius={0,.16f,.205f,.235f,.217f,.195f,0};
            float[] height={-.035f,-.035f,-.022f,.045f,.045f,-.004f,-.004f};
            for(int r=0;r<radius.Length;r++)for(int i=0;i<=segments;i++)
            {float a=i*Mathf.PI*2/segments;vertices.Add(new Vector3(Mathf.Cos(a)*radius[r],height[r],.52f+Mathf.Sin(a)*radius[r]));}
            for(int r=0;r<radius.Length-1;r++)for(int i=0;i<segments;i++)
            {int a=r*(segments+1)+i,b=a+segments+1;var t=tris[r>=4?1:0];t.AddRange(new[]{a,b,a+1,a+1,b,b+1});}
            Box(new Vector3(0,.004f,.145f),new Vector3(.06f,.058f,.29f),2);
            Box(new Vector3(0,.004f,.29f),new Vector3(.043f,.028f,.13f),0);
            Box(new Vector3(0,.036f,.21f),new Vector3(.024f,.007f,.022f),0);
            Box(new Vector3(0,.036f,.26f),new Vector3(.024f,.007f,.022f),0);
            var mesh=new Mesh{name="Frying pan • 32 segment cast iron"};mesh.SetVertices(vertices);mesh.subMeshCount=3;
            for(int i=0;i<3;i++)mesh.SetTriangles(tris[i],i);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
            void Box(Vector3 center,Vector3 size,int material)
            {
                var points=new[]{new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,1,-1),new Vector3(-1,1,-1),new Vector3(-1,-1,1),new Vector3(1,-1,1),new Vector3(1,1,1),new Vector3(-1,1,1)};
                int[] faces={0,3,2,1,4,5,6,7,0,4,7,3,1,2,6,5,3,7,6,2,0,1,5,4};
                for(int f=0;f<6;f++){int start=vertices.Count;for(int i=0;i<4;i++)vertices.Add(center+Vector3.Scale(points[faces[f*4+i]],size*.5f));tris[material].AddRange(new[]{start,start+1,start+2,start,start+2,start+3});}
            }
        }
        private static AudioClip Sound(bool impact)
        {
            const int rate=22050;float duration=impact?.32f:.16f;var samples=new float[(int)(rate*duration)];uint random=771;
            for(int i=0;i<samples.Length;i++)
            {
                float t=i/(float)rate;random=unchecked(random*1664525+1013904223);float noise=((random>>8)/8388608f-1);
                samples[i]=impact?(.48f*Mathf.Sin(t*575*Mathf.PI*2)*Mathf.Exp(-t*19)+.23f*Mathf.Sin(t*1437*Mathf.PI*2)*Mathf.Exp(-t*25)+noise*.25f*Mathf.Exp(-t*100)):
                    noise*.38f*Mathf.Sin(t/duration*Mathf.PI)*Mathf.Exp(-t*12);
            }
            var clip=AudioClip.Create(impact?"Pan clang":"Pan swoosh",samples.Length,1,rate,false);clip.SetData(samples,0);return clip;
        }
        public void Dispose(){Release(Mesh);foreach(var m in Materials)Release(m);Release(TrailMaterial);Release(Swing);Release(Impact);}
        private static void Release(Object o){if(Application.isPlaying)Object.Destroy(o);else Object.DestroyImmediate(o);}
    }
}
