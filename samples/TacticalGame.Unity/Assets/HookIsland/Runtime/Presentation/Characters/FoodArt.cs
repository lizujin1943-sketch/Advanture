using System;
using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Shared compact food props for eating and dropped inventory. No colliders or gameplay components.</summary>
    public sealed class FoodArt : IDisposable
    {
        private readonly Mesh round,tail;
        private readonly Material green,cream,water,wood,gold,charcoal,orange;
        public FoodArt()
        {
            round=RoundMesh();tail=new Mesh{name="Food fish tail"};
            tail.vertices=new[]{new Vector3(0,0,0),new Vector3(1,0,-1),new Vector3(1,0,1)};
            tail.triangles=new[]{0,1,2,2,1,0};tail.RecalculateNormals();tail.RecalculateBounds();
            green=Mat("Coconut green",new Color(.33f,.62f,.16f));cream=Mat("Food cream",new Color(1,.9f,.67f));
            water=Mat("Coconut water",new Color(.58f,.7f,.32f));wood=Mat("Wooden bowl",new Color(.54f,.28f,.11f));
            gold=Mat("Roasted fish",new Color(.94f,.51f,.12f));charcoal=Mat("Grill marks",new Color(.25f,.10f,.035f));orange=Mat("Shrimp",new Color(1,.31f,.09f));
        }
        public Transform Create(Transform parent,int type)
        {
            var root=new GameObject("Food prop "+type).transform;root.SetParent(parent,false);
            if(type==0)
            {
                Part(root,"Green coconut",round,Vector3.zero,new Vector3(.19f,.21f,.19f),green);
                Part(root,"White cut top",round,new Vector3(0,.075f,0),new Vector3(.15f,.03f,.15f),cream);
                Part(root,"Coconut water",round,new Vector3(0,.088f,0),new Vector3(.11f,.01f,.11f),water);
                var straw=Part(root,"Straw",round,new Vector3(.025f,.145f,0),new Vector3(.012f,.13f,.012f),gold);straw.localRotation=Quaternion.Euler(0,0,-16);
            }
            else
            {
                if(type==1)
                {
                    Part(root,"Leaf",round,new Vector3(0,-.03f,0),new Vector3(.36f,.018f,.17f),green);
                    Part(root,"Grilled whole fish",round,Vector3.zero,new Vector3(.27f,.08f,.12f),gold);
                    Part(root,"Tail",tail,new Vector3(.115f,0,0),Vector3.one*.07f,gold);
                    Part(root,"Fish eye",round,new Vector3(-.09f,.027f,.025f),Vector3.one*.016f,charcoal);
                    for(int n=0;n<3;n++)Part(root,"Grill mark",round,new Vector3(-.04f+n*.04f,.039f,0),new Vector3(.009f,.008f,.065f),charcoal);
                }
                else
                {
                    Part(root,"Wooden bowl",round,Vector3.zero,new Vector3(.27f,.13f,.23f),wood);
                    Part(root,"Bowl rim",round,new Vector3(0,.04f,0),new Vector3(.285f,.03f,.245f),cream);
                    Part(root,"Greens",round,new Vector3(0,.052f,0),new Vector3(.24f,.02f,.2f),green);
                    for(int n=0;n<3;n++)Part(root,"Fish meat",round,new Vector3(-.07f+n*.06f,.065f,.025f),new Vector3(.06f,.035f,.08f),cream);
                    for(int n=0;n<6;n++){float a=n*.5f;Part(root,"Shrimp",round,new Vector3(Mathf.Cos(a)*.055f,.078f,-.03f+Mathf.Sin(a)*.045f),new Vector3(.035f,.03f,.03f),orange);}
                }
            }
            return root;
        }
        private static Transform Part(Transform root,string name,Mesh mesh,Vector3 position,Vector3 scale,Material material)
        {
            var part=new GameObject(name).transform;part.SetParent(root,false);part.localPosition=position;part.localScale=scale;
            part.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;part.gameObject.AddComponent<MeshRenderer>().sharedMaterial=material;return part;
        }
        private static Material Mat(string name,Color color)
        {var material=new Material(Shader.Find("Hook Island/Baked Color Surface")){name=name,color=color};material.SetFloat("_Ambient",.5f);material.SetFloat("_SunStrength",.8f);return material;}
        private static Mesh RoundMesh()
        {
            const int sides=12,rings=6;var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int y=0;y<=rings;y++)for(int x=0;x<=sides;x++)
            {float a=x*Mathf.PI*2/sides,b=y*Mathf.PI/rings;vertices.Add(new Vector3(Mathf.Sin(b)*Mathf.Cos(a),Mathf.Cos(b),Mathf.Sin(b)*Mathf.Sin(a))*.5f);}
            for(int y=0;y<rings;y++)for(int x=0;x<sides;x++)
            {int a=y*(sides+1)+x,b=a+sides+1;triangles.AddRange(new[]{a,a+1,b,a+1,b+1,b});}
            var mesh=new Mesh{name="Food smooth 12x6"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        public void Dispose(){foreach(var item in new Object[]{round,tail,green,cream,water,wood,gold,charcoal,orange}){if(Application.isPlaying)Object.Destroy(item);else Object.DestroyImmediate(item);}}
    }
}
