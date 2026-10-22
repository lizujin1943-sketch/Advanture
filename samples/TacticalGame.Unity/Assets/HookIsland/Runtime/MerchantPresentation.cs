using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Stationary supply boats. Geometry is decorative; the simulation owns their footprint.</summary>
    public sealed class MerchantPresentation:IDisposable
    {
        private readonly Transform root;
        private readonly List<Transform> ships=new List<Transform>();
        private readonly List<Material> materials=new List<Material>();
        private readonly Mesh hull;
        public int ShipCount=>ships.Count;
        public MerchantPresentation(Transform parent,FishingConfig config)
        {
            root=new GameObject("Supply boats (configured)").transform;root.SetParent(parent,false);
            hull=CreateHull();
            var wood=Surface("Boat wood",new Color(.30f,.17f,.10f));
            var deck=Surface("Boat deck",new Color(.67f,.44f,.23f));
            var trim=Surface("Boat trim",new Color(.88f,.73f,.43f));
            var goods=Surface("Boat cargo",new Color(.19f,.31f,.30f));
            var colors=new[]{new Color(.91f,.50f,.22f),new Color(.30f,.65f,.63f),new Color(.52f,.51f,.78f),new Color(.83f,.41f,.43f)};
            foreach(var definition in config.Merchants.Definitions)
            {
                var ship=new GameObject(MerchantHud.Name(config,definition.Id)).transform;ship.SetParent(root,false);
                ship.localPosition=new Vector3(definition.X/1000f,0,definition.Z/1000f);ships.Add(ship);
                var body=new GameObject("Tapered hull");body.transform.SetParent(ship,false);
                body.AddComponent<MeshFilter>().sharedMesh=hull;body.AddComponent<MeshRenderer>().sharedMaterial=wood;
                var canvas=Surface("Supply canvas "+definition.Id,colors[definition.Id-1]);
                Part(ship,"Deck",new Vector3(0,.17f,-.12f),new Vector3(1.38f,.09f,1.08f),deck);
                for(int side=-1;side<=1;side+=2)
                {
                    Part(ship,"Gunwale",new Vector3(side*.73f,.25f,-.20f),new Vector3(.08f,.14f,1.15f),trim);
                    Part(ship,"Awning post",new Vector3(side*.46f,.75f,-.38f),new Vector3(.045f,1.08f,.045f),wood);
                }
                Part(ship,"Counter",new Vector3(0,.42f,-.10f),new Vector3(1.04f,.40f,.44f),wood);
                Part(ship,"Counter edge",new Vector3(0,.65f,-.10f),new Vector3(1.12f,.055f,.50f),trim);
                Part(ship,"Awning",new Vector3(0,1.28f,-.22f),new Vector3(1.26f,.065f,.85f),canvas);
                Part(ship,"Cargo",new Vector3(.43f,.35f,-.58f),new Vector3(.32f,.28f,.26f),goods);
                for(int n=0;n<3;n++)Part(ship,"Displayed rod",new Vector3(-.30f+n*.28f,.69f,-.12f),new Vector3(.025f,.025f,.38f),trim);
                Part(ship,"Flag mast",new Vector3(-.60f,1.05f,-.64f),new Vector3(.04f,1.70f,.04f),wood);
                Part(ship,"Flag",new Vector3(-.39f,1.72f,-.64f),new Vector3(.40f,.28f,.035f),canvas);
            }
        }
        public void Synchronize(HookState state)
        {
            float time=state==null?0:state.Steps/(float)HookGame.Rate;
            for(int n=0;n<ships.Count;n++)
            {
                var p=ships[n].localPosition;p.y=state==null?0:Mathf.Sin(time*1.4f+n)*.025f;ships[n].localPosition=p;
            }
        }
        private Material Surface(string name,Color color)
        {
            var material=new Material(Shader.Find("Hook Island/Baked Color Surface")){name=name,color=color};
            materials.Add(material);return material;
        }
        private static void Part(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
        {
            var part=GameObject.CreatePrimitive(PrimitiveType.Cube);part.name=name;part.transform.SetParent(parent,false);
            part.transform.localPosition=position;part.transform.localScale=scale;part.GetComponent<Renderer>().sharedMaterial=material;
            var collider=part.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
        }
        private static Mesh CreateHull()
        {
            var rim=new[]{new Vector3(-.80f,.15f,-.80f),new Vector3(.80f,.15f,-.80f),new Vector3(.80f,.15f,.30f),new Vector3(0,.15f,.80f),new Vector3(-.80f,.15f,.30f)};
            var vertices=new List<Vector3>();var triangles=new List<int>();
            void Triangle(Vector3 a,Vector3 b,Vector3 c)
            {int i=vertices.Count;vertices.Add(a);vertices.Add(b);vertices.Add(c);triangles.Add(i);triangles.Add(i+1);triangles.Add(i+2);}
            for(int n=0;n<rim.Length;n++)
            {
                var a=rim[n];var b=rim[(n+1)%rim.Length];
                var lowA=new Vector3(a.x*.72f,-.32f,a.z*.72f);var lowB=new Vector3(b.x*.72f,-.32f,b.z*.72f);
                Triangle(a,b,lowB);Triangle(a,lowB,lowA);
                Triangle(new Vector3(0,.15f,0),b,a);Triangle(new Vector3(0,-.32f,0),lowA,lowB);
            }
            var mesh=new Mesh{name="Supply boat hull"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static void Destroy(Object value){if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
        public void Dispose(){Destroy(root.gameObject);Destroy(hull);foreach(var material in materials)Destroy(material);}
    }
}
