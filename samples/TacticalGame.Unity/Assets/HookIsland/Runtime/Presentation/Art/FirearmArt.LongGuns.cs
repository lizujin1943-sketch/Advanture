using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    public sealed partial class FirearmArt
    {
        private readonly Dictionary<string,AudioClip> reports=new Dictionary<string,AudioClip>();
        private Material steel,wood,olive,sand,lens;
        public static float Recoil(FirearmDefinition gun)=>gun.Id=="barrett"?1.8f:gun.Category==FirearmCategory.Sniper?1.3f:gun.Automatic?.65f:1;
        public static Vector3 SupportGrip(FirearmDefinition gun)=>gun.Category==FirearmCategory.Pistol?
            new Vector3(-.055f,-.035f,.028f):new Vector3(-.035f,-.025f,.24f);
        public AudioClip Report(int type)
        {
            var gun=FirearmCatalog.Get(type);if(gun.Category==FirearmCategory.Pistol)return Shot;
            if(!reports.TryGetValue(gun.Id,out var clip))
            {clip=Sound(gun.Name+" report",gun.Automatic?.18f:.45f,gun.Id=="barrett"?48:gun.Automatic?155:70,gun.Automatic?.85f:1);reports.Add(gun.Id,clip);}
            return clip;
        }
        private Model CreateLongGun(Transform parent,FirearmDefinition gun)
        {
            if(steel==null)
            {
                steel=Surface("Gun blued steel",new Color(.12f,.16f,.19f),.75f,.38f);
                wood=Surface("AK walnut furniture",new Color(.43f,.19f,.07f),.05f,.30f);
                olive=Surface("AWM olive chassis",new Color(.28f,.35f,.18f),.16f,.28f);
                sand=Surface("Barrett sand chassis",new Color(.48f,.40f,.27f),.45f,.35f);
                lens=Surface("Scope blue glass",new Color(.07f,.30f,.39f),.8f,.88f);
            }
            bool rifle=gun.Category==FirearmCategory.Rifle,heavy=gun.Id=="barrett";
            var furniture=rifle?wood:heavy?sand:olive;
            var m=new Model{Root=Node(gun.Name,parent)};
            m.Slide=Node(rifle?"Moving bolt carrier":"Moving bolt",m.Root);
            m.Magazine=Node("Removable magazine",m.Root);
            void Box(Transform root,string name,Material mat,float x,float y,float z,float sx,float sy,float sz)
                =>Part(root,name,bevel,mat,new Vector3(x,y,z),new Vector3(sx,sy,sz));
            void Tube(Transform root,string name,Material mat,float x,float y,float z,float radius,float length)
                =>Part(root,name,cylinder,mat,new Vector3(x,y,z),new Vector3(radius,radius,length));
            float muzzle=rifle?.64f:heavy?.98f:.83f;
            Box(m.Root,"Receiver",steel,0,.07f,.08f,.088f,.105f,.32f);
            Box(m.Root,"Stock",furniture,0,.035f,-.24f,.08f,.14f,.29f);
            Box(m.Root,"Rubber butt pad",grip,0,.032f,-.39f,.09f,.16f,.027f);
            Box(m.Root,"Cheek rest",furniture,0,.12f,-.24f,.082f,.045f,.17f);
            var handle=Part(m.Root,"Pistol grip",bevel,grip,new Vector3(0,-.055f,-.025f),new Vector3(.06f,.145f,.07f));handle.localRotation=Quaternion.Euler(-16,0,0);
            Box(m.Root,"Trigger guard",steel,0,-.024f,.055f,.032f,.018f,.10f);
            Box(m.Root,"Trigger guard front",steel,0,.0f,.10f,.03f,.065f,.015f);
            Box(m.Root,"Trigger",black,0,.008f,.05f,.012f,.045f,.013f);
            Box(m.Root,"Fore-end",furniture,0,.06f,.29f,.10f,heavy?.13f:.09f,heavy?.33f:.25f);
            Tube(m.Root,"Barrel",steel,0,.105f,(muzzle+.3f)*.5f,heavy?.043f:.032f,muzzle-.3f);
            Tube(m.Root,"Muzzle brake",steel,0,.105f,muzzle,heavy?.077f:.044f,heavy?.105f:.045f);
            Tube(m.Root,"Dark bore",black,0,.105f,muzzle+(heavy?.054f:.024f),heavy?.028f:.018f,.003f);
            Box(m.Slide,"Bolt carrier",silver,.047f,.097f,.035f,.011f,.022f,.075f);
            Box(m.Slide,"Charging handle",steel,.067f,.092f,.04f,.045f,.018f,.017f);
            if(rifle)
            {
                Tube(m.Root,"Gas tube",steel,0,.15f,.39f,.028f,.27f);
                Box(m.Root,"Front sight post",steel,0,.16f,.57f,.026f,.12f,.03f);
                Box(m.Root,"Rear sight",steel,0,.143f,.09f,.045f,.03f,.03f);
                for(int i=0;i<4;i++)
                {
                    float t=i/3f;
                    var segment=Part(m.Magazine,"Curved magazine segment",bevel,steel,new Vector3(0,-.025f-i*.04f,.15f+t*t*.055f),new Vector3(.058f,.06f,.12f));
                    segment.localRotation=Quaternion.Euler(-i*10,0,0);
                    Box(m.Magazine,"Magazine rib",black,.031f,-.03f-i*.04f,.15f+t*t*.055f,.004f,.012f,.095f);
                }
            }
            else
            {
                Box(m.Magazine,"Box magazine",steel,0,-.035f,.11f,.075f,heavy?.16f:.11f,.12f);
                Box(m.Magazine,"Magazine base",grip,0,heavy?-.12f:-.095f,.11f,.083f,.02f,.135f);
                Box(m.Root,"Optic rail",steel,0,.133f,.04f,.055f,.026f,.29f);
                for(int i=0;i<8;i++)Box(m.Root,"Rail slot",black,0,.151f,-.085f+i*.034f,.062f,.008f,.01f);
                for(int side=-1;side<=1;side+=2)
                {
                    Box(m.Root,"Scope mount",steel,0,.175f,side*.09f,.055f,.075f,.035f);
                    var leg=Part(m.Root,"Folded bipod",bevel,steel,new Vector3(side*.055f,-.035f,.40f),new Vector3(.018f,.022f,.23f));
                    leg.localRotation=Quaternion.Euler(0,side*9,0);
                    for(int i=0;i<5;i++)Box(m.Root,"Fore-end vent",black,side*.051f,.072f,.18f+i*.047f,.003f,.025f,.028f);
                }
                Tube(m.Root,"Optical scope",black,0,.224f,.02f,.065f,.32f);
                Tube(m.Root,"Objective bell",steel,0,.224f,.205f,heavy?.105f:.092f,.09f);
                Tube(m.Root,"Objective glass",lens,0,.224f,.252f,heavy?.084f:.072f,.004f);
                Tube(m.Root,"Eyepiece",grip,0,.224f,-.16f,.082f,.055f);
                Tube(m.Root,"Eyepiece glass",lens,0,.224f,-.19f,.062f,.003f);
                Box(m.Root,"Elevation turret",steel,0,.272f,.025f,.044f,.045f,.045f);
                if(heavy)for(int i=0;i<3;i++)for(int side=-1;side<=1;side+=2)
                    Box(m.Root,"Brake vent",black,side*.037f,.105f,muzzle-.033f+i*.026f,.004f,.043f,.012f);
            }
            m.Muzzle=Node("Muzzle",m.Root);m.Muzzle.localPosition=new Vector3(0,.105f,muzzle+(heavy?.06f:.03f));
            // Merge each rigid part by material once. Long guns do not add dozens of draw calls per player.
            MergeParts(m.Root);MergeParts(m.Slide);MergeParts(m.Magazine);
            m.Root.gameObject.SetActive(false);return m;
        }
        private void MergeParts(Transform root)
        {
            var parts=root.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.transform.parent==root).ToArray();
            foreach(var group in parts.GroupBy(f=>f.GetComponent<MeshRenderer>().sharedMaterial))
            {
                var mesh=new Mesh{name=root.name+" "+group.Key.name};
                mesh.CombineMeshes(group.Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=root.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray());
                resources.Add(mesh);Part(root,"Combined "+group.Key.name,mesh,group.Key,Vector3.zero,Vector3.one);
            }
            foreach(var part in parts){part.gameObject.SetActive(false);Release(part.gameObject);}
        }
    }
}
