using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Only the deduplicated confirmed-event consumer calls Apply. No predicted impacts or ammunition writes.</summary>
    public sealed class FirearmFeedback : IDisposable
    {
        private sealed class ShotEffect
        {
            public Transform Root,Casing,Label;
            public LineRenderer Trace,Flash,Smoke,Hit;
            public Vector3 Start,End,Direction;
            public bool Impact;
            public float Started;
        }
        private readonly Transform root;
        private readonly DesertEagleArt art;
        private readonly AudioSource audio;
        private readonly FirearmWaterImpactView waterImpacts;
        private readonly List<ShotEffect> shots=new List<ShotEffect>();
        private readonly LineRenderer spread;
        public int PresentedShots {get;private set;}
        public FirearmFeedback(Transform parent,DesertEagleArt art)
        {
            this.art=art;root=new GameObject("Confirmed firearm feedback").transform;root.SetParent(parent,false);
            audio=root.gameObject.AddComponent<AudioSource>();audio.spatialBlend=0;audio.volume=.42f;
            waterImpacts=new FirearmWaterImpactView(root);
            spread=Line(root,"Movement spread arc",.018f);spread.positionCount=17;
        }
        public void Empty()=>audio.PlayOneShot(art.Empty,.5f);
        public void Apply(HookGameplayEvent value,ushort local,IslandMap map,HookState state,Vector3? muzzle=null)
        {
            if(value.Kind==HookGameplayEventKind.FirearmReloadStarted){audio.PlayOneShot(art.Reload,value.Slot==local?.65f:.3f);return;}
            if(value.Kind==HookGameplayEventKind.FirearmReloaded){audio.PlayOneShot(art.Loaded,value.Slot==local?.65f:.3f);return;}
            if(value.Kind!=HookGameplayEventKind.FirearmShot)return;
            PresentedShots++;audio.PlayOneShot(art.Shot,value.Slot==local?1:.55f);
            if(shots.Count>=32){Release(shots[0].Root.gameObject);shots.RemoveAt(0);}
            var node=new GameObject("Confirmed shot").transform;node.SetParent(root,false);
            // Surface membership, not the wider character standing footprint: a bullet
            // at a deck lip still hits wood even when a player cannot stand there safely.
            bool water=map.Platform==null?!map.CanStand(value.X,value.Z):!PlatformRules.OnPlatform(state.Tiles,value.X,value.Z);
            float originHeight=map.Platform==null?1.3f:PlatformGeometry.DeckHeight+.65f;
            if(value.Slot<state.Players.Count && state.Players[value.Slot].Swimming.InWater)originHeight=.48f;
            var start=new Vector3(value.OriginX/1000f,originHeight,value.OriginZ/1000f);
            bool splash=value.TargetId==-1 && water;
            float hitHeight=splash?waterImpacts.SurfaceHeight:value.TargetId==-2 || value.TargetId==-3?.14f:originHeight;
            var end=new Vector3(value.X/1000f,hitHeight,value.Z/1000f);
            var delta=end-start;var direction=new Vector3(delta.x,0,delta.z).normalized;
            // Only the visual origin follows the rendered gun. Never move the confirmed hit
            // to the current cursor or roll a second spread direction on this client.
            start=muzzle??start+direction*Mathf.Min(.34f,new Vector2(delta.x,delta.z).magnitude*.3f);
            var effect=new ShotEffect{Root=node,Start=start,End=end,Direction=(end-start).normalized,Started=Time.unscaledTime,Impact=value.TargetId!=-1};
            effect.Trace=Line(node,"Short bullet trace",.023f);effect.Trace.positionCount=2;
            effect.Flash=Line(node,"Warm muzzle flash",.035f);effect.Flash.positionCount=9;
            effect.Smoke=Line(node,"Brief muzzle smoke",.023f);effect.Smoke.positionCount=8;
            effect.Hit=Line(node,"Confirmed impact",.025f);effect.Hit.positionCount=17;
            effect.Casing=art.Casing(node);
            if(splash)
            {
                waterImpacts.Add(end,direction,Time.unscaledTime,unchecked((uint)value.X*73856093u^(uint)value.Z*19349663u^(uint)value.Slot));
                audio.PlayOneShot(art.WaterImpact,value.Slot==local?.6f:.3f);
            }
            if(value.TargetId!=-1)audio.PlayOneShot(art.Impact,value.Slot==local?.45f:.2f);
            if(value.TargetId==-2 && value.Amount>0)
            {
                effect.Label=new GameObject("Confirmed firearm damage").transform;effect.Label.SetParent(node,false);
                var text=effect.Label.gameObject.AddComponent<TextMesh>();text.text="−"+value.Amount;text.characterSize=.065f;text.fontSize=40;text.anchor=TextAnchor.MiddleCenter;text.color=new Color(1,.94f,.74f);
                text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.GetComponent<MeshRenderer>().sharedMaterial=text.font.material;
            }
            shots.Add(effect);
        }
        public void Present(IslandMap map,HookState state,HookState predicted,ushort local,Vector3 aim,bool show)
        {
            PresentSpread(map,state,predicted,local,aim,show);
            waterImpacts.Present(Time.unscaledTime);
            for(int i=shots.Count-1;i>=0;i--)
            {
                var s=shots[i];float age=Time.unscaledTime-s.Started;
                if(age>.7f){Release(s.Root.gameObject);shots.RemoveAt(i);continue;}
                s.Trace.enabled=age<.085f;s.Trace.SetPosition(0,Vector3.Lerp(s.Start,s.End,Mathf.Clamp01(age/.09f)*.7f));s.Trace.SetPosition(1,s.End);
                Tint(s.Trace,new Color(1,.84f,.44f,Mathf.Clamp01(1-age/.085f)*.8f));
                s.Flash.enabled=age<.055f;var side=Vector3.Cross(Vector3.up,s.Direction).normalized;var up=Vector3.Cross(s.Direction,side);
                for(int n=0;n<9;n++){float a=n*Mathf.PI/4;float radius=n%2==0?.075f:.025f;s.Flash.SetPosition(n,s.Start+s.Direction*.04f+(side*Mathf.Cos(a)+up*Mathf.Sin(a))*radius);}
                Tint(s.Flash,new Color(1,.72f,.23f,1-age/.055f));
                s.Smoke.enabled=age<.32f;
                for(int n=0;n<8;n++){float t=n/7f;s.Smoke.SetPosition(n,s.Start+s.Direction*(t*.12f+age*.05f)+Vector3.up*(age*.24f+t*.04f)+side*(Mathf.Sin(t*8+age*8)*.012f));}
                Tint(s.Smoke,new Color(.83f,.87f,.88f,.23f*Mathf.Clamp01(1-age/.32f)));
                s.Casing.gameObject.SetActive(age<.45f);s.Casing.position=s.Start+side*(age*.65f)+Vector3.up*(age*1.4f-age*age*4);
                s.Casing.rotation=Quaternion.Euler(age*1040,age*730,age*380);
                s.Hit.enabled=s.Impact && age<.24f;
                for(int n=0;n<17;n++)
                {float a=n*Mathf.PI/8,radius=.04f+age*.6f;var offset=(side*Mathf.Cos(a)+Vector3.up*Mathf.Sin(a))*radius*(n%2==0?1:.3f);s.Hit.SetPosition(n,s.End+offset);}
                Tint(s.Hit,new Color(1,.94f,.66f,1-age/.24f));
                if(s.Label!=null){s.Label.position=s.End+Vector3.up*(.3f+age*.6f);if(Camera.main!=null)s.Label.rotation=Camera.main.transform.rotation;var text=s.Label.GetComponent<TextMesh>();text.color=new Color(1,.94f,.74f,Mathf.Clamp01(1-age/.7f));}
            }
        }
        private void PresentSpread(IslandMap map,HookState state,HookState predicted,ushort local,Vector3 aim,bool show)
        {
            spread.enabled=false;if(!show || local>=state.Players.Count)return;
            var player=state.Players[local];var gun=ActiveToolView.Firearm(state,player);if(gun==null || player.Vitals.Downed || player.Unavailable)return;
            var item=state.Tackle.Inventory.Equipped(local,EquipmentSlot.RightHand);if(!item.HasValue)return;
            var motion=local<predicted.Players.Count?predicted.Players[local]:player;
            var direction=aim-new Vector3(motion.X/1000f,0,motion.Z/1000f);direction.y=0;if(direction.sqrMagnitude<.001f)return;direction.Normalize();
            int dx=Mathf.RoundToInt(direction.x*EnemyGeometry.DirectionScale),dz=Mathf.RoundToInt(direction.z*EnemyGeometry.DirectionScale);
            int reach=gun.Range;
            if(map.Platform==null)
            {reach=Math.Min(reach,FirearmRules.RayCircle(motion.X,motion.Z,dx,dz,0,1050,2050,reach));reach=Math.Min(reach,FirearmRules.RayCircle(motion.X,motion.Z,dx,dz,-2150,1550,390,reach));}
            for(int id=0;id<=map.Fishing.Merchants.Definitions.Count;id++)
            {var center=MerchantRules.Position(map.Fishing,id);reach=Math.Min(reach,FirearmRules.RayBox(motion.X,motion.Z,dx,dz,center.X,center.Z,MerchantRules.BodyHalfSize,reach));}
            if(state.Finale.Phase==FinalePhase.Surfaced)reach=Math.Min(reach,FirearmRules.RayCircle(motion.X,motion.Z,dx,dz,state.Finale.X,state.Finale.Z,map.Fishing.Finale.BodyRadius,reach));
            foreach(var friend in state.Players)
                if(friend.Slot!=local && !friend.WaitingToJoin && !friend.Vitals.Downed && !friend.Revival.Dead)reach=Math.Min(reach,FirearmRules.RayCircle(motion.X,motion.Z,dx,dz,friend.X,friend.Z,EnemyGeometry.PlayerRadius,reach));
            foreach(var fish in state.Enemies.Piranhas)
                if(fish.Owner<0 && fish.RespawnTicks==0 && !fish.Escape.Active)reach=Math.Min(reach,FirearmRules.RayCircle(motion.X,motion.Z,dx,dz,fish.X,fish.Z,250,reach));
            if(state.Enemies.Monster.Phase==MonsterPhase.Windup)reach=Math.Min(reach,FirearmRules.RayCircle(motion.X,motion.Z,dx,dz,state.Enemies.Monster.X,state.Enemies.Monster.Z,EnemyGeometry.MonsterRadius,reach));
            float halfAngle=motion.Firearm.Spread(item.Value.Type)/1000f,heading=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
            var origin=new Vector3(motion.X/1000f,0,motion.Z/1000f);
            for(int n=0;n<17;n++)
            {float angle=(heading+Mathf.Lerp(-halfAngle,halfAngle,n/16f))*Mathf.Deg2Rad;var point=origin+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*(reach/1000f);point.y=SwimmingTerrain.Ground(map,state.Tiles,Mathf.RoundToInt(point.x*1000),Mathf.RoundToInt(point.z*1000))?(map.Platform==null?.77f:PlatformGeometry.DeckHeight+.035f):.075f;spread.SetPosition(n,point);}
            spread.enabled=true;Tint(spread,new Color(.85f,.97f,.84f,.30f));
        }
        private LineRenderer Line(Transform parent,string name,float width)
        {var node=new GameObject(name).transform;node.SetParent(parent,false);var line=node.gameObject.AddComponent<LineRenderer>();line.sharedMaterial=art.Effects;line.useWorldSpace=true;line.startWidth=line.endWidth=width;line.numCapVertices=3;line.enabled=false;line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return line;}
        private static void Tint(LineRenderer line,Color color)=>line.startColor=line.endColor=color;
        public void Reset(){foreach(var shot in shots)Release(shot.Root.gameObject);shots.Clear();waterImpacts.Clear();audio.Stop();spread.enabled=false;PresentedShots=0;}
        public void Dispose(){Reset();waterImpacts.Dispose();Release(root.gameObject);}
        private static void Release(Object value){if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
    }
}
