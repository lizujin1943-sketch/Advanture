using System.Collections.Generic;
using UnityEngine;
namespace Advanture.TacticalGame.NetworkView
{
    /// <summary>
    /// 本项目原创的低多边形 AK-47 外观：木托、木护木、机匣、弯弹匣和机械瞄具。
    /// 这里只创建可见网格，不创建 Collider，也不参与射线命中或弹药计算。
    /// </summary>
    internal sealed class Ak47Model
    {
        private readonly Transform root;
        private readonly Mesh cube, cylinder;
        private readonly Material metal, edge, wood, dark;
        internal Transform Muzzle { get; }
        internal Transform Magazine { get; }
        internal Ak47Model(Transform parent, List<Mesh> meshes, List<Material> materials, bool firstPerson)
        {
            root = new GameObject("AK47").transform; root.SetParent(parent, false);
            metal = Material("AK47 Steel", new Color(.095f,.115f,.13f), .75f, materials);
            edge = Material("AK47 Edges", new Color(.25f,.28f,.29f), .8f, materials);
            wood = Material("AK47 Walnut", new Color(.43f,.16f,.055f), .05f, materials);
            dark = Material("AK47 Grip", new Color(.12f,.07f,.045f), .05f, materials);
            cube = Cube(); cylinder = Cylinder(); meshes.Add(cube); meshes.Add(cylinder);
            Box("Receiver",new Vector3(0,0,.015f),new Vector3(.075f,.095f,.33f),metal);
            Box("DustCover",new Vector3(0,.057f,.005f),new Vector3(.068f,.035f,.30f),metal);
            Box("WoodStock",new Vector3(0,-.025f,-.285f),new Vector3(.062f,.115f,.26f),wood,new Vector3(-8,0,0));
            Box("ButtPlate",new Vector3(0,-.044f,-.418f),new Vector3(.07f,.135f,.018f),metal,new Vector3(-8,0,0));
            Box("PistolGrip",new Vector3(0,-.12f,-.105f),new Vector3(.053f,.17f,.065f),dark,new Vector3(-18,0,0));
            Box("TriggerGuardBottom",new Vector3(0,-.097f,-.015f),new Vector3(.022f,.012f,.09f),metal);
            Box("TriggerGuardFront",new Vector3(0,-.075f,.025f),new Vector3(.022f,.055f,.012f),metal);
            Box("Trigger",new Vector3(0,-.067f,-.02f),new Vector3(.012f,.032f,.015f),edge,new Vector3(-20,0,0));
            Box("WoodHandguard",new Vector3(0,-.005f,.27f),new Vector3(.085f,.085f,.21f),wood);
            Box("UpperHandguard",new Vector3(0,.052f,.27f),new Vector3(.065f,.04f,.20f),wood);
            for(int i=0;i<3;i++) Box("HandguardVent"+i,new Vector3(.044f,.025f,.21f+i*.05f),new Vector3(.004f,.013f,.025f),dark);
            Tube("Barrel",new Vector3(0,.018f,.53f),.014f,.33f,metal);
            Tube("GasTube",new Vector3(0,.064f,.405f),.012f,.20f,metal);
            Box("GasBlock",new Vector3(0,.05f,.49f),new Vector3(.044f,.07f,.035f),metal);
            Box("FrontSightBase",new Vector3(0,.055f,.635f),new Vector3(.035f,.08f,.026f),metal);
            Box("FrontSightPost",new Vector3(0,.106f,.635f),new Vector3(.007f,.026f,.012f),edge);
            for(int side=-1;side<=1;side+=2) Box("SightEar"+side,new Vector3(side*.015f,.103f,.635f),new Vector3(.006f,.03f,.024f),metal);
            Box("RearSight",new Vector3(0,.084f,.135f),new Vector3(.043f,.023f,.058f),edge);
            Tube("MuzzleBrake",new Vector3(0,.018f,.709f),.020f,.045f,metal);
            Tube("MuzzleOpening",new Vector3(0,.018f,.733f),.013f,.002f,dark);
            Box("ChargingHandle",new Vector3(.058f,.028f,.05f),new Vector3(.055f,.013f,.025f),edge);
            Box("SafetyLever",new Vector3(.039f,0,-.035f),new Vector3(.005f,.018f,.13f),edge,new Vector3(10,0,0));
            Magazine = new GameObject("CurvedMagazine").transform; Magazine.SetParent(root,false);
            // 四段不同角度的盒体构成清晰弧线，轮廓适合当前工业庭院的低多边形风格。
            for(int i=0;i<4;i++)
            {
                var part=Box("MagazineSegment"+i,new Vector3(0,-.08f-i*.063f,.055f+i*i*.009f),new Vector3(.058f,.085f,.11f),metal,new Vector3(-i*13,0,0));
                part.SetParent(Magazine,true);
                for(int side=-1;side<=1;side+=2)
                {
                    var rib=Box("MagazineRib"+i+"_"+side,new Vector3(side*.030f,-.08f-i*.063f,.05f+i*i*.009f),new Vector3(.004f,.059f,.016f),edge,new Vector3(-i*13,0,0));
                    rib.SetParent(Magazine,true);
                }
            }
            Muzzle=new GameObject("Muzzle").transform; Muzzle.SetParent(root,false);Muzzle.localPosition=new Vector3(0,.018f,.75f);
            if(firstPerson)
            {
                var glove=Material("Gloves",new Color(.19f,.23f,.22f),0,materials);
                Box("RightGlove",new Vector3(.022f,-.1f,-.09f),new Vector3(.095f,.095f,.11f),glove,new Vector3(-12,0,-8));
                Box("LeftGlove",new Vector3(-.025f,-.06f,.27f),new Vector3(.105f,.07f,.11f),glove,new Vector3(0,0,-12));
            }
        }
        internal static Material Material(string name,Color color,float metallic,List<Material> owned)
        {
            var material=new Material(Shader.Find("Standard")){name=name,color=color};material.SetFloat("_Metallic",metallic);material.SetFloat("_Glossiness",.28f);owned.Add(material);return material;
        }
        private Transform Box(string name,Vector3 position,Vector3 size,Material material,Vector3 rotation=default)
        { return Part(name,cube,position,size,material,rotation); }
        private void Tube(string name,Vector3 position,float radius,float length,Material material)
        { Part(name,cylinder,position,new Vector3(radius,radius,length),material,Vector3.zero); }
        private Transform Part(string name,Mesh mesh,Vector3 position,Vector3 size,Material material,Vector3 rotation)
        {
            var part=new GameObject(name);part.transform.SetParent(root,false);part.transform.localPosition=position;
            part.transform.localRotation=Quaternion.Euler(rotation);part.transform.localScale=size;
            part.AddComponent<MeshFilter>().sharedMesh=mesh;part.AddComponent<MeshRenderer>().sharedMaterial=material;return part.transform;
        }
        private static Mesh Cube()
        {
            var corners=new[]{new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(-.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,.5f),new Vector3(.5f,-.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,.5f,.5f)};
            int[] faces={0,3,2,1,4,5,6,7,0,4,7,3,1,2,6,5,3,7,6,2,0,1,5,4};
            var vertices=new Vector3[24];var triangles=new int[36];
            for(int f=0;f<6;f++){for(int v=0;v<4;v++)vertices[f*4+v]=corners[faces[f*4+v]];int o=f*6,n=f*4;triangles[o]=n;triangles[o+1]=n+1;triangles[o+2]=n+2;triangles[o+3]=n;triangles[o+4]=n+2;triangles[o+5]=n+3;}
            var mesh=new Mesh{name="AK47 Box",vertices=vertices,triangles=triangles};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
        private static Mesh Cylinder()
        {
            var vertices=new List<Vector3>();var triangles=new List<int>();const int sides=10;
            for(int i=0;i<sides;i++)
            {
                float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;
                var p=new Vector3(Mathf.Cos(a),Mathf.Sin(a),-.5f);var q=new Vector3(Mathf.Cos(b),Mathf.Sin(b),-.5f);int n=vertices.Count;
                vertices.AddRange(new[]{p,q,q+Vector3.forward,p+Vector3.forward,new Vector3(0,0,-.5f),new Vector3(0,0,.5f)});
                triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3,n+4,n+1,n,n+5,n+3,n+2});
            }
            var mesh=new Mesh{name="AK47 Tube"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
