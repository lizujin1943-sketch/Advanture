using System;
using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Advanture.HookIsland
{
    /// <summary>Disposable cosmetic objects. No Unity physics or view transform writes back into the simulation.</summary>
    public sealed class FishingPresentation : IDisposable
    {
        public static readonly Color[] PlayerColors={new Color(.12f,.57f,1),new Color(1,.33f,.18f),new Color(.22f,.8f,.36f),new Color(.8f,.35f,1)};
        private readonly Transform root;
        private readonly Camera camera;
        private readonly IslandMap map;
        private readonly FisherCharacterDefinition character;
        private readonly FishVisualCatalog fishArt;
        private readonly List<Object> resources=new List<Object>();
        private readonly Dictionary<int,Avatar> avatars=new Dictionary<int,Avatar>();
        private readonly Transform[] fishes;
        private readonly FishShadowView[] fishShadows;
        private readonly FishWaterEffects fishWater;
        private readonly Mesh shape,cube,cone;
        private readonly Material skin,boots,white,dark,wood,metal,gold,line;
        private readonly Material[] coats,rodMaterials;
        private readonly int[] shownSpecies;
        private readonly FishModelView[] fishViews;
        private readonly LineRenderer aimRing,aimLine;
        // 出钩期间冻结的预瞄落点；只有重新变为 Ready 才重新跟随鼠标。
        private Vector3 lockedAim;
        private bool aimTargetLocked;
        private readonly Collider ground,pier;
        private readonly FishingAudioFeedback audioFeedback;
        private readonly CatchEscapePresentation escapes;
        private readonly FryingPanArt meleeArt;
        public int RenderedPlayers { get {int count=0;foreach(var avatar in avatars.Values)if(avatar.Root.gameObject.activeSelf)count++;return count;} }
        public int RenderedFishingLines { get {int count=0;foreach(var avatar in avatars.Values)if(avatar.Rope.enabled)count++;return count;} }
        public int PresentedCatches=>audioFeedback.PresentedCatches;
        public int PresentedCasts=>audioFeedback.PresentedCasts;
        public int PresentedPlayerHooks=>audioFeedback.PresentedPlayerHooks;
        private sealed class Avatar
        {
            public Transform Root,Body,Head,Hook,LeftLeg,RightLeg,Rod,RodTip,Backpack;
            public CharacterEquipmentAppearance Equipment;
            public SwimmingAppearance Swimming;
            public FisherAnimationView Animation;
            public MeleeAvatarView Melee;
            public Renderer RodShaft,RodTipShaft;
            public LineRenderer Rope,Ring;
            public bool Placed;
            public Vector3 Previous;
        }
        public FishingPresentation(Transform parent,Camera camera,Shader lineShader,IslandMap map)
        {
            this.camera=camera;this.map=map;fishArt=FishVisualCatalog.LoadRequired();
            if(map.Fishing!=null)
            {
                character=Resources.Load<FisherCharacterDefinition>(FisherCharacterDefinition.ResourcePath);
                if(character==null || !character.Complete)
                    throw new InvalidOperationException("Missing animated fisher. Run Tools/Hook Island/Install Animated Fisher.");
            }
            root=new GameObject("Fishing visuals").transform;root.SetParent(parent,false);
            ground=GameObject.Find("Island_Ground")?.GetComponent<Collider>();
            pier=GameObject.Find("Pier Walk Surface")?.GetComponent<Collider>()??GameObject.Find("Island_Pier")?.GetComponent<Collider>();
            shape=MakeShape();cube=MakeCube();cone=MakeCone();
            skin=Mat("Skin",new Color(1,.72f,.42f));boots=Mat("Boots",new Color(.16f,.19f,.23f));
            white=Mat("Cream",new Color(1,.96f,.78f));dark=Mat("Eyes",new Color(.035f,.08f,.1f));
            wood=Mat("Leather",new Color(.42f,.2f,.07f));metal=Mat("Hook",new Color(.75f,.88f,.94f));gold=Mat("Gold",new Color(1,.72f,.06f));
            coats=new Material[4];for(int i=0;i<4;i++)coats[i]=Mat("Player "+(i+1),PlayerColors[i]);
            rodMaterials=new[]{wood,coats[2],coats[0],coats[3],gold,metal};
            line=new Material(lineShader){name="Fishing lines"};resources.Add(line);
            escapes=new CatchEscapePresentation(root,lineShader);
            fishes=new Transform[map.FishSpots.Count];shownSpecies=new int[fishes.Length];fishViews=new FishModelView[fishes.Length];
            for(int i=0;i<shownSpecies.Length;i++)shownSpecies[i]=-1;
            fishShadows=new FishShadowView[fishes.Length];
            fishWater=new FishWaterEffects(root,map,fishArt);
            var shadowQuad=FishShadowView.CreateSharedQuad();resources.Add(shadowQuad);
            var shadowMaterial=new Material(fishArt.shadowShader){name="Soft submerged fish",color=fishArt.shadowTint};resources.Add(shadowMaterial);
            for(int i=0;i<fishes.Length;i++)
            {
                var fish=new GameObject("Jumping fish "+i).transform;fish.SetParent(root,false);
                fishes[i]=fish;fish.gameObject.SetActive(false);
                fishShadows[i]=new FishShadowView(root,i,shadowQuad,shadowMaterial);
            }
            aimRing=Line("Aim target",.045f,new Color(1,.96f,.64f));aimRing.loop=true;
            aimLine=Line("Aim guide",.035f,new Color(1,1,.7f,.5f));
            audioFeedback=new FishingAudioFeedback(root);
            if(map.Fishing?.Melee.Enabled==true)meleeArt=new FryingPanArt(lineShader);
        }
        public Vector3 PlayerPosition(int slot)=>avatars.TryGetValue(slot,out var a)?a.Root.position:Vector3.zero;
        public bool TryGetPlayerHudAnchors(int slot,out Vector3 head,out Vector3 chest,out float halfWidth)
        {
            head=chest=Vector3.zero;halfWidth=0;
            if(!avatars.TryGetValue(slot,out var a) || !a.Root.gameObject.activeSelf)return false;
            // Follow the displayed pose, including swimming, knockback and lying down.
            head=a.Head!=null?a.Head.position+Vector3.up*.28f:a.Body.TransformPoint(Vector3.up*(a.Animation!=null?1.12f:2.1f));
            chest=a.Body.TransformPoint(Vector3.up*(a.Animation!=null?.55f:1.0f));
            halfWidth=a.Animation!=null?.35f:.4f*a.Body.lossyScale.x;
            return true;
        }
        public void Present(HookState state,HookState predicted,ushort local,Vector3 aim,bool showAim)
        {
            float dt=Mathf.Min(Time.unscaledDeltaTime,.1f);
            foreach(var confirmed in state.Players)
            {
                if(confirmed.WaitingToJoin)
                {
                    if(avatars.TryGetValue(confirmed.Slot,out var hidden))
                    {hidden.Root.gameObject.SetActive(false);hidden.Rope.enabled=hidden.Ring.enabled=false;hidden.Hook.gameObject.SetActive(false);}
                    continue;
                }
                var p=confirmed;
                if(!p.Unavailable && p.Slot==local && p.PulledBy<0 && p.HookedPlayer<0)
                    foreach(var future in predicted.Players)if(future.Slot==local){p=future;break;}
                if(!avatars.TryGetValue(p.Slot,out var a))
                {
                    a=CreateAvatar(p.Slot,confirmed.Casts);avatars.Add(p.Slot,a);
                    // A late/restored snapshot contains history; only subsequent changes play feedback.

                }
                a.Root.gameObject.SetActive(true);a.Ring.enabled=true;
                Vector3 target=new Vector3(p.X/1000f,p.Swimming.InWater?SwimmingAppearance.FeetInWater:Height(p.X/1000f,p.Z/1000f),p.Z/1000f);
                a.Root.position=a.Placed?Vector3.Lerp(a.Root.position,target,1-Mathf.Exp(-22*dt)):target;
                a.Placed=true;
                bool moving=(state.Tackle==null || !confirmed.Vitals.Downed) && (a.Root.position-a.Previous).sqrMagnitude>.000005f;a.Previous=a.Root.position;
                Vector3 direction=p.Slot==local && showAim?aim-a.Root.position:new Vector3(p.AimX/1000f,0,p.AimZ/1000f)-a.Root.position;
                // Aim can change while a hook is flying. The body casts along the accepted
                // launch direction instead of turning after the mouse during the swing.
                if(p.Phase!=HookPhase.Ready)direction=new Vector3(p.DirectionX,0,p.DirectionZ);
                if(p.Melee.Active)direction=new Vector3(p.Melee.DirectionX,0,p.Melee.DirectionZ);
                if(confirmed.PulledBy>=0)
                {
                    var owner=state.Players[confirmed.PulledBy];
                    direction=new Vector3(owner.X/1000f,0,owner.Z/1000f)-a.Root.position;
                }
                direction.y=0;if(direction.sqrMagnitude>.01f)
                    a.Body.rotation=Quaternion.Slerp(a.Body.rotation,Quaternion.LookRotation(direction)*Quaternion.Euler(confirmed.PulledBy>=0?-18:confirmed.Swimming.InWater && moving?18:0,0,state.Tackle!=null && confirmed.Vitals.Downed?80:0),1-Mathf.Exp(-20*dt));
                float walk=moving?Mathf.Sin(Time.time*(confirmed.PulledBy>=0?24:15))*(confirmed.PulledBy>=0?42:25):0;
                a.Ring.startColor=a.Ring.endColor=confirmed.PulledBy>=0?new Color(1,.7f,.12f):
                    confirmed.Shopping || confirmed.ProtectionTicks>0?new Color(.55f,1,1):PlayerColors[p.Slot];
                if(a.Animation==null)
                {a.LeftLeg.localRotation=Quaternion.Euler(walk,0,0);a.RightLeg.localRotation=Quaternion.Euler(-walk,0,0);}
                else
                {
                    bool hasRing=state.Tackle!=null && SwimRingCatalog.Equipped(state.Tackle.Inventory,confirmed.Slot)!=null;
                    bool interrupted=state.RoundFinished || confirmed.Unavailable || confirmed.Vitals.Downed ||
                        confirmed.PulledBy>=0 || confirmed.Knockback.Active || confirmed.EquippedRod==0 && MeleeRules.Equipped(state,confirmed)==null ||
                        confirmed.Swimming.InWater && !hasRing;
                    bool reeling=p.Phase==HookPhase.Returning || p.FishId>=0 || p.DriftwoodId>=0 ||
                        p.HookedPlayer>=0 || p.HookedPiranha>0 || p.HookedChest>0 || p.HookedFinale;
                    // Local motion/casts use prediction for immediate feedback. Peers use
                    // confirmed observations. Hook ownership and all rewards below stay confirmed.
                    a.Animation.Present(dt,p.Casts,moving,reeling,interrupted);
                }
                Ring(a.Ring,new Vector3(a.Root.position.x,p.Swimming.InWater?.075f:a.Root.position.y+.045f,a.Root.position.z),(p.Slot==local?.65f:.48f)*(map.Platform==null?1:.55f));
                // Hook and ownership use confirmed state; speculative catches never play a reward.
                bool active=confirmed.Phase!=HookPhase.Ready;
                a.Rope.enabled=active;a.Hook.gameObject.SetActive(active);
                if(active)
                {
                    float hand=map.Platform==null?1.05f:.55f;
                    Vector3 from=a.Root.position+Vector3.up*hand+a.Body.forward*(map.Platform==null?.4f:.2f);
                    if(state.Tackle!=null && a.RodTip!=null)from=a.RodTip.position;
                    Vector3 to=new Vector3(confirmed.HookX/1000f,map.Platform==null?1.25f:.75f,confirmed.HookZ/1000f);
                    if(confirmed.DriftwoodId>=0)to.y=.65f;
                    if(confirmed.FishId>=0)to.y=state.Fish[confirmed.FishId].Y/1000f;
                    if(confirmed.HookedPlayer>=0)
                    {
                        var friend=state.Players[confirmed.HookedPlayer];
                        to=avatars.ContainsKey(friend.Slot)?PlayerPosition(friend.Slot)+Vector3.up*hand:
                            new Vector3(friend.X/1000f,Height(friend.X/1000f,friend.Z/1000f)+hand,friend.Z/1000f);
                    }
                    a.Rope.startColor=a.Rope.endColor=confirmed.HookedPlayer>=0?new Color(1,.56f,.15f):new Color(1,.76f,.26f);
                    a.Rope.startWidth=a.Rope.endWidth=confirmed.HookedPlayer>=0?.085f:.055f;
                    a.Hook.position=to;a.Hook.rotation=Quaternion.LookRotation((to-from).sqrMagnitude>.001f?to-from:Vector3.forward);
                    a.Rope.positionCount=12;
                    for(int j=0;j<12;j++){float t=j/11f;var point=Vector3.Lerp(from,to,t);point.y+=Mathf.Sin(t*Mathf.PI)*.3f;a.Rope.SetPosition(j,point);}
                }
                if(state.Tackle!=null)
                {
                    a.Equipment?.Present(state.Tackle.Inventory,confirmed.Slot);
                    a.Melee?.Present(confirmed,p,MeleeRules.Equipped(state,confirmed),state.RoundFinished || confirmed.Unavailable || confirmed.Vitals.Downed || confirmed.PulledBy>=0 || confirmed.Knockback.Active);
                    if(a.RodTipShaft!=null)a.RodTipShaft.sharedMaterial=a.RodShaft.sharedMaterial;
                    a.Swimming?.Present(SwimRingCatalog.Equipped(state.Tackle.Inventory,confirmed.Slot)?.Level??0,confirmed.Swimming.InWater,moving,a.Root.position);
                }

            }
            for(int i=0;i<fishes.Length;i++)
            {
                var f=state.Fish[i];bool visible=f.Phase==FishPhase.Jumping || f.Phase==FishPhase.Hooked || f.Escape.Active;
                // The shared art multiplier improves readability; confirmed weight still
                // controls relative size. Neither value changes capture geometry.
                float weightScale=map.Fishing==null?1:Mathf.Pow(Mathf.Max(1000,f.WeightGrams)/1000f,1f/3);
                int species=map.Fishing==null?i%fishArt.species.Length:f.Species;
                if(shownSpecies[i]!=species)
                {
                    fishViews[i]?.Dispose();
                    fishViews[i]=new FishModelView(fishes[i],fishArt.species[species]);
                    shownSpecies[i]=species;
                }
                float displayScale=fishArt.displayScale*weightScale;
                fishes[i].localScale=Vector3.one*displayScale;
                var heading=FishMotion.Direction(f,OceanRules.Home(map,state,i));
                var direction=new Vector3(heading.X/1000f,0,heading.Z/1000f).normalized;
                if(f.Phase==FishPhase.Hooked && f.Owner>=0 && f.Owner<state.Players.Count)
                {
                    // Ownership and fish position are confirmed; replaying a snapshot must keep the same facing.
                    var owner=state.Players[f.Owner];
                    var towardOwner=new Vector3(owner.X-f.X,0,owner.Z-f.Z);
                    if(towardOwner.sqrMagnitude>.000001f)direction=towardOwner.normalized;
                }
                fishes[i].gameObject.SetActive(visible);
                if(visible)FishAppearance.Present(fishViews[i],f,state.Steps);
                if(visible)
                {
                    fishes[i].position=new Vector3(f.X/1000f,f.Y/1000f+.12f,f.Z/1000f);
                    float vertical=f.Phase==FishPhase.Jumping?4f*1900*(FishMotion.JumpTicks-2*f.Timer)/(FishMotion.JumpTicks*FishMotion.JumpTicks):0;
                    fishes[i].rotation=Quaternion.LookRotation(direction)*Quaternion.Euler(-Mathf.Atan2(vertical,75)*Mathf.Rad2Deg,0,0);
                }
                escapes.Present(i,f.Escape,fishes[i],showWater:false);
                bool swimming=f.Phase==FishPhase.Swimming || f.Phase==FishPhase.Diving;
                bool shadowVisible=swimming || f.Phase==FishPhase.Jumping;
                var surface=new Vector3(f.X/1000f,.07f,f.Z/1000f);
                if(shadowVisible)
                {
                    float opacity=f.Phase==FishPhase.Jumping?Mathf.Lerp(.55f,.15f,Mathf.Clamp01(f.Y/1900f)):
                        Mathf.Lerp(.9f,.6f,Mathf.Clamp01(-f.Y/700f));
                    if(f.Phase==FishPhase.Swimming)opacity*=Mathf.SmoothStep(0,1,f.Timer/10f);
                    if(f.Phase==FishPhase.Diving)opacity*=1-Mathf.SmoothStep(0,1,f.Timer/(float)FishMotion.DiveTicks);
                    fishShadows[i].Present(fishArt.species[species],displayScale,surface,direction,opacity,
                        (state.Steps%30000)/(float)HookGame.Rate+i*.137f);
                }
                else fishShadows[i].Hide();
            }
            fishWater.Present(state);
            // 预瞄落点只在可再次出钩时跟随鼠标。钩子一离开就冻结目标点，
            // 否则出钩后移动鼠标会让“刚才瞄准的位置”看起来变了，玩家无法判断钩子飞向哪里。
            // 线的起点仍然跟着角色走，被拉拽时不会与角色脱节。
            // 自己能否出钩必须读预测态：确认态落后约一秒，用它会让线在钩子已飞出后继续乱动。
            bool aiming=showAim && local<state.Players.Count && local<predicted.Players.Count && avatars.ContainsKey(local);
            if(aiming)
            {
                // 射程读确认态：预测态可能已换上尚未确认的鱼竿，此时按 id 查背包会失败。
                var shooter=predicted.Players[local];
                var range=FishingRules.Range(map,state,state.Players[local]);
                aiming=range>0;
                if(aiming)
                {
                    var start=PlayerPosition(local);
                    float reach=range/1000f;
                    bool canAim=shooter.Phase==HookPhase.Ready && shooter.Cooldown==0;
                    if(canAim)
                    {
                        Vector3 direction=aim-start;direction.y=0;
                        lockedAim=start+Vector3.ClampMagnitude(direction,reach);
                        aimTargetLocked=true;
                    }
                    else if(!aimTargetLocked)
                    {
                        // 重新显示（例如刚关掉面板）时不能偷看当前鼠标：出钩后
                        // 鼠标早已移开，只有已提交的瞄准点才代表玩家当时的选择。
                        lockedAim=new Vector3(shooter.AimX/1000f,.12f,shooter.AimZ/1000f);
                        if((lockedAim-start).sqrMagnitude<1e-4f)
                        {
                            Vector3 direction=aim-start;direction.y=0;
                            lockedAim=start+Vector3.ClampMagnitude(direction,reach);
                        }
                        aimTargetLocked=true;
                    }
                    // 冻结的是出钩瞬间的落点；角色被拉走后按当前手长收拢，线段不会超过射程。
                    Vector3 offset=lockedAim-start;offset.y=0;
                    var end=canAim?lockedAim:start+Vector3.ClampMagnitude(offset,reach);
                    end.y=.12f;Ring(aimRing,end,.42f);aimLine.positionCount=2;
                    aimLine.SetPosition(0,start+Vector3.up*.16f);aimLine.SetPosition(1,end);
                }
            }
            else aimTargetLocked=false;
            aimLine.enabled=aimRing.enabled=aiming;
        }
        public float Height(float x,float z)
        {
            if(map.Platform!=null)return PlatformGeometry.DeckHeight+.015f;
            var ray=new Ray(new Vector3(x,8,z),Vector3.down);float y=.68f;
            if(ground!=null && ground.Raycast(ray,out var hit,12))y=hit.point.y;
            if(pier!=null && pier.Raycast(ray,out hit,12))y=Mathf.Max(y,hit.point.y);
            return y+.015f;
        }
        private Avatar CreateAvatar(int slot,int initialCasts)
        {
            var a=new Avatar();a.Root=new GameObject("Fisher "+(slot+1)).transform;a.Root.SetParent(root,false);
            if(character!=null)CreateAnimatedBody(a,slot,initialCasts);
            else CreateLegacyBody(a,slot);
            a.Rope=Line("P"+(slot+1)+" rope",.055f,new Color(1,.76f,.26f));
            a.Hook=new GameObject("P"+(slot+1)+" hook").transform;a.Hook.SetParent(root,false);
            Part(a.Hook,"Tip",shape,Vector3.zero,new Vector3(.22f,.2f,.43f),metal);
            var arc=Line("Hook curve",.075f,new Color(.8f,.96f,1));arc.transform.SetParent(a.Hook,false);arc.useWorldSpace=false;
            arc.positionCount=12;
            for(int i=0;i<12;i++){float t=i/11f*Mathf.PI*1.6f;arc.SetPosition(i,new Vector3(Mathf.Cos(t)*.24f,0,Mathf.Sin(t)*.24f));}
            a.Ring=Line("P"+(slot+1)+" marker",.075f,PlayerColors[slot]);a.Ring.loop=true;
            if(map.Platform!=null)a.Hook.localScale=Vector3.one*.65f;
            return a;
        }
        private void CreateAnimatedBody(Avatar a,int slot,int initialCasts)
        {
            a.Body=Object.Instantiate(character.model,a.Root,false).transform;a.Body.name="Body";
            foreach(var bone in a.Body.GetComponentsInChildren<Transform>(true))if(bone.name=="Head"){a.Head=bone;break;}
            foreach(var renderer in a.Body.GetComponentsInChildren<Renderer>(true))
            {
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)if(materials[i].name=="Fisher_Coat")materials[i]=coats[slot];
                renderer.sharedMaterials=materials;
            }
            a.Animation=new FisherAnimationView(a.Body.GetComponent<Animator>(),character,initialCasts);
            a.Rod=a.Body.Find("Equipped fishing rod");a.RodTip=a.Animation.LineGuide;
            a.RodShaft=a.Rod.Find("Rod Shaft").GetComponent<Renderer>();
            a.RodTipShaft=a.Rod.Find("Rod Tip Shaft").GetComponent<Renderer>();
            a.Backpack=a.Body.Find("Backpack");
            a.Equipment=new CharacterEquipmentAppearance(a.Rod,a.RodShaft,a.Backpack,rodMaterials,
                new EquipmentSlotVisual(EquipmentSlot.Hat,a.Body.Find("Equipped cap guard")),
                new EquipmentSlotVisual(EquipmentSlot.Clothes,a.Body.Find("Equipped reinforced vest")),
                new EquipmentSlotVisual(EquipmentSlot.Shoes,a.Body.Find("Equipped light shoe L")),
                new EquipmentSlotVisual(EquipmentSlot.Shoes,a.Body.Find("Equipped light shoe R")));
            // The previous equipment mesh was authored at twice the one-metre character scale.
            var ringMount=new GameObject("Swim ring mount").transform;ringMount.SetParent(a.Body,false);ringMount.localScale=Vector3.one*.5f;
            a.Swimming=new SwimmingAppearance(ringMount,a.Root,line.shader);
            if(meleeArt!=null)a.Melee=new MeleeAvatarView(a.Body,meleeArt);
        }
        private void CreateLegacyBody(Avatar a,int slot)
        {
            a.Body=new GameObject("Body").transform;a.Body.SetParent(a.Root,false);
            a.LeftLeg=Part(a.Body,"Left boot",cube,new Vector3(-.19f,.25f,0),new Vector3(.25f,.5f,.34f),boots);
            a.RightLeg=Part(a.Body,"Right boot",cube,new Vector3(.19f,.25f,0),new Vector3(.25f,.5f,.34f),boots);
            Part(a.Body,"Jacket",cube,new Vector3(0,.84f,0),new Vector3(.71f,.74f,.48f),coats[slot]);
            Part(a.Body,"Belt",cube,new Vector3(0,.6f,.02f),new Vector3(.73f,.12f,.51f),wood);
            Part(a.Body,"Face",shape,new Vector3(0,1.42f,.015f),new Vector3(.4f,.4f,.35f),skin);
            Part(a.Body,"Hat brim",cone,new Vector3(0,1.68f,0),new Vector3(.64f,.09f,.64f),coats[slot]);
            Part(a.Body,"Hat crown",cone,new Vector3(0,1.72f,0),new Vector3(.44f,.38f,.44f),coats[slot]);
            a.Backpack=Part(a.Body,"Backpack",cube,new Vector3(0,.88f,-.37f),new Vector3(.48f,.51f,.26f),wood);
            Part(a.Body,"Left hand",shape,new Vector3(-.45f,.9f,.25f),Vector3.one*.17f,skin);
            Part(a.Body,"Right hand",shape,new Vector3(.4f,1.0f,.42f),Vector3.one*.17f,skin);
            Part(a.Body,"Hook launcher",cube,new Vector3(.35f,1.05f,.56f),new Vector3(.22f,.22f,.52f),metal);
            Part(a.Body,"Left eye",cube,new Vector3(-.13f,1.45f,.29f),new Vector3(.075f,.09f,.04f),dark);
            Part(a.Body,"Right eye",cube,new Vector3(.13f,1.45f,.29f),new Vector3(.075f,.09f,.04f),dark);
            if(map.Platform!=null)a.Body.localScale=Vector3.one*.5f;
        }
        private Material Mat(string name,Color color)
        {
            var m=new Material(Shader.Find("Hook Island/Baked Color Surface")){name=name,color=color};
            m.SetFloat("_Ambient",.4f);m.SetFloat("_SunStrength",.85f);resources.Add(m);return m;
        }
        private Transform Part(Transform parent,string name,Mesh mesh,Vector3 position,Vector3 scale,Material material)
        {
            var g=new GameObject(name);g.transform.SetParent(parent,false);g.transform.localPosition=position;g.transform.localScale=scale;
            g.AddComponent<MeshFilter>().sharedMesh=mesh;g.AddComponent<MeshRenderer>().sharedMaterial=material;return g.transform;
        }
        private LineRenderer Line(string name,float width,Color color)
        {
            var g=new GameObject(name);g.transform.SetParent(root,false);var l=g.AddComponent<LineRenderer>();l.sharedMaterial=line;
            l.startWidth=l.endWidth=width;l.startColor=l.endColor=color;l.numCapVertices=2;l.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return l;
        }
        private static void Ring(LineRenderer line,Vector3 position,float radius)
        {line.positionCount=32;for(int i=0;i<32;i++){float a=i*Mathf.PI*2/32;line.SetPosition(i,position+new Vector3(Mathf.Cos(a)*radius,0,Mathf.Sin(a)*radius));}}
        private Mesh MeshFrom(string name,Vector3[] vertices,int[] triangles)
        {
            // Separate vertices per triangle to keep the low-poly shading.
            var flat=new Vector3[triangles.Length];var indices=new int[triangles.Length];
            for(int i=0;i<flat.Length;i++){flat[i]=vertices[triangles[i]];indices[i]=i;}
            var mesh=new Mesh{name=name};mesh.vertices=flat;mesh.triangles=indices;mesh.RecalculateNormals();mesh.RecalculateBounds();resources.Add(mesh);return mesh;
        }
        private Mesh MakeShape()=>MeshFrom("Low poly octahedron",new[]{Vector3.up,Vector3.down,Vector3.left,Vector3.right,Vector3.forward,Vector3.back},
            new[]{0,4,3,0,2,4,0,5,2,0,3,5,1,3,4,1,4,2,1,2,5,1,5,3});
        private Mesh MakeCube()=>MeshFrom("Cube",new[]{new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(-.5f,.5f,-.5f),
            new Vector3(-.5f,-.5f,.5f),new Vector3(.5f,-.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,.5f,.5f)},
            new[]{0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,1,2,6,1,6,5,0,4,7,0,7,3});
        private Mesh MakeCone()
        {
            var v=new Vector3[10];v[0]=Vector3.up;v[9]=Vector3.zero;var t=new int[48];
            for(int i=0;i<8;i++){float a=i*Mathf.PI/4;v[i+1]=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));int j=(i+1)%8+1;t[i*6]=0;t[i*6+1]=j;t[i*6+2]=i+1;t[i*6+3]=9;t[i*6+4]=i+1;t[i*6+5]=j;}
            return MeshFrom("Eight sided cone",v,t);
        }
        public void PresentConfirmedEvent(HookGameplayEvent value,ushort local)
        {
            audioFeedback.Apply(value,local,map.Fishing?.Combo,meleeArt);
            if(value.Kind==HookGameplayEventKind.MeleeHit && avatars.TryGetValue(value.Slot,out var avatar))avatar.Melee?.ConfirmHit();
        }
        public void ClearPlayers()
        {
            foreach(var a in avatars.Values){a.Animation?.Dispose();a.Swimming?.Dispose();Release(a.Root.gameObject);Release(a.Rope.gameObject);Release(a.Ring.gameObject);Release(a.Hook.gameObject);}
            audioFeedback.Reset();
            escapes.Clear();
            avatars.Clear();
            foreach(var f in fishes)f.gameObject.SetActive(false);foreach(var f in fishShadows)f.Hide();
            fishWater.Clear();
            aimRing.enabled=aimLine.enabled=false;
            aimTargetLocked=false;
        }
        public void Dispose(){audioFeedback.Dispose();meleeArt?.Dispose();fishWater.Dispose();escapes.Dispose();foreach(var f in fishViews)f?.Dispose();foreach(var a in avatars.Values){a.Animation?.Dispose();a.Swimming?.Dispose();}Release(root.gameObject);foreach(var r in resources)Release(r);}
        private static void Release(Object value){if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
    }
}
