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
        private readonly List<Object> resources=new List<Object>();
        private readonly Dictionary<int,Avatar> avatars=new Dictionary<int,Avatar>();
        private readonly Transform[] fishes,fishShadows;
        private readonly LineRenderer[] ripples,wakes;
        private readonly Mesh shape,cube,cone;
        private readonly Material skin,boots,white,dark,wood,metal,gold,line;
        private readonly Material[] coats,fishMaterials,rodMaterials,speciesMaterials;
        private readonly int[] shownSpecies,shownGrades;
        private readonly LineRenderer aimRing,aimLine;
        private readonly Collider ground,pier;
        private readonly List<CatchPopup> popups=new List<CatchPopup>();
        private readonly AudioSource audio;
        private readonly AudioClip catchSound,castSound,playerHookSound;
        private readonly CatchEscapePresentation escapes;
        public int RenderedPlayers { get {int count=0;foreach(var avatar in avatars.Values)if(avatar.Root.gameObject.activeSelf)count++;return count;} }
        public int RenderedFishingLines { get {int count=0;foreach(var avatar in avatars.Values)if(avatar.Rope.enabled)count++;return count;} }
        public int PresentedCatches { get; private set; }
        public int PresentedCasts { get; private set; }
        public int PresentedPlayerHooks { get; private set; }
        public IReadOnlyList<CatchPopup> Popups=>popups;
        public sealed class CatchPopup { public Vector3 Position;public int Value;public float Until; }
        private sealed class Avatar
        {
            public Transform Root,Body,Hook,LeftLeg,RightLeg,Rod,RodTip,Backpack;
            public CharacterEquipmentAppearance Equipment;
            public SwimmingAppearance Swimming;
            public MeshRenderer RodShaft;
            public LineRenderer Rope,Ring;
            public int Catches,Casts,Coins,RangeLevel,SpeedLevel,PlayerHooks,TimesHooked;
            public bool Placed;
            public Vector3 Previous;
        }
        public FishingPresentation(Transform parent,Camera camera,Shader lineShader,IslandMap map)
        {
            this.camera=camera;this.map=map;
            root=new GameObject("Fishing visuals").transform;root.SetParent(parent,false);
            ground=GameObject.Find("Island_Ground")?.GetComponent<Collider>();
            pier=GameObject.Find("Pier Walk Surface")?.GetComponent<Collider>()??GameObject.Find("Island_Pier")?.GetComponent<Collider>();
            shape=MakeShape();cube=MakeCube();cone=MakeCone();
            skin=Mat("Skin",new Color(1,.72f,.42f));boots=Mat("Boots",new Color(.16f,.19f,.23f));
            white=Mat("Cream",new Color(1,.96f,.78f));dark=Mat("Eyes",new Color(.035f,.08f,.1f));
            wood=Mat("Leather",new Color(.42f,.2f,.07f));metal=Mat("Hook",new Color(.75f,.88f,.94f));gold=Mat("Gold",new Color(1,.72f,.06f));
            coats=new Material[4];for(int i=0;i<4;i++)coats[i]=Mat("Player "+(i+1),PlayerColors[i]);
            fishMaterials=new[]{Mat("Coral fish",new Color(1,.37f,.07f)),Mat("Aqua fish",new Color(.14f,.88f,.84f)),gold};
            rodMaterials=new[]{wood,coats[2],coats[0],coats[3],gold,metal};
            var fishColors=new[]{new Color(.65f,.78f,.82f),new Color(.2f,.5f,.9f),new Color(.93f,.31f,.22f),new Color(.48f,.55f,.3f),
                new Color(.94f,.72f,.18f),new Color(.1f,.85f,.5f),new Color(.76f,.83f,.98f),new Color(.7f,.26f,.72f),new Color(.98f,.75f,.14f),new Color(.95f,.52f,.68f)};
            speciesMaterials=new Material[fishColors.Length];for(int i=0;i<fishColors.Length;i++)speciesMaterials[i]=Mat("Fish species "+i,fishColors[i]);
            line=new Material(lineShader){name="Fishing lines"};resources.Add(line);
            escapes=new CatchEscapePresentation(root,lineShader);
            fishes=new Transform[map.FishSpots.Count];shownSpecies=new int[fishes.Length];shownGrades=new int[fishes.Length];
            for(int i=0;i<shownSpecies.Length;i++)shownSpecies[i]=-1;
            fishShadows=new Transform[fishes.Length];
            ripples=new LineRenderer[fishes.Length];wakes=new LineRenderer[fishes.Length];
            var shadowMaterial=Mat("Submerged fish silhouette",new Color(.035f,.24f,.29f));
            for(int i=0;i<fishes.Length;i++)
            {
                var fish=new GameObject("Jumping fish "+i).transform;fish.SetParent(root,false);
                var material=fishMaterials[HookGame.Value(i)==30?2:HookGame.Value(i)==20?1:0];
                Part(fish,"Body",shape,Vector3.zero,new Vector3(.42f,.32f,.74f),material);
                Part(fish,"Belly",shape,new Vector3(0,-.1f,.08f),new Vector3(.34f,.19f,.48f),white);
                Part(fish,"Tail",shape,new Vector3(0,0,-.61f),new Vector3(.39f,.08f,.24f),material);
                Part(fish,"Top fin",shape,new Vector3(0,.25f,-.08f),new Vector3(.06f,.27f,.25f),material);
                Part(fish,"Eye L",shape,new Vector3(-.32f,.1f,.32f),Vector3.one*.085f,dark);
                Part(fish,"Eye R",shape,new Vector3(.32f,.1f,.32f),Vector3.one*.085f,dark);
                fishes[i]=fish;fish.gameObject.SetActive(false);
                var shadow=new GameObject("Swimming fish shadow "+i).transform;shadow.SetParent(root,false);
                Part(shadow,"Body",shape,Vector3.zero,new Vector3(.34f,.025f,.72f),shadowMaterial);
                Part(shadow,"Tail",shape,new Vector3(0,0,-.55f),new Vector3(.3f,.025f,.22f),shadowMaterial);
                fishShadows[i]=shadow;shadow.gameObject.SetActive(false);
                wakes[i]=Line("Swimming wake "+i,.035f,new Color(.6f,.95f,.92f,.55f));wakes[i].enabled=false;
                ripples[i]=Line("Fish splash "+i,.045f,Color.white);ripples[i].loop=true;
            }
            aimRing=Line("Aim target",.045f,new Color(1,.96f,.64f));aimRing.loop=true;
            aimLine=Line("Aim guide",.035f,new Color(1,1,.7f,.5f));
            audio=root.gameObject.AddComponent<AudioSource>();audio.spatialBlend=0;audio.volume=.25f;
            catchSound=Tone("Catch chime",880,1320,.2f);castSound=Tone("Hook cast",220,500,.09f);
            playerHookSound=Tone("Teammate hooked",430,150,.19f);
        }
        public Vector3 PlayerPosition(int slot)=>avatars.TryGetValue(slot,out var a)?a.Root.position:Vector3.zero;
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
                    a=CreateAvatar(p.Slot);avatars.Add(p.Slot,a);
                    // A late/restored snapshot contains history; only subsequent changes play feedback.
                    a.Catches=confirmed.Catches;a.Casts=confirmed.Casts;a.Coins=state.Tackle==null?confirmed.Coins:confirmed.CoinsEarned;
                    a.PlayerHooks=confirmed.PlayerHooks;a.TimesHooked=confirmed.TimesHooked;
                    a.RangeLevel=confirmed.RangeLevel;a.SpeedLevel=confirmed.SpeedLevel;
                }
                a.Root.gameObject.SetActive(true);a.Ring.enabled=true;
                Vector3 target=new Vector3(p.X/1000f,p.Swimming.InWater?SwimmingAppearance.FeetInWater:Height(p.X/1000f,p.Z/1000f),p.Z/1000f);
                a.Root.position=a.Placed?Vector3.Lerp(a.Root.position,target,1-Mathf.Exp(-22*dt)):target;
                a.Placed=true;
                bool moving=(state.Tackle==null || !confirmed.Vitals.Downed) && (a.Root.position-a.Previous).sqrMagnitude>.000005f;a.Previous=a.Root.position;
                Vector3 direction=p.Slot==local && showAim?aim-a.Root.position:new Vector3(p.AimX/1000f,0,p.AimZ/1000f)-a.Root.position;
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
                a.LeftLeg.localRotation=Quaternion.Euler(walk,0,0);a.RightLeg.localRotation=Quaternion.Euler(-walk,0,0);
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
                if(confirmed.Casts>a.Casts){PresentedCasts+=confirmed.Casts-a.Casts;if(p.Slot==local)audio.PlayOneShot(castSound);a.Casts=confirmed.Casts;}
                if(confirmed.PlayerHooks>a.PlayerHooks)
                {
                    PresentedPlayerHooks+=confirmed.PlayerHooks-a.PlayerHooks;
                    if(p.Slot==local)audio.PlayOneShot(playerHookSound);
                    a.PlayerHooks=confirmed.PlayerHooks;
                }
                if(confirmed.TimesHooked>a.TimesHooked)
                {if(p.Slot==local)audio.PlayOneShot(playerHookSound);a.TimesHooked=confirmed.TimesHooked;}
                if(confirmed.Catches>a.Catches)
                {
                    PresentedCatches+=confirmed.Catches-a.Catches;int amount=(state.Tackle==null?confirmed.Coins:confirmed.CoinsEarned)-a.Coins;
                    popups.Add(new CatchPopup{Position=a.Root.position+Vector3.up*2,Value=amount,Until=Time.unscaledTime+1.5f});
                    audio.PlayOneShot(catchSound);a.Catches=confirmed.Catches;
                }
                a.Coins=state.Tackle==null?confirmed.Coins:confirmed.CoinsEarned;
                if(state.Tackle!=null)
                {a.Equipment?.Present(state.Tackle.Inventory,confirmed.Slot);a.Swimming?.Present(SwimRingCatalog.Equipped(state.Tackle.Inventory,confirmed.Slot)?.Level??0,confirmed.Swimming.InWater,moving,a.Root.position);}
                if(confirmed.RangeLevel>a.RangeLevel || confirmed.SpeedLevel>a.SpeedLevel)
                {
                    if(p.Slot==local)audio.PlayOneShot(catchSound);
                    a.RangeLevel=confirmed.RangeLevel;a.SpeedLevel=confirmed.SpeedLevel;
                }
            }
            for(int i=0;i<fishes.Length;i++)
            {
                var f=state.Fish[i];bool visible=f.Phase==FishPhase.Jumping || f.Phase==FishPhase.Hooked || f.Escape.Active;
                float weightScale=state.Tackle==null?1:Mathf.Pow(f.WeightGrams/1000f,1f/3)*.65f;
                if(state.Tackle!=null)
                {
                    fishes[i].localScale=Vector3.one*weightScale;
                    if(shownSpecies[i]!=f.Species || shownGrades[i]!=f.Grade)
                    {
                        FishAppearance.Apply(fishes[i],f.Species,f.Grade,speciesMaterials[f.Species],f.Grade==3?gold:metal);
                        shownSpecies[i]=f.Species;shownGrades[i]=f.Grade;
                    }
                }
                var heading=FishMotion.Direction(f,OceanRules.Home(map,state,i));
                var direction=new Vector3(heading.X/1000f,0,heading.Z/1000f).normalized;
                fishes[i].gameObject.SetActive(visible);
                if(visible)
                {
                    fishes[i].position=new Vector3(f.X/1000f,f.Y/1000f+.12f,f.Z/1000f);
                    float vertical=f.Phase==FishPhase.Jumping?4f*1900*(FishMotion.JumpTicks-2*f.Timer)/(FishMotion.JumpTicks*FishMotion.JumpTicks):0;
                    fishes[i].rotation=Quaternion.LookRotation(direction)*Quaternion.Euler(-Mathf.Atan2(vertical,75)*Mathf.Rad2Deg,0,0);
                }
                escapes.Present(i,f.Escape,fishes[i]);
                bool swimming=f.Phase==FishPhase.Swimming || f.Phase==FishPhase.Diving;
                bool shadowVisible=swimming || f.Phase==FishPhase.Jumping;
                fishShadows[i].gameObject.SetActive(shadowVisible);wakes[i].enabled=swimming;
                var surface=new Vector3(f.X/1000f,.07f,f.Z/1000f);
                if(shadowVisible)
                {
                    fishShadows[i].position=surface;fishShadows[i].rotation=Quaternion.LookRotation(direction);
                    float size=f.Phase==FishPhase.Jumping?.85f:Mathf.Lerp(.8f,1,-f.Y/700f);
                    fishShadows[i].localScale=Vector3.one*size*weightScale;
                }
                if(swimming)
                {
                    var side=Vector3.Cross(Vector3.up,direction)*.4f;
                    wakes[i].positionCount=3;wakes[i].SetPosition(0,surface-direction*1.4f+side);
                    wakes[i].SetPosition(1,surface-direction*.7f);wakes[i].SetPosition(2,surface-direction*1.4f-side);
                }
                bool takingOff=f.Phase==FishPhase.Swimming && f.Timer>=FishMotion.SwimTicks-12;
                bool landing=f.Phase==FishPhase.Diving && f.Timer<14;
                bool splash=takingOff || landing || f.Phase==FishPhase.Jumping && f.Timer<8;
                ripples[i].enabled=splash;
                if(splash)
                {
                    float radius=takingOff?.38f+(f.Timer-(FishMotion.SwimTicks-12))*.025f:.4f+f.Timer*.065f;
                    ripples[i].startColor=ripples[i].endColor=takingOff?new Color(1,.91f,.55f,.75f):new Color(.84f,1,1,.7f);
                    var home=OceanRules.Home(map,state,i);
                    var splashPosition=takingOff?surface:new Vector3(home.X/1000f,.09f,home.Z/1000f)+direction*(landing?2.4f:-1.2f);
                    Ring(ripples[i],splashPosition,radius);
                }
            }
            aimLine.enabled=aimRing.enabled=showAim && avatars.ContainsKey(local) && FishingRules.Range(map,state,state.Players[local])>0;
            if(aimRing.enabled)
            {
                var start=PlayerPosition(local);Vector3 direction=aim-start;direction.y=0;
                var end=start+Vector3.ClampMagnitude(direction,FishingRules.Range(map,state,state.Players[local])/1000f);
                end.y=.12f;Ring(aimRing,end,.42f);aimLine.positionCount=2;
                aimLine.SetPosition(0,start+Vector3.up*.16f);aimLine.SetPosition(1,end);
            }
            popups.RemoveAll(p=>p.Until<Time.unscaledTime);
        }
        public float Height(float x,float z)
        {
            if(map.Platform!=null)return PlatformGeometry.DeckHeight+.015f;
            var ray=new Ray(new Vector3(x,8,z),Vector3.down);float y=.68f;
            if(ground!=null && ground.Raycast(ray,out var hit,12))y=hit.point.y;
            if(pier!=null && pier.Raycast(ray,out hit,12))y=Mathf.Max(y,hit.point.y);
            return y+.015f;
        }
        private Avatar CreateAvatar(int slot)
        {
            var a=new Avatar();a.Root=new GameObject("Fisher "+(slot+1)).transform;a.Root.SetParent(root,false);
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
            if(map.Fishing==null)Part(a.Body,"Hook launcher",cube,new Vector3(.35f,1.05f,.56f),new Vector3(.22f,.22f,.52f),metal);
            else
            {
                a.Rod=new GameObject("Equipped fishing rod").transform;a.Rod.SetParent(a.Body,false);
                a.Rod.localPosition=new Vector3(.4f,1,.42f);a.Rod.localRotation=Quaternion.Euler(-24,0,0);
                Part(a.Rod,"Grip",cube,new Vector3(0,0,.05f),new Vector3(.12f,.12f,.4f),wood);
                a.RodShaft=Part(a.Rod,"Shaft",cube,new Vector3(0,0,.95f),new Vector3(.055f,.055f,1.6f),wood).GetComponent<MeshRenderer>();
                Part(a.Rod,"Reel",cone,new Vector3(.05f,-.08f,.15f),Vector3.one*.16f,metal);
                a.RodTip=Part(a.Rod,"Line guide",shape,new Vector3(0,0,1.78f),Vector3.one*.065f,gold);
            }
            if(a.Rod!=null)
            {
                var guard=Part(a.Body,"Equipped cap guard",cube,new Vector3(0,1.8f,.28f),new Vector3(.39f,.19f,.08f),metal);
                var vest=Part(a.Body,"Equipped reinforced vest",cube,new Vector3(0,.88f,.27f),new Vector3(.49f,.49f,.08f),wood);
                var leftShoe=Part(a.LeftLeg,"Equipped light shoe",cube,new Vector3(0,-.18f,.12f),new Vector3(1.08f,.65f,1.1f),white);
                var rightShoe=Part(a.RightLeg,"Equipped light shoe",cube,new Vector3(0,-.18f,.12f),new Vector3(1.08f,.65f,1.1f),white);
                a.Equipment=new CharacterEquipmentAppearance(a.Rod,a.RodShaft,a.Backpack,rodMaterials,
                    new EquipmentSlotVisual(EquipmentSlot.Hat,guard),new EquipmentSlotVisual(EquipmentSlot.Clothes,vest),
                    new EquipmentSlotVisual(EquipmentSlot.Shoes,leftShoe),new EquipmentSlotVisual(EquipmentSlot.Shoes,rightShoe));
                a.Swimming=new SwimmingAppearance(a.Body,a.Root,line.shader);
            }
            Part(a.Body,"Left eye",cube,new Vector3(-.13f,1.45f,.29f),new Vector3(.075f,.09f,.04f),dark);
            Part(a.Body,"Right eye",cube,new Vector3(.13f,1.45f,.29f),new Vector3(.075f,.09f,.04f),dark);
            a.Rope=Line("P"+(slot+1)+" rope",.055f,new Color(1,.76f,.26f));
            a.Hook=new GameObject("P"+(slot+1)+" hook").transform;a.Hook.SetParent(root,false);
            Part(a.Hook,"Tip",shape,Vector3.zero,new Vector3(.22f,.2f,.43f),metal);
            var arc=Line("Hook curve",.075f,new Color(.8f,.96f,1));arc.transform.SetParent(a.Hook,false);arc.useWorldSpace=false;
            arc.positionCount=12;
            for(int i=0;i<12;i++){float t=i/11f*Mathf.PI*1.6f;arc.SetPosition(i,new Vector3(Mathf.Cos(t)*.24f,0,Mathf.Sin(t)*.24f));}
            a.Ring=Line("P"+(slot+1)+" marker",.075f,PlayerColors[slot]);a.Ring.loop=true;
            if(map.Platform!=null){a.Body.localScale=Vector3.one*.5f;a.Hook.localScale=Vector3.one*.65f;}
            return a;
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
        private AudioClip Tone(string name,float start,float end,float duration)
        {
            var samples=new float[(int)(22050*duration)];float phase=0;
            for(int i=0;i<samples.Length;i++){float t=i/(float)samples.Length;phase+=Mathf.Lerp(start,end,t)/22050*Mathf.PI*2;samples[i]=Mathf.Sin(phase)*Mathf.Sin(t*Mathf.PI)*(1-t)*.45f;}
            var clip=AudioClip.Create(name,samples.Length,1,22050,false);clip.SetData(samples,0);resources.Add(clip);return clip;
        }
        public void ClearPlayers()
        {
            foreach(var a in avatars.Values){a.Swimming?.Dispose();Release(a.Root.gameObject);Release(a.Rope.gameObject);Release(a.Ring.gameObject);Release(a.Hook.gameObject);}
            audio.Stop();
            escapes.Clear();
            avatars.Clear();popups.Clear();PresentedCatches=PresentedCasts=PresentedPlayerHooks=0;
            foreach(var f in fishes)f.gameObject.SetActive(false);foreach(var f in fishShadows)f.gameObject.SetActive(false);
            foreach(var r in ripples)r.enabled=false;foreach(var w in wakes)w.enabled=false;
            aimRing.enabled=aimLine.enabled=false;
        }
        public void Dispose(){escapes.Dispose();foreach(var a in avatars.Values)a.Swimming?.Dispose();Release(root.gameObject);foreach(var r in resources)Release(r);}
        private static void Release(Object value){if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);}
    }
}
