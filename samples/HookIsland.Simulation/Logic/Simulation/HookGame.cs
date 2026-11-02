using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrameSync.Abstractions;

namespace Advanture.HookIsland.Simulation;

public sealed partial class HookGame : IStateCodec<HookState>, ISimulationFactory<HookState>, IInputValidator
{
    public const int MaximumStateBytes=524288;
    public const int Rate=30, Range=8000, JumpTicks=FishMotion.JumpTicks;
    public const int PullStep=400, PullStopDistance=1000, PullLimitTicks=60, ProtectionDuration=90;
    private readonly bool initialFriendlyPranks;
    public HookGame(bool initialFriendlyPranks=true){this.initialFriendlyPranks=initialFriendlyPranks;}
    public static SimulationModule<HookState> CreateModule(bool initialFriendlyPranks=true)
    {
        var game=new HookGame(initialFriendlyPranks);
        return new SimulationModule<HookState>(new CompatibilityManifest("hook-island",rulesVersion:"30",inputFormatVersion:"12",
            stateFormatVersion:"27",contentHash:CatalogData.Hash(),determinismHash:"integer-mm-food-quickbar-v30"),game,game,game);
    }
    public IDeterministicSimulation<HookState> Create()=>new Model(initialFriendlyPranks);
    public void ValidateCanonical(ReadOnlySpan<byte> bytes)=>HookInput.Decode(bytes);
    public byte[] Encode(HookState s)
    {
        using var stream=new MemoryStream();using var w=new BinaryWriter(stream);
        w.Write(0x48534948);w.Write(s.Steps);w.Write(s.Coins);w.Write(s.Players.Count);
        foreach(var p in s.Players)
        {
            w.Write(p.Slot);w.Write(p.X);w.Write(p.Z);w.Write(p.AimX);w.Write(p.AimZ);w.Write(p.RemainderX);w.Write(p.RemainderZ);
            w.Write((byte)p.Phase);w.Write(p.HookX);w.Write(p.HookZ);w.Write(p.DirectionX);w.Write(p.DirectionZ);w.Write(p.Distance);
            w.Write(p.FishId);w.Write(p.Cooldown);w.Write(p.Catches);w.Write(p.Coins);w.Write(p.Casts);
            w.Write(p.RangeLevel);w.Write(p.SpeedLevel);w.Write(p.LastPurchaseId);w.Write((byte)p.LastPurchaseResult);
            w.Write(p.HookedPlayer);w.Write(p.PulledBy);w.Write(p.PullTicks);w.Write(p.ProtectionTicks);
            w.Write(p.PlayerHooks);w.Write(p.TimesHooked);w.Write(p.Shopping?(byte)1:(byte)0);
            w.Write(p.Wood);w.Write(p.Fishbones);w.Write(p.WoodEarned);w.Write(p.FishbonesEarned);w.Write(p.DriftwoodId);
            w.Write(p.LastBuildId);w.Write((byte)p.LastBuildResult);
        }
        w.Write(s.Fish.Count);
        foreach(var f in s.Fish){w.Write(f.Id);w.Write(f.X);w.Write(f.Z);w.Write(f.Y);w.Write(f.Timer);w.Write(f.Owner);w.Write((byte)f.Phase);w.Write(f.Heading);}
        PlatformStateCodec.Write(w,s);
        FishingStateCodec.Write(w,s);
        EnemyStateCodec.Write(w,s);
        CatchEscapeStateCodec.Write(w,s);
        CooperativeFishingStateCodec.Write(w,s);
        RoomRulesStateCodec.Write(w,s);
        TreasureStateCodec.Write(w,s);
        CommissionStateCodec.Write(w,s);
        OceanStateCodec.Write(w,s);
        FinaleStateCodec.Write(w,s);
        PresenceStateCodec.Write(w,s);
        ComboStateCodec.Write(w,s);
        MeleeStateCodec.Write(w,s);
        return stream.ToArray();
    }
    public HookState Decode(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length<24 || bytes.Length>MaximumStateBytes)throw new InvalidDataException("Invalid state length.");
        using var r=new BinaryReader(new MemoryStream(bytes.ToArray(),false));
        if(r.ReadInt32()!=0x48534948)throw new InvalidDataException("State version mismatch.");
        ulong tick=r.ReadUInt64();int coins=r.ReadInt32(),count=r.ReadInt32();
        if(count<1 || count>4 || coins<0)throw new InvalidDataException("Invalid players/wallet.");
        var players=new Fisher[count];
        for(int i=0;i<count;i++)
        {
            var p=new Fisher { Slot=r.ReadUInt16(),X=r.ReadInt32(),Z=r.ReadInt32(),AimX=r.ReadInt32(),AimZ=r.ReadInt32(),
                RemainderX=r.ReadInt32(),RemainderZ=r.ReadInt32(),Phase=(HookPhase)r.ReadByte(),HookX=r.ReadInt32(),HookZ=r.ReadInt32(),
                DirectionX=r.ReadInt32(),DirectionZ=r.ReadInt32(),Distance=r.ReadInt32(),FishId=r.ReadInt32(),Cooldown=r.ReadInt32(),
                Catches=r.ReadInt32(),Coins=r.ReadInt32(),Casts=r.ReadInt32(),RangeLevel=r.ReadInt32(),SpeedLevel=r.ReadInt32(),
                LastPurchaseId=r.ReadUInt32(),LastPurchaseResult=(PurchaseResult)r.ReadByte(),
                HookedPlayer=r.ReadInt32(),PulledBy=r.ReadInt32(),PullTicks=r.ReadInt32(),ProtectionTicks=r.ReadInt32(),
                PlayerHooks=r.ReadInt32(),TimesHooked=r.ReadInt32() };
            byte shopping=r.ReadByte();p.Shopping=shopping==1;
            p.Wood=r.ReadInt32();p.Fishbones=r.ReadInt32();p.WoodEarned=r.ReadInt32();p.FishbonesEarned=r.ReadInt32();p.DriftwoodId=r.ReadInt32();
            p.LastBuildId=r.ReadUInt32();p.LastBuildResult=(BuildResult)r.ReadByte();
            if(p.Slot!=i || p.Phase>HookPhase.Returning || p.FishId< -1 || p.FishId>31 || p.Cooldown<0 || p.Cooldown>12 ||
                p.Catches<0 || p.Coins<0 || p.Casts<0 || p.PlayerHooks<0 || p.TimesHooked<0 || shopping>1 ||
                p.HookedPlayer< -1 || p.HookedPlayer>=count || p.HookedPlayer==i ||
                p.PulledBy< -1 || p.PulledBy>=count || p.PulledBy==i ||
                p.PullTicks<0 || p.PullTicks>PullLimitTicks || p.ProtectionTicks<0 || p.ProtectionTicks>ProtectionDuration ||
                (p.HookedPlayer<0 && p.PullTicks!=0) ||
                (p.PulledBy>=0 && (p.HookedPlayer>=0 || p.Shopping || p.ProtectionTicks>0)) ||
                Math.Abs((long)p.X)>50000 || Math.Abs((long)p.Z)>50000 ||
                Math.Abs((long)p.HookX)>100000 || Math.Abs((long)p.HookZ)>100000 ||
                Math.Abs((long)p.DirectionX)>1000 || Math.Abs((long)p.DirectionZ)>1000 || p.Distance<0 ||
                p.RangeLevel<0 || p.RangeLevel>HookUpgrades.MaxLevel || p.SpeedLevel<0 || p.SpeedLevel>HookUpgrades.MaxLevel ||
                p.LastPurchaseResult>PurchaseResult.HookBusy || (p.LastPurchaseId==0)!=(p.LastPurchaseResult==PurchaseResult.None) ||
                Math.Abs((long)p.AimX)>100000 || Math.Abs((long)p.AimZ)>100000 ||
                Math.Abs((long)p.RemainderX)>=1000 || Math.Abs((long)p.RemainderZ)>=1000)
                throw new InvalidDataException("Invalid fisher.");
            players[i]=p;
        }
        int fishCount=r.ReadInt32();if(fishCount<1 || fishCount>32)throw new InvalidDataException("Invalid fish count.");
        var fish=new Fish[fishCount];
        for(int i=0;i<fishCount;i++)
        {
            var f=new Fish { Id=r.ReadInt32(),X=r.ReadInt32(),Z=r.ReadInt32(),Y=r.ReadInt32(),Timer=r.ReadInt32(),Owner=r.ReadInt32(),Phase=(FishPhase)r.ReadByte(),Heading=r.ReadInt32() };
            if(f.Id!=i || f.Phase>FishPhase.Escaping || f.Owner< -1 || f.Owner>=count || f.Timer<0 || f.Timer>300 ||
                (f.Heading!=1 && f.Heading!=-1) ||
                (f.Phase==FishPhase.Swimming && f.Timer>=FishMotion.SwimTicks) ||
                (f.Phase==FishPhase.Jumping && f.Timer>=FishMotion.JumpTicks) ||
                (f.Phase==FishPhase.Diving && f.Timer>=FishMotion.DiveTicks) ||
                Math.Abs((long)f.X)>100000 || Math.Abs((long)f.Z)>100000 || f.Y< -700 || f.Y>5000)
                throw new InvalidDataException("Invalid fish.");
            fish[i]=f;
        }
        var state=CooperativeFishingStateCodec.Read(r,CatchEscapeStateCodec.Read(r,EnemyStateCodec.Read(r,FishingStateCodec.Read(r,PlatformStateCodec.Read(r,tick,players,fish,coins)))));
        state=RoomRulesStateCodec.Read(r,state);
        state=TreasureStateCodec.Read(r,state);
        state=CommissionStateCodec.Read(r,state);
        state=OceanStateCodec.Read(r,state);
        state=FinaleStateCodec.Read(r,state);
        state=PresenceStateCodec.Read(r,state);
        state=ComboStateCodec.Read(r,state);
        state=MeleeStateCodec.Read(r,state);
        PlatformStateCodec.ValidateBalances(state);
        if(state.Tackle!=null)foreach(var p in state.Players)RevivalStateCodec.Validate(state,p);
        foreach(var p in state.Players)
            if(p.Shopping && (state.Tackle==null?!HookUpgrades.IsNearShop(p.X,p.Z):
                !MerchantRules.CanInteract(p) || !Enumerable.Range(0,state.Tackle.Merchants.Count).Any(id=>MerchantRules.Near(state.Tackle.Config,state.Tiles,p,id))))
                throw new InvalidDataException("Shopping outside an available merchant.");
        players=state.Players.ToArray();fish=state.Fish.ToArray();
        if(r.BaseStream.Position!=bytes.Length ||
            (state.Tackle==null?players.Sum(p=>(long)p.Coins-HookUpgrades.TotalSpent(p.RangeLevel,p.SpeedLevel)):players.Sum(p=>(long)p.Coins))!=coins)
            throw new InvalidDataException("Invalid wallet/state tail.");
        foreach(var p in players)
        {
            if(p.FishId>=fishCount || (p.FishId>=0 && (p.Phase!=HookPhase.Returning || (state.Tackle==null && fish[p.FishId].Owner!=p.Slot))))
                throw new InvalidDataException("Invalid hook ownership.");
            if((p.HookedPlayer>=0 && (p.Phase!=HookPhase.Returning || p.FishId>=0 ||
                players[p.HookedPlayer].PulledBy!=p.Slot || p.HookX!=players[p.HookedPlayer].X || p.HookZ!=players[p.HookedPlayer].Z)) ||
                (p.PulledBy>=0 && players[p.PulledBy].HookedPlayer!=p.Slot))
                throw new InvalidDataException("Invalid player pull ownership.");
        }
        foreach(var f in fish)
            if((f.Phase==FishPhase.Hooked)!=(f.Owner>=0) || (f.Owner>=0 && players[f.Owner].FishId!=f.Id))
                throw new InvalidDataException("Invalid fish ownership.");
        return state;
    }
    public static int Sqrt(long n)
    {
        if(n<=0)return 0;
        long x=n,y=(x+1)/2;
        while(y<x){x=y;y=(x+n/x)/2;}
        return (int)x;
    }
    /// <summary>Swept-circle query: fast hooks cannot skip a fish between frames.</summary>
    public static bool SegmentHit(int ax,int az,int bx,int bz,int x,int z,int radius)
    {
        long dx=bx-ax,dz=bz-az,len=dx*dx+dz*dz;
        if(len==0)return IslandMap.DistanceSquared(ax,az,x,z)<=(long)radius*radius;
        long t=Math.Max(0,Math.Min(len,(x-ax)*dx+(z-az)*dz));
        long px=ax+dx*t/len,pz=az+dz*t/len;
        return (x-px)*(x-px)+(z-pz)*(z-pz)<=(long)radius*radius;
    }
    public static int Value(int id)=>10+FishMotion.Tier(id)*10;
    // Existing prototype schools represent 1 / 2 / 3 kg ordinary fish until species/weight generation is implemented.
    public static int BoneValue(int id)=>2*(FishMotion.Tier(id)+1);

    private sealed partial class Model : IDeterministicSimulation<HookState>
    {
        private readonly bool initialFriendlyPranks;
        public Model(bool initialFriendlyPranks){this.initialFriendlyPranks=initialFriendlyPranks;}
        private IslandMap map=null!;
        private Fisher[] players=Array.Empty<Fisher>();
        private Fish[] fish=Array.Empty<Fish>();
        private List<PlatformTile> tiles=new List<PlatformTile>();
        private Driftwood[] driftwood=Array.Empty<Driftwood>();
        private EquipmentSystem? equipment;
        private EnemySystem? enemies;
        private ulong tick;
        private int coins;
        private CommissionState commissions=CommissionState.Empty;
        private readonly List<ByteString> events=new List<ByteString>();
        private void Record(HookGameplayEvent value)=>events.Add(new ByteString(HookGameplayEventCodec.Encode(value)));
        private void Record(HookGameplayEventKind kind,Fisher p,int amount=0,int value=0,int targetId=-1,ItemKind itemKind=default,int itemType=0)
            =>Record(new HookGameplayEvent(kind,p.Slot,amount,value,targetId,p.X,p.Z,itemKind,itemType));
        private void ObserveInventory(){if(equipment!=null)equipment.Inventory.Events=Record;}
        private void RecordCombo(Fisher p,int previous){if(p.Combo.Count>previous)Record(HookGameplayEventKind.ComboIncreased,p,value:p.Combo.Count);}
        public void Initialize(SimulationInitialization init)
        {
            if(init.Session.TickRate!=Rate)throw new InvalidDataException("Hook Island requires 30 Hz.");
            map=IslandMap.Decode(init.Data.Span);
            tiles=map.Platform==null?new List<PlatformTile>():PlatformRules.InitialTiles().ToList();
            driftwood=map.Platform==null?Array.Empty<Driftwood>():Enumerable.Range(0,DriftwoodMotion.Count).Select(i=>DriftwoodMotion.Create(i)).ToArray();
            players=init.Session.Participants.Select(p=>{
                if(p.Slot>=4)throw new InvalidDataException("Invalid slot.");
                var s=map.Spawns[p.Slot];if(!map.CanStand(s.X,s.Z))throw new InvalidDataException("Spawn outside walkable island.");
                return new Fisher { Slot=p.Slot,X=s.X,Z=s.Z,AimX=s.X,AimZ=s.Z-5000,FishId=-1,HookedPlayer=-1,PulledBy=-1,DriftwoodId=-1,
                    WaitingToJoin=!p.IsActive,Unavailable=!p.IsActive };
            }).ToArray();
            equipment=map.Fishing==null?null:new EquipmentSystem(map.Fishing,players);
            events.Clear();ObserveInventory();
            enemies=map.Fishing?.Enemies.Enabled==true?new EnemySystem(map.Fishing.Enemies):null;
            commissions=CommissionSystem.Create(map.Fishing?.Commissions??CommissionConfig.Disabled);
            InitializeOcean();
            InitializeFinale();
            fish=map.FishSpots.Select((p,i)=>CreateFish(i,i*5,0)).ToArray();
            tick=0;coins=0;roomRules=new RoomRulesState(initialFriendlyPranks);
            InitializeTreasure();
        }
        public IReadOnlyList<ByteString> Step(FrameContext context,IReadOnlyList<SimulationInput> inputs)
        {
            events.Clear();
            bool finished=finale?.Capture().Phase==FinalePhase.Victory || equipment!=null && RevivalSystem.GameOver(players);
            foreach(var change in context.MemberEvents)
                if(change.Kind==MemberEventKind.Deactivate && change.Slot<players.Length)
                {
                    int slot=change.Slot;var leaving=players[slot];
                    StopCharacterHook(ref leaving);leaving.Swimming.Active=false;leaving.Unavailable=true;leaving.Shopping=false;players[slot]=leaving;
                }
                else if(change.Slot<players.Length && !(finished && players[change.Slot].WaitingToJoin))
                {players[change.Slot].Unavailable=false;players[change.Slot].WaitingToJoin=false;}
            // Inactive restored seats have no input records. Missing active inputs still
            // have neutral substitutes, so this applies ordinary disconnect rules only.
            if(context.Tick==1 && context.PersistentTick>1)
                for(int i=0;i<players.Length;i++)
                    if(!players[i].Unavailable && !inputs.Any(input=>input.Participant.Slot==players[i].Slot))
                    {var absent=players[i];StopCharacterHook(ref absent);absent.Swimming.Active=false;absent.Unavailable=true;absent.Shopping=false;players[i]=absent;}
            // A previous-round identity can reconnect to inspect the result, but a reserved
            // body must not enter a finished world and turn defeat back into a playable round.
            if(finished)return Array.Empty<ByteString>();
            equipment?.Advance(map,tiles,players,tick+1);
            var healthAtFrameStart=players.Select(p=>p.Vitals.Health).ToArray();
            var active=players.Select(p=>p.Phase!=HookPhase.Ready || p.Melee.Active).ToArray();
            ocean?.BeforeStep(map,tiles,fish,tick+1);
            UpdateFish();DriftTreasures();
            for(int i=0;i<driftwood.Length;i++)DriftwoodMotion.Advance(ref driftwood[i]);
            var commands=new HookInput[players.Length];
            foreach(var input in inputs)commands[input.Participant.Slot]=HookInput.Decode(input.Payload.Span);
            for(int i=0;i<players.Length;i++)
            {
                if(players[i].Unavailable)commands[i]=default;
                if(!players[i].WaitingToJoin && players[i].ProtectionTicks>0)players[i].ProtectionTicks--;
            }
            ApplyRoomRules(commands[0].RoomRules);
            // All voluntary actions precede hook collisions; claims remain in stable slot order.
            for(int i=0;i<players.Length;i++)
            {
                var p=players[i];var c=commands[i];
                if(p.WaitingToJoin)continue;
                if(p.FoodCooldownTicks>0)p.FoodCooldownTicks--;
                bool wasDowned=p.Vitals.Downed;
                if(equipment!=null)RevivalSystem.Respawn(map,tiles,players,ref p,c.Revival,equipment.Attributes(p),ref coins);
                if(wasDowned && !p.Vitals.Downed)
                {
                    // Early payment can occur during a teammate's tow. Release only after the transaction succeeds.
                    if(p.PulledBy>=0)
                    {var owner=players[p.PulledBy];ReleasePlayer(ref owner);players[owner.Slot]=owner;p.PulledBy=-1;p.ProtectionTicks=ProtectionDuration;}
                    StopCharacterHook(ref p);
                }
                bool knockedBack=p.Knockback.Active;
                if(knockedBack)
                {bool wasInWater=p.Swimming.InWater;KnockbackSystem.Advance(map,tiles,players,ref p);
                    if(!wasInWater && p.Swimming.InWater)ComboRules.Interrupt(ref p.Combo);}
                bool canAct=!p.Unavailable && (equipment==null || !p.Vitals.Downed);
                if(!canAct)StopCharacterHook(ref p);
                p.Shopping=canAct && !p.Melee.Active && !knockedBack && !p.Swimming.InWater && (equipment!=null || map.Platform==null) && c.Shopping && p.PulledBy<0 && p.HookedPlayer<0 && FishingRules.NearShop(map,tiles,p);
                Purchase(ref p,c);equipment?.Apply(map,tiles,ref p,c,ref coins,c.Revival.RescueTarget!=0 || players.Any(other=>other.Revival.RescueTicks>0 && other.Revival.Rescuer==i+1));
                if(c.Cancel || (equipment!=null && p.Swimming.InWater && equipment.Ring(p)==null))RetractHook(ref p);
                if(c.AimX!=0 || c.AimZ!=0 || c.Fire){p.AimX=c.AimX;p.AimZ=c.AimZ;}
                if(p.Cooldown>0)p.Cooldown--;
                var melee=p.Melee;if(melee.CooldownTicks>0)melee.CooldownTicks--;p.Melee=melee;
                if(!MeleeRules.CanAct(p,equipment?.Ring(p)!=null))MeleeRules.Cancel(ref p);
                if(map.Fishing?.Melee.Enabled==true && equipment?.ActiveTool(p)==true && c.Fire && c.Build==TileKind.None && c.RodAction==RodAction.None && c.Inventory.Action==InventoryAction.None && c.Treasure.IsEmpty && c.Revival.RescueTarget==0)
                {var weapon=equipment?.Weapon(p);if(weapon!=null && MeleeRules.Begin(ref p,weapon,c.AimX,c.AimZ,equipment!.Ring(p)!=null))
                    Record(HookGameplayEventKind.MeleeSwing,p,itemKind:ItemKind.Weapon,itemType:p.Melee.WeaponType);}
                bool canCast=equipment==null || SwimmingSystem.CanCast(p.Swimming.InWater,p.Vitals.Downed,equipment.Ring(p)!=null);
                if(canAct && canCast && (equipment==null || equipment.ActiveTool(p)) && c.Fire && c.Build==TileKind.None && c.RodAction==RodAction.None && c.Inventory.Action==InventoryAction.None && c.Treasure.IsEmpty && !p.Melee.Active &&
                    (equipment==null || p.EquippedRod!=0) && p.PulledBy<0 && !p.Shopping && p.Phase==HookPhase.Ready && p.Cooldown==0)
                {
                    int dx=c.AimX-p.X,dz=c.AimZ-p.Z,length=Sqrt((long)dx*dx+(long)dz*dz);
                    if(length>100)
                    {
                        p.DirectionX=dx*1000/length;p.DirectionZ=dz*1000/length;
                        p.HookX=p.X;p.HookZ=p.Z;p.Phase=HookPhase.Flying;p.Distance=0;p.Casts++;ComboRules.Begin(ref p.Combo,map.Fishing?.Combo);p.HookTravelRemainder=p.ReelRemainder=0;
                        Record(HookGameplayEventKind.HookCast,p);
                    }
                }
                int movementPermille=CharacterMovement.HookSpeedPermille(p);
                bool canMove=canAct && !knockedBack && p.PulledBy<0 && !p.Shopping && !p.Melee.Active && movementPermille>0;
                p.Swimming.Active=false;
                if(equipment!=null)
                {
                    var ring=equipment.Ring(p);
                    bool swim=canMove && SwimmingSystem.Active(p.Swimming.InWater,!canAct,p.PulledBy>=0,c.MoveX,c.MoveZ);
                    // Fish costs are shared and charged once per target after all hook joins.
                    int fishCost=0;
                    int fraction=SwimmingSystem.Spend(ref p.Stamina,swim?SwimmingSystem.DrainPerTick(map.Fishing!.Swimming,ring):0,fishCost);
                    active[i]|=swim;
                    if(canMove)
                    {
                        int dx,dz;
                        if(p.Swimming.InWater)
                        {
                            int speed=SwimmingSystem.Speed(map.Fishing!.Swimming,ring)*movementPermille/1000;
                            dx=SwimmingSystem.Delta(c.MoveX,speed,fraction,ref p.Swimming.MoveRemainderX);
                            dz=SwimmingSystem.Delta(c.MoveZ,speed,fraction,ref p.Swimming.MoveRemainderZ);
                        }
                        else
                        {
                            int speed=equipment.Attributes(p).MoveSpeed*movementPermille/1000;
                            dx=CharacterMovement.Delta(c.MoveX,speed,ref p.MoveRemainderX);
                            dz=CharacterMovement.Delta(c.MoveZ,speed,ref p.MoveRemainderZ);
                        }
                        var next=SwimmingTerrain.Move(map,tiles,players,p.Slot,new Point(p.X,p.Z),new Point(p.X+dx,p.Z+dz),p.Swimming.InWater,false,out bool water);
                        if(water && !p.Swimming.InWater)ComboRules.Interrupt(ref p.Combo);
                        p.X=next.X;p.Z=next.Z;p.Swimming.InWater=water;
                        p.Swimming.Active=SwimmingSystem.Active(water,false,false,c.MoveX,c.MoveZ);
                        active[i]|=p.Swimming.Active;
                    }
                    if(p.Swimming.InWater)
                    {
                        p.Shopping=false;
                        if(ring==null)RetractHook(ref p);
                    }
                }
                else if(canMove)
                {
                    int speed=(map.Platform==null?180:100)*movementPermille/1000;
                    int nx=c.MoveX*speed+p.RemainderX,nz=c.MoveZ*speed+p.RemainderZ;
                    int x=p.X+nx/1000,z=p.Z+nz/1000;
                    if(CanStand(x,p.Z)){p.X=x;p.RemainderX=nx%1000;}else p.RemainderX=0;
                    if(CanStand(p.X,z)){p.Z=z;p.RemainderZ=nz%1000;}else p.RemainderZ=0;
                }
                else p.RemainderX=p.RemainderZ=0;
                active[i]|=p.Phase!=HookPhase.Ready || p.Melee.Active;players[i]=p;
            }
            PlatformBuildSystem.Resolve(map,players,tiles,fish,driftwood,commands,equipment?.Inventory,chests,finale?.Capture());
            // New tiles can complete a player's footprint without moving them. Reconcile before
            // harvest and water damage, including characters whose own hook or downed state locks movement.
            if(equipment!=null)for(int i=0;i<players.Length;i++)
            {
                if(players[i].WaitingToJoin)continue;
                bool water=!SwimmingTerrain.Ground(map,tiles,players[i].X,players[i].Z);
                if(water && !players[i].Swimming.InWater)ComboRules.Interrupt(ref players[i].Combo);
                players[i].Swimming.InWater=water;
                if(!players[i].Swimming.InWater)players[i].Swimming.Active=false;
            }
            PruneCatches();PruneTreasures();PruneFinale();
            for(int i=0;i<players.Length;i++){var p=players[i];UpdateHook(ref p);players[i]=p;}
            PruneCatches();
            AdvanceCatches();
            AdvanceMelee(commands);
            enemies?.Advance(players,ApplyEnemyHit,IsMonsterWater,ocean?.AllowNewAttacks(tick+1)??true);
            if(equipment!=null)for(int i=0;i<players.Length;i++)
            {
                var p=players[i];
                if(p.WaitingToJoin)continue;
                if(p.Swimming.InWater && equipment.Ring(p)==null)RetractHook(ref p);
                int healthBeforeDamage=p.Vitals.Health;
                SwimmingSystem.Damage(ref p.Swimming,ref p.Vitals,map.Fishing!.Swimming,equipment.Ring(p)!=null,equipment.Attributes(p).HealthMax);
                if(p.Vitals.Health<healthBeforeDamage)Record(HookGameplayEventKind.Damaged,p,amount:healthBeforeDamage-p.Vitals.Health);
                if(p.Vitals.Downed){StopCharacterHook(ref p);p.Swimming.Active=false;}
                if(!MeleeRules.CanAct(p,equipment.Ring(p)!=null))MeleeRules.Cancel(ref p);
                StaminaSystem.Recover(ref p,map.Fishing!,equipment.Maximum(p),p.Vitals.Downed || active[i] || p.Phase!=HookPhase.Ready);
                players[i]=p;
            }
            if(equipment!=null)
            {
                bool solo=RevivalSystem.IsSolo(players);
                var alreadyDead=players.Select(p=>p.Revival.Dead).ToArray();
                RevivalSystem.Resolve(map,tiles,players,commands,healthAtFrameStart,tick+1,equipment.Attributes);
                for(int i=0;i<players.Length;i++)if(players[i].Revival.Dead)
                {
                    var dead=players[i];
                    if(dead.PulledBy>=0)
                    {var owner=players[dead.PulledBy];ReleasePlayer(ref owner);players[owner.Slot]=owner;dead=players[i];}
                    StopCharacterHook(ref dead);dead.Knockback=default;dead.Swimming.Active=false;dead.Shopping=false;
                    // One automatic attempt on the death transition; failures must not silently retry.
                    if(solo && !alreadyDead[i] && !dead.Unavailable && dead.Coins>=dead.Revival.DeathCost && dead.Revival.LastRequest<uint.MaxValue)
                        RevivalSystem.Respawn(map,tiles,players,ref dead,new RevivalCommand(respawnRequest:dead.Revival.LastRequest+1,deathId:dead.Revival.DownedId),equipment.Attributes(dead),ref coins);
                    players[i]=dead;
                }
            }
            ReelTreasures();OpenTreasures(commands);
            ocean?.Advance(map,tiles,players,fish,commissions.Completed,enemies?.Capture()??EnemyState.Empty,tick+1);
            AdvanceFinale();
            tick++;return events.ToArray();
        }
        private void ApplyEnemyHit(EnemyHit hit)
        {
            var p=players[hit.Slot];
            MeleeRules.Cancel(ref p);
            int healthBeforeDamage=p.Vitals.Health;
            CharacterSystem.Damage(ref p.Vitals,hit.Damage,DamageKind.Combat,equipment!.Attributes(p).Armor);
            if(p.Vitals.Health<healthBeforeDamage)Record(HookGameplayEventKind.Damaged,p,amount:healthBeforeDamage-p.Vitals.Health);
            if(hit.Knockback)
            {
                if(p.PulledBy>=0)
                {
                    var owner=players[p.PulledBy];ReleasePlayer(ref owner);players[owner.Slot]=owner;
                    p.PulledBy=-1;p.ProtectionTicks=players[p.Slot].ProtectionTicks;
                }
                RetractHook(ref p);
                KnockbackSystem.Start(ref p,hit.DirectionX,hit.DirectionZ,hit.KnockbackDistance);
            }
            players[hit.Slot]=p;
        }
        private void RetractHook(ref Fisher p)
        {
            ComboRules.FailPending(ref p.Combo);
            FinaleRules.ClearLine(ref p);
            DetachTreasure(ref p);
            ReleasePiranha(ref p);
            if(p.HookedPlayer>=0)ReleasePlayer(ref p);
            if(p.FishId>=0 && (equipment!=null || map.Platform!=null))
                ReleaseFish(ref p);
            if(p.DriftwoodId>=0){driftwood[p.DriftwoodId]=DriftwoodMotion.Create(p.DriftwoodId,90);p.DriftwoodId=-1;}
            if(p.Phase!=HookPhase.Ready)p.Phase=HookPhase.Returning;
        }
        private void StopCharacterHook(ref Fisher p)
        {
            MeleeRules.Cancel(ref p);
            p.Combo=default;
            FinaleRules.ClearLine(ref p);
            DetachTreasure(ref p);
            ReleasePiranha(ref p);
            if(p.HookedPlayer>=0)ReleasePlayer(ref p);
            if(p.FishId>=0)ReleaseFish(ref p);
            if(p.DriftwoodId>=0){driftwood[p.DriftwoodId]=DriftwoodMotion.Create(p.DriftwoodId,90);p.DriftwoodId=-1;}
            p.Phase=HookPhase.Ready;p.HookX=p.X;p.HookZ=p.Z;p.Distance=0;
        }
        private void ReleasePiranha(ref Fisher p)
        {
            if(p.HookedPiranha==0)return;
            if(equipment!=null){DetachCatch(ref p);return;}
            var f=enemies!.GetPiranha(p.HookedPiranha-1);
            var landing=CatchEscapeMotion.Landing(map,tiles,new Point(f.X,f.Z),new Point(p.X,p.Z));
            enemies.Release(f.Id,p.Slot,landing,p.Swimming.InWater?100:1250);p.HookedPiranha=0;
        }
        private void ReleaseFish(ref Fisher p)
        {
            if(equipment!=null){DetachCatch(ref p);return;}
            var f=fish[p.FishId];
            var landing=CatchEscapeMotion.Landing(map,tiles,new Point(f.X,f.Z),new Point(p.X,p.Z));
            f.Escape=CatchEscapeMotion.Begin(f.X,f.Y,f.Z,landing);f.Phase=FishPhase.Escaping;f.Timer=0;f.Owner=-1;
            fish[p.FishId]=f;p.FishId=-1;
        }
        private bool IsMonsterWater(int x,int z)
        {
            // Keep the entire body clear of deck edges, including newly built platforms.
            if(map.Platform!=null)
            {
                if(Math.Abs((long)x)+900>SwimmingTerrain.WorldLimit || Math.Abs((long)z)+900>SwimmingTerrain.WorldLimit)return false;
                foreach(var tile in tiles)
                    if(Math.Abs(x-tile.X*PlatformRules.TileSize)<PlatformRules.HalfTile+900 &&
                        Math.Abs(z-tile.Z*PlatformRules.TileSize)<PlatformRules.HalfTile+900)return false;
                foreach(var cell in map.Platform.BlockedCells)
                    if(Math.Abs(x-cell.X*PlatformRules.TileSize)<PlatformRules.HalfTile+900 &&
                        Math.Abs(z-cell.Z*PlatformRules.TileSize)<PlatformRules.HalfTile+900)return false;
                return true;
            }
            for(int dz=-900;dz<=900;dz+=300)for(int dx=-900;dx<=900;dx+=300)
                if(SwimmingTerrain.Obstacle(map,x+dx,z+dz) || map.OnLand(x+dx,z+dz))return false;
            return true;
        }
        private void Purchase(ref Fisher p,HookInput command)
        {
            if(command.Upgrade==HookUpgrade.None || command.PurchaseId<=p.LastPurchaseId)return;
            // Record failed requests too: moving closer or earning coins cannot replay an old click.
            p.LastPurchaseId=command.PurchaseId;
            p.LastPurchaseResult=map.Platform==null && equipment==null?HookUpgrades.CheckPurchase(p,coins,command.Upgrade):PurchaseResult.TooFar;
            if(p.LastPurchaseResult!=PurchaseResult.Success)return;
            int level=command.Upgrade==HookUpgrade.Range?p.RangeLevel:p.SpeedLevel;
            coins-=HookUpgrades.Cost(level);
            if(command.Upgrade==HookUpgrade.Range)p.RangeLevel++;else p.SpeedLevel++;
            Record(HookGameplayEventKind.Upgraded,p,value:level+1,targetId:(int)command.Upgrade);
        }
        private void UpdateFish()
        {
            for(int i=0;i<fish.Length;i++)
            {
                if(ocean!=null && !ocean.Available(i,commissions.Completed))continue;
                if(fish[i].Phase!=FishPhase.Escaping){FishMotion.Advance(ref fish[i],ocean?.Home(map,i)??map.FishSpots[i]);continue;}
                if(!CatchEscapeMotion.Advance(ref fish[i].Escape))
                {ocean?.EndVisit(i);fish[i]=CreateFish(i,100,unchecked(fish[i].Generation+1));}
                else CatchEscapeMotion.Position(fish[i].Escape,out fish[i].X,out fish[i].Y,out fish[i].Z);
            }
        }
        private void UpdateHook(ref Fisher p)
        {
            if(equipment!=null && p.DriftwoodId>=0)
            {
                var rod=equipment.Equipped(p);
                if(rod==null || IslandMap.DistanceSquared(p.X,p.Z,p.HookX,p.HookZ)>(long)rod.Range*rod.Range)
                {ComboRules.FailHarvest(ref p.Combo);RetractHook(ref p);}
            }
            if(p.HookedPlayer>=0){PullPlayer(ref p);return;}
            if(p.Phase==HookPhase.Flying)
            {
                int oldX=p.HookX,oldZ=p.HookZ;
                int range=equipment==null?map.HookRange(p.RangeLevel):equipment.Equipped(p)!.Range;
                int speed=equipment==null?HookUpgrades.SpeedPerTick(p.SpeedLevel):FishingRules.Travel(equipment.Equipped(p)!.Speed,ref p.HookTravelRemainder);
                int travel=Math.Min(speed,range-p.Distance);
                p.HookX+=p.DirectionX*travel/1000;p.HookZ+=p.DirectionZ*travel/1000;p.Distance+=travel;
                int closest=map.Platform==null?Math.Min(HitFraction(oldX,oldZ,p.HookX,p.HookZ,0,1050,2150),
                    HitFraction(oldX,oldZ,p.HookX,p.HookZ,-2150,1550,490)):int.MaxValue;
                int playerHit=-1,fishHit=-1,woodHit=-1,piranhaHit=-1,chestHit=-1;bool monsterHit=false,finaleHit=false;
                for(int i=0;i<players.Length;i++)
                {
                    var target=players[i];
                    if(!CanHookPlayer(p,target))continue;
                    int hit=HitFraction(oldX,oldZ,p.HookX,p.HookZ,target.X,target.Z,550);
                    if(hit<closest){closest=hit;playerHit=i;}
                }
                for(int i=0;i<fish.Length;i++)
                {
                    var f=fish[i];
                    if((f.Phase!=FishPhase.Jumping || f.Y<300) && !(equipment!=null && f.Phase==FishPhase.Hooked))continue;
                    if(equipment!=null && f.Owner>=0 && CatchYieldsToRescue(p,new Fisher{FishId=i},new Point(f.X,f.Z)))continue;
                    int hit=HitFraction(oldX,oldZ,p.HookX,p.HookZ,f.X,f.Z,720);
                    if(hit<closest){closest=hit;fishHit=i;playerHit=-1;}
                }
                for(int i=0;i<driftwood.Length;i++)
                {
                    var d=driftwood[i];if(d.Phase!=DriftwoodPhase.Floating)continue;
                    int hit=HitFraction(oldX,oldZ,p.HookX,p.HookZ,d.X,d.Z,DriftwoodMotion.Radius(i));
                    if(hit<closest){closest=hit;woodHit=i;playerHit=fishHit=-1;}
                }
                if(enemies!=null)
                {
                    for(int i=0;i<enemies.PiranhaCount;i++)
                    {
                        var f=enemies.GetPiranha(i);
                        if(f.RespawnTicks>0 || f.Escape.Active)continue;
                        if(f.Owner>=0 && CatchYieldsToRescue(p,new Fisher{FishId=-1,HookedPiranha=i+1},new Point(f.X,f.Z)))continue;
                        int hit=HitFraction(oldX,oldZ,p.HookX,p.HookZ,f.X,f.Z,450);
                        if(hit<closest){closest=hit;piranhaHit=f.Id;playerHit=fishHit=woodHit=-1;}
                    }
                    var m=enemies.Monster;
                    if(map.Fishing!.Enemies.MonsterEnabled && m.Phase==MonsterPhase.Windup)
                    {
                        int hit=HitFraction(oldX,oldZ,p.HookX,p.HookZ,m.X,m.Z,EnemyGeometry.MonsterRadius);
                        if(hit<closest){closest=hit;monsterHit=true;playerHit=fishHit=woodHit=piranhaHit=-1;}
                    }
                }
                for(int i=0;i<chests.Length;i++)
                {
                    var chest=chests[i];if(!CanJoinTreasure(chest))continue;
                    int hit=HitFraction(oldX,oldZ,p.HookX,p.HookZ,chest.X,chest.Z,TreasureMotion.Radius);
                    if(hit<closest){closest=hit;chestHit=i;playerHit=fishHit=woodHit=piranhaHit=-1;monsterHit=false;}
                }
                var giant=finale?.Capture();
                if(giant?.Phase==FinalePhase.Surfaced)
                {
                    int hit=HitFraction(oldX,oldZ,p.HookX,p.HookZ,giant.X,giant.Z,map.Fishing!.Finale.BodyRadius);
                    if(hit<closest){closest=hit;finaleHit=true;playerHit=fishHit=woodHit=piranhaHit=chestHit=-1;monsterHit=false;}
                }
                if(playerHit>=0)
                {
                    var target=players[playerHit];target.PulledBy=p.Slot;target.TimesHooked++;
                    FinaleRules.ClearLine(ref target);
                    target.RemainderX=target.RemainderZ=0;target.Swimming.Active=false;target.Shopping=false;target.ProtectionTicks=0;
                    // Stop a victim's outgoing hook, but keep reeling any fish they already caught.
                    if(target.Phase==HookPhase.Flying)
                    {target.Phase=HookPhase.Returning;ComboRules.Preserve(ref target.Combo);}
                    players[playerHit]=target;
                    p.HookedPlayer=playerHit;p.PullTicks=0;p.PlayerHooks++;ComboRules.Preserve(ref p.Combo);
                    p.HookX=target.X;p.HookZ=target.Z;p.Phase=HookPhase.Returning;
                    Record(HookGameplayEventKind.PlayerHooked,p,targetId:playerHit);
                }
                else if(fishHit>=0)
                {
                    if(equipment!=null){JoinCatch(ref p,fishHit,false);p.Phase=HookPhase.Returning;}
                    else
                    {
                        var f=fish[fishHit];f.Phase=FishPhase.Hooked;f.Owner=p.Slot;fish[fishHit]=f;
                        p.HookX=f.X;p.HookZ=f.Z;p.FishId=fishHit;p.Phase=HookPhase.Returning;
                    }
                }
                else if(woodHit>=0)
                {
                    var d=driftwood[woodHit];d.Phase=DriftwoodPhase.Hooked;d.Owner=p.Slot;driftwood[woodHit]=d;
                    p.HookX=d.X;p.HookZ=d.Z;p.DriftwoodId=woodHit;p.Phase=HookPhase.Returning;
                }
                else if(piranhaHit>=0)
                {JoinCatch(ref p,piranhaHit,true);p.Phase=HookPhase.Returning;}
                else if(chestHit>=0)
                {JoinTreasure(ref p,chestHit);p.Phase=HookPhase.Returning;}
                else if(finaleHit)
                {ComboRules.FailHarvest(ref p.Combo);JoinFinale(ref p);p.Phase=HookPhase.Returning;}
                else if(closest!=int.MaxValue)
                {
                    if(monsterHit){ComboRules.FailHarvest(ref p.Combo);enemies!.Interrupt();}
                    p.HookX=oldX+(int)((long)(p.HookX-oldX)*closest/1000000);
                    p.HookZ=oldZ+(int)((long)(p.HookZ-oldZ)*closest/1000000);
                    p.Phase=HookPhase.Returning;
                }
                if(p.Distance>=range)p.Phase=HookPhase.Returning;
            }
            else if(p.Phase==HookPhase.Returning)
            {
                if(p.HookedFinale || p.HookedChest>0 || equipment!=null && CooperativeFishing.Attached(p))return;
                int returnStep=HookUpgrades.ReturnPerTick;
                int dx=p.X-p.HookX,dz=p.Z-p.HookZ,length=Sqrt((long)dx*dx+(long)dz*dz);
                if(length<=returnStep)
                {
                    if(equipment!=null && p.Swimming.InWater && p.DriftwoodId>=0)
                    {
                        p.HookX=p.X;p.HookZ=p.Z;FollowHook(p);return;
                    }
                    if(p.FishId>=0)
                    {
                        // 所有可能失败的边界检查和后继鱼生成都放在提交库存之前，不能只发鱼骨却没记收入。
                        // Only the legacy, unequipped prototype reaches this single-owner settlement.
                        int value=Value(p.FishId);
                        if(coins>int.MaxValue-value || p.Coins>int.MaxValue-value || p.Catches==int.MaxValue)return;
                        // 代数是确定性随机序列的 uint 计数器，明确按 2^32 循环，取消／挣脱也使用相同规则。
                        var nextFish=CreateFish(p.FishId,100,unchecked(fish[p.FishId].Generation+1));
                        if(map.Platform!=null && !PersonalMaterialInventory.Credit(ref p,TileKind.Fishbone,BoneValue(p.FishId),null))return;
                        coins+=value;p.Coins+=value;p.Catches++;
                        Record(HookGameplayEventKind.CoinsReceived,p,amount:value);
                        if(map.Platform!=null)Record(HookGameplayEventKind.MaterialReceived,p,amount:BoneValue(p.FishId),itemKind:ItemKind.Fishbone);
                        Record(HookGameplayEventKind.FishCaught,p,targetId:p.FishId);
                        fish[p.FishId]=nextFish;
                    }
                    if(p.DriftwoodId>=0)
                    {
                        if(!PersonalMaterialInventory.Credit(ref p,TileKind.Wood,DriftwoodMotion.Value(p.DriftwoodId),equipment?.Inventory))return;
                        if(equipment==null)Record(HookGameplayEventKind.MaterialReceived,p,amount:DriftwoodMotion.Value(p.DriftwoodId),itemKind:ItemKind.Wood);
                        driftwood[p.DriftwoodId]=DriftwoodMotion.Create(p.DriftwoodId,150);p.DriftwoodId=-1;
                        int previousCombo=p.Combo.Count;ComboRules.Succeed(ref p.Combo,map.Fishing?.Combo);RecordCombo(p,previousCombo);
                    }
                    ComboRules.FailPending(ref p.Combo);
                    p.FishId=-1;p.Phase=HookPhase.Ready;p.Cooldown=6;p.HookX=p.X;p.HookZ=p.Z;
                }
                else
                {
                    p.HookX+=dx*returnStep/length;p.HookZ+=dz*returnStep/length;
                    FollowHook(p);
                }
            }
        }
        private void FollowHook(Fisher p)
        {
            if(p.HookedPiranha>0)enemies!.Follow(p.HookedPiranha-1,p.Slot,p.HookX,p.HookZ);
            if(p.FishId>=0){var f=fish[p.FishId];f.X=p.HookX;f.Z=p.HookZ;f.Y=p.Swimming.InWater?100:1250;fish[p.FishId]=f;}
            if(p.DriftwoodId>=0){var d=driftwood[p.DriftwoodId];d.X=p.HookX;d.Z=p.HookZ;driftwood[p.DriftwoodId]=d;}
        }
        // Exact swept-circle entry order for a single (at most 1.2 m) flight step, using integer arithmetic.
        private bool CanHookPlayer(Fisher caster,Fisher target)
        {
            bool rescue=equipment!=null && target.Swimming.InWater;
            return target.Slot!=caster.Slot && !(equipment!=null && target.Revival.Dead) && !caster.Knockback.Active &&
                !target.WaitingToJoin && (!target.Unavailable || rescue) && !target.Knockback.Active && target.PulledBy<0 && target.HookedPlayer<0 &&
                (rescue || (roomRules.FriendlyPranksEnabled && !(target.Shopping || target.ProtectionTicks>0 ||
                    IslandMap.DistanceSquared(caster.X,caster.Z,target.X,target.Z)<=(long)PullStopDistance*PullStopDistance)));
        }
        private static int HitFraction(int ax,int az,int bx,int bz,int x,int z,int radius)=>EnemyGeometry.HitFraction(ax,az,bx,bz,x,z,radius);
        private void PullPlayer(ref Fisher owner)
        {
            if(!roomRules.FriendlyPranksEnabled && (equipment==null || !players[owner.HookedPlayer].Swimming.InWater))
            {ReleasePlayer(ref owner);return;}
            if(equipment!=null){PullAcrossWater(ref owner);return;}
            var target=players[owner.HookedPlayer];
            int dx=owner.X-target.X,dz=owner.Z-target.Z,length=Sqrt((long)dx*dx+(long)dz*dz);
            if(++owner.PullTicks>=PullLimitTicks || length<=PullStopDistance){ReleasePlayer(ref owner);return;}
            int travel=Math.Min(PullStep,length-PullStopDistance);
            int fromX=target.X,fromZ=target.Z;
            bool blocked=false;
            // Sample the entire dragged path; endpoint-only checks could cross a narrow water gap or tree.
            for(int moved=Math.Min(100,travel);;moved=Math.Min(moved+100,travel))
            {
                int x=fromX+dx*moved/length,z=fromZ+dz*moved/length;
                if(!CanStand(x,z)){blocked=true;break;}
                target.X=x;target.Z=z;
                if(moved==travel)break;
            }
            players[owner.HookedPlayer]=target;owner.HookX=target.X;owner.HookZ=target.Z;
            if(blocked || travel==length-PullStopDistance)ReleasePlayer(ref owner);
        }
        private void PullAcrossWater(ref Fisher owner)
        {
            var target=players[owner.HookedPlayer];
            if(++owner.PullTicks>=PullLimitTicks){ReleasePlayer(ref owner);return;}
            bool rescue=target.Swimming.InWater && !owner.Swimming.InWater;
            Point destination=new Point(owner.X,owner.Z);
            if(rescue && !SwimmingTerrain.RescueLanding(map,tiles,players,owner,target,out destination))
            {ReleasePlayer(ref owner);return;}
            int dx=destination.X-target.X,dz=destination.Z-target.Z,length=Sqrt((long)dx*dx+(long)dz*dz);
            int stop=rescue?0:PullStopDistance;
            if(length<=stop){ReleasePlayer(ref owner);return;}
            int travel=Math.Min(PullStep,length-stop);
            var next=SwimmingTerrain.Move(map,tiles,players,target.Slot,new Point(target.X,target.Z),
                new Point(target.X+dx*travel/length,target.Z+dz*travel/length),target.Swimming.InWater,rescue || !roomRules.FriendlyPranksEnabled,out bool water);
            bool blocked=next.X==target.X && next.Z==target.Z;
            if(water && !target.Swimming.InWater)ComboRules.Interrupt(ref target.Combo);
            target.X=next.X;target.Z=next.Z;target.Swimming.InWater=water;target.Swimming.Active=false;target.Shopping=false;
            players[target.Slot]=target;owner.HookX=target.X;owner.HookZ=target.Z;
            if(blocked || (!water && (rescue || !roomRules.FriendlyPranksEnabled)) || travel==length-stop)ReleasePlayer(ref owner);
        }
        private void ReleasePlayer(ref Fisher owner)
        {
            var target=players[owner.HookedPlayer];target.PulledBy=-1;target.ProtectionTicks=ProtectionDuration;
            players[owner.HookedPlayer]=target;owner.HookedPlayer=-1;owner.PullTicks=0;
        }
        private bool CanStand(int x,int z)=>map.Platform==null?map.CanStand(x,z):PlatformRules.CanStand(tiles,x,z);
        public HookState Capture()=>new HookState(tick,players,fish,coins,tiles,driftwood,equipment?.Capture(),enemies?.Capture(),roomRules,new TreasureState(chests),commissions,ocean?.Capture(),finale?.Capture());
        public void Restore(HookState state)
        {
            // Validate ownership as well as positions before using a received checkpoint.
            state=new HookGame().Decode(new HookGame().Encode(state));
            if((map.Fishing==null)!=(state.Tackle==null) ||
                (map.Fishing!=null && !map.Fishing.Encode().SequenceEqual(state.Tackle!.Config.Encode())) ||
                state.Players.Count!=players.Length || state.Fish.Count!=fish.Length || (map.Platform==null)!=(state.Tiles.Count==0) ||
                state.Players.Any(p=>map.Fishing==null?!map.CanStand(state,p.X,p.Z):SwimmingTerrain.Obstacle(map,p.X,p.Z) || p.Swimming.InWater==map.CanStand(state,p.X,p.Z)) ||
                (map.Platform!=null && state.Tiles.Any(t=>map.Platform.BlockedCells.Any(b=>b.X==t.X && b.Z==t.Z))))
                throw new InvalidDataException("State does not match island.");
            OceanStateCodec.ValidateMap(map,state);
            FinaleStateCodec.ValidateMap(map,state);
            tick=state.Steps;coins=state.Coins;players=state.Players.ToArray();fish=state.Fish.ToArray();roomRules=state.RoomRules;chests=state.Treasure.Chests.ToArray();
            commissions=state.Commissions;
            ocean=map.Fishing?.Ocean.Enabled==true?new OceanSystem(map.Fishing.Ocean,state.Ocean):null;
            finale=map.Fishing?.Finale.Enabled==true?new FinaleSystem(map.Fishing.Finale,state.Finale):null;
            tiles=state.Tiles.ToList();driftwood=state.Driftwood.ToArray();equipment=state.Tackle==null?null:new EquipmentSystem(state.Tackle);
            enemies=map.Fishing?.Enemies.Enabled==true?new EnemySystem(map.Fishing.Enemies,state.Enemies):null;
            events.Clear();ObserveInventory();
        }
    }
}
