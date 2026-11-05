using System;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

public static partial class TownGameplayTests
{
    static readonly HookGame Codec=new HookGame(false);
    static IDeterministicSimulation<HookState> Model(int count=1)
    {var m=Codec.Create();m.Initialize(new SimulationInitialization(new SessionDescriptor(Guid.Parse("68dce040-d1df-4de3-b03c-ab1ad91cc343"),1,30,Enumerable.Range(0,count).Select(i=>new Participant((ushort)i,Guid.Parse("00000000-0000-0000-0000-"+(i+1).ToString("D12")))).ToArray()),new ByteString(IslandMap.CreateTown().Encode())));return m;}
    static void Step(IDeterministicSimulation<HookState> m,HookInput input=default)
    {var s=m.Capture();m.Step(new FrameContext(s.Steps+1,s.Steps+1,30),new[]{new SimulationInput(new Participant(0,Guid.Parse("00000000-0000-0000-0000-000000000001")),new ByteString(input.Encode()))});}
    static HookState Copy(HookState s,Fisher[] p)=>new HookState(s.Steps,p,s.Fish,p.Sum(x=>x.Coins),s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,s.Crabs,s.Boss,s.Grenades,s.Eldritch,s.Town);
    static void At(IDeterministicSimulation<HookState> m,int building)
    {var s=m.Capture();var p=s.Players.ToArray();var door=TownLayout.Buildings[building-1].Entrance;p[0].X=p[0].HookX=door.X;p[0].Z=p[0].HookZ=door.Z;p[0].Swimming=default;m.Restore(Copy(s,p));for(int i=0;i<15;i++)Step(m);}
    static void MultiStep(IDeterministicSimulation<HookState> m,params HookInput[] commands)
    {
        var s=m.Capture();var inputs=Enumerable.Range(0,s.Players.Count).Select(i=>new SimulationInput(new Participant((ushort)i,Guid.Parse("00000000-0000-0000-0000-"+(i+1).ToString("D12"))),new ByteString((i<commands.Length?commands[i]:default).Encode()))).ToArray();
        m.Step(new FrameContext(s.Steps+1,s.Steps+1,30),inputs);
    }
    static void PrepareFinale(IDeterministicSimulation<HookState> m,bool empty=false)
    {
        var s=m.Capture();var ps=s.Players.ToArray();var roster=s.Eldritch.Creatures.ToArray();var zones=s.Town.Zones.ToArray();var people=s.Town.Players.ToArray();
        foreach(int z in new[]{2,4,5})
        {
            zones[z]=new TownZoneState{Started=true,Cleared=true};
            for(int i=0;i<TownEncounters.AdultCount;i++)if(TownEncounters.Zone(i)==z)
            {var c=roster[i];c.Phase=EldritchPhase.Dead;c.Generation=1;c.Timer=2700;c.DirectionX=1000000;c.Target=c.Ally=-1;c.RewardOwner=1;c.Rewarded=true;roster[i]=c;}
        }
        var inv=new InventorySystem(s.Tackle!.Inventory);
        for(int i=0;i<ps.Length;i++)
        {
            ps[i].X=ps[i].HookX=TownLayout.BeaconX+(i%2)*700;ps[i].Z=ps[i].HookZ=TownLayout.BeaconZ-(i/2)*700;ps[i].Swimming=default;
            if(empty){ps[i].Coins=0;people[i].ServiceSpent=300;int gun=inv.Capture().Items.Single(v=>v.Owner==i && v.Kind==ItemKind.Firearm).Id;for(int k=0;k<7;k++)inv.TryFireRound(i,gun);}
        }
        var town=new TownState(true,people,zones,goals:7);var setup=new HookState(s.Steps,ps,s.Fish,ps.Sum(p=>p.Coins),s.Tiles,s.Driftwood,new TackleState(s.Tackle.Config,inv.Capture(),s.Tackle.Merchants),s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,s.Crabs,s.Boss,s.Grenades,new EldritchState(roster),town);
        m.Restore(Codec.Decode(Codec.Encode(setup)));for(int i=0;i<15;i++)MultiStep(m);
    }
    static void Require(bool v,string msg){if(!v)throw new Exception(msg);}
    public static int Run(bool network=true)
    {
        int failed=0;
        void Test(string name,Action f){try{f();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
        Test("town_game_roundtrips_without_material_currency",()=>{
            var m=Model();var s=m.Capture();Require(s.Town.Enabled && s.Town.Players.Count==1,"Missing authoritative town state");
            Require(s.Driftwood.Count==0 && s.Players.All(p=>p.Wood==0 && p.Fishbones==0) && !s.Tackle!.Inventory.Items.Any(i=>InventoryCatalog.IsMaterial(i.Kind)),"Legacy currencies leaked");
            Require(Codec.Encode(s).SequenceEqual(Codec.Encode(Codec.Decode(Codec.Encode(s)))),"Town snapshot changed");Step(m);
        });
        Test("failed_service_is_final_after_save_and_retry",()=>{
            var m=Model();Step(m,new HookInput(0,0,town:new TownCommand(TownAction.Heal,1)));Require(m.Capture().Town.Players[0].LastResult==TownResult.TooFar,"Distance bypassed");
            At(m,3);var before=m.Capture().Players[0].Coins;Step(m,new HookInput(0,0,town:new TownCommand(TownAction.Heal,1)));
            Require(m.Capture().Players[0].Coins==before && m.Capture().Town.Players[0].LastResult==TownResult.TooFar,"Failed click replayed");
        });
        Test("inn_registration_and_free_map_survive_restore",()=>{
            var m=Model();At(m,10);Step(m,new HookInput(0,0,town:new TownCommand(TownAction.RegisterInn,1)));Require(m.Capture().Town.Players[0].InnRegistered,"No inn receipt");
            At(m,8);Step(m,new HookInput(0,0,town:new TownCommand(TownAction.RevealMap,2)));var saved=Codec.Encode(m.Capture());m.Restore(Codec.Decode(saved));
            Require(m.Capture().Town.MapKnownMask==1 && m.Capture().Town.Players[0].InnRegistered,"Lost personal progress");
        });
        Test("paid_shortcut_is_optional_atomic_and_deduplicated",()=>{
            var m=Model();At(m,9);int before=m.Capture().Players[0].Coins;var c=new HookInput(0,0,town:new TownCommand(TownAction.ShortcutOne,1));Step(m,c);var after=m.Capture();
            Require(after.Town.Shortcuts==1 && after.Players[0].Coins==before-60,"Shortcut price/result not atomic");m.Restore(Codec.Decode(Codec.Encode(after)));Step(m,c);
            Require(m.Capture().Players[0].Coins==before-60,"Shortcut charged twice");
        });
        Test("boss_cannot_start_before_three_free_objectives",()=>{
            var m=Model();At(m,12);Step(m,new HookInput(0,0,town:new TownCommand(TownAction.ReadyBoss,1)));
            Require(m.Capture().Boss.Phase==BossPhase.Dormant && m.Capture().Town.Players[0].LastResult==TownResult.GoalLocked,"Skipped town objectives");
        });
        Test("paused_district_retains_health_and_ignores_combat_until_reemergence",()=>{
            var map=IslandMap.CreateTown();var port=TownLayout.SpawnPorts.First(p=>p.ZoneId==1 && !p.Water);
            var roster=new EldritchSystem(map).Capture().Creatures.ToArray();var c=roster[3];c.Phase=EldritchPhase.Hunting;c.Generation=1;c.Health=60;c.X=port.X;c.Z=port.Z;c.DirectionX=1000000;roster[3]=c;
            var system=new EldritchSystem(map,new EldritchState(roster));system.ActivateTownZone(1,false);
            Require(system.Get(3).Paused && !system.Get(3).Visible && !system.Get(3).Damageable,"Paused enemy still occupies combat");
            Require(system.Strike(3,0,1000,port.X,port.Z)==0 && system.Get(3).Health==60,"Paused health was modified");
            system.ActivateTownZone(1,true);Require(system.Get(3).Health==60 && system.Get(3).Phase==EldritchPhase.Emerging && system.Get(3).Timer==45,"Reentry skipped emergence or healed");
        });
        Test("deep_southern_water_has_positive_distance_to_shore",()=>Require(TownLayout.DistanceToGround(-8000,-63000)>0,"Deep water reports shore distance zero"));
        Test("purchased_rounds_reload_finitely_and_survive_save",()=>{
            var m=Model();At(m,2);Step(m,new HookInput(0,0,town:new TownCommand(TownAction.BuyAmmo,1)));
            Require(m.Capture().Tackle!.Inventory.Balance(0,ItemKind.Ammunition)==14 && m.Capture().Players[0].Coins==270,"Ammo purchase has wrong quantity or price");
            var p=m.Capture().Players[0];
            for(int i=0;i<7;i++){Step(m,new HookInput(0,0,p.X+5000,p.Z,fire:true));for(int k=0;k<20;k++)Step(m);}
            Require(m.Capture().Tackle!.Inventory.ActiveItem(0)!.Value.LoadedAmmo==0,"Starter pistol did not consume rounds");
            Step(m,new HookInput(0,0,reload:true));for(int k=0;k<60;k++)Step(m);
            var s=m.Capture();Require(s.Tackle!.Inventory.ActiveItem(0)!.Value.LoadedAmmo==7 && s.Tackle.Inventory.Balance(0,ItemKind.Ammunition)==7,"Finite reload created ammunition");m.Restore(Codec.Decode(Codec.Encode(s)));
        });
        Test("clinic_ring_pawn_and_workshop_use_gold_receipts",()=>{
            var m=Model();At(m,3);var s=m.Capture();var p=s.Players.ToArray();p[0].Vitals.Health-=25;m.Restore(Copy(s,p));
            Step(m,new HookInput(0,0,town:new TownCommand(TownAction.Heal,1)));Require(m.Capture().Players[0].Coins==260 && m.Capture().Players[0].Vitals.Health==100,"Clinic did not heal and charge atomically");
            At(m,4);Step(m,new HookInput(0,0,town:new TownCommand(TownAction.BuyRing,2)));Require(m.Capture().Town.Players[0].LastResult==TownResult.Success,"Ring service: "+m.Capture().Town.Players[0].LastResult);var ring=m.Capture().Tackle!.Inventory.Items.Single(i=>i.Kind==ItemKind.SwimRing);
            Require(m.Capture().Players[0].Coins==110,"Ring price ignored");
            At(m,7);Step(m,new HookInput(0,0,town:new TownCommand(TownAction.Sell,3,ring.Id)));Require(m.Capture().Players[0].Coins==185 && !m.Capture().Tackle!.Inventory.Items.Any(i=>i.Id==ring.Id),"Pawn settlement incorrect");
            At(m,5);Step(m,new HookInput(0,0,town:new TownCommand(TownAction.BuyGrenade,4)));Require(m.Capture().Players[0].Coins==145 && m.Capture().Tackle!.Inventory.Items.Any(i=>i.Kind==ItemKind.Throwable),"Workshop created a free grenade");
            m.Restore(Codec.Decode(Codec.Encode(m.Capture())));
        });
        Test("selling_used_rifle_preserves_ammunition_and_fallback_weapon",()=>{
            var inv=new InventorySystem(1,true,true,false);int coins=2000;
            Require(inv.Purchase(0,ItemKind.Firearm,1,true,false,default,ref coins)==InventoryResult.Success,"Rifle purchase failed");
            var rifle=inv.Capture().Items.Single(i=>i.Kind==ItemKind.Firearm && i.Type==1);for(int i=0;i<9;i++)Require(inv.TryFireRound(0,rifle.Id),"Could not fire rifle");
            Require(inv.SellTownEquipment(0,rifle.Id,3,out int full,out int paid) && full==900 && paid==450,"Used rifle cannot be sold");
            var restored=new InventorySystem(InventoryStateCodec.Decode(InventoryStateCodec.Encode(inv.Capture())));
            Require(restored.ActiveItem(0)?.Kind==ItemKind.Firearm && restored.ActiveItem(0)?.Type==0,"Selling rifle lost fallback pistol");
            Require(!restored.SellTownEquipment(0,3,3,out _,out _),"Fallback pistol can be sold");
        });
        Test("emergency_ammo_is_free_finite_and_deduplicated",()=>{
            var m=Model();At(m,2);var s=m.Capture();var ps=s.Players.ToArray();ps[0].Coins=0;
            var people=s.Town.Players.ToArray();people[0].ServiceSpent=300;var inv=new InventorySystem(s.Tackle!.Inventory);int gun=inv.ActiveItem(0)!.Value.Id;for(int i=0;i<7;i++)inv.TryFireRound(0,gun);
            var t=new TownState(true,people,s.Town.Zones);var fixture=new HookState(s.Steps,ps,s.Fish,0,s.Tiles,s.Driftwood,new TackleState(s.Tackle.Config,inv.Capture(),s.Tackle.Merchants),s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,s.Crabs,s.Boss,s.Grenades,s.Eldritch,t);
            m.Restore(Codec.Decode(Codec.Encode(fixture)));var click=new HookInput(0,0,town:new TownCommand(TownAction.EmergencyAmmo,1));Step(m,click);Require(m.Capture().Town.Players[0].EmergencyReserve==7,"Zero-gold player lacks fallback supply");
            Step(m,new HookInput(0,0,reload:true));for(int i=0;i<60;i++)Step(m);Require(m.Capture().Town.Players[0].EmergencyReserve==0 && m.Capture().Tackle!.Inventory.ActiveItem(0)!.Value.LoadedAmmo==7,"Emergency reload unlimited");
            m.Restore(Codec.Decode(Codec.Encode(m.Capture())));Step(m,click);Require(m.Capture().Town.Players[0].EmergencyReserve==0,"Repeated request regranted emergency ammunition");
        });
        Test("finale_waits_for_every_online_player_and_grants_scaled_finite_pools",()=>{
            foreach(int count in new[]{1,2,3,4})
            {
                var m=Model(count);PrepareFinale(m,true);
                if(count>1){MultiStep(m,new HookInput(0,0,town:new TownCommand(TownAction.ReadyBoss,1)));Require(m.Capture().Boss.Phase==BossPhase.Dormant,"Party started before everyone was ready");}
                var ready=Enumerable.Range(0,count).Select(i=>new HookInput(0,0,town:new TownCommand(TownAction.ReadyBoss,2))).ToArray();MultiStep(m,ready);var s=m.Capture();
                int rounds=count==1?168:count==2?126:112;Require(s.Boss.Phase==BossPhase.Emerge && s.Town.Attempt==1 && s.Town.Members==(1<<count)-1,"Ready party did not start one attempt");
                Require(s.Town.Players.All(p=>p.BoundReserve==rounds) && s.Players.All(p=>p.Coins==0),"Zero-gold attempt supply incorrect for "+count);m.Restore(Codec.Decode(Codec.Encode(s)));MultiStep(m,ready);
                Require(m.Capture().Town.Attempt==1 && m.Capture().Town.Players.All(p=>p.BoundReserve==rounds),"Ready retry duplicated supply");
            }
        });
        Test("attempt_loaded_rounds_are_reclaimed_on_exit_without_touching_paid_reserve",()=>{
            var m=Model();At(m,2);Step(m,new HookInput(0,0,town:new TownCommand(TownAction.BuyAmmo,1)));PrepareFinale(m);
            // Consume only the initial magazine; paid backpack rounds remain untouched.
            var s=m.Capture();var inv=new InventorySystem(s.Tackle!.Inventory);int gun=inv.ActiveItem(0)!.Value.Id;for(int i=0;i<7;i++)inv.TryFireRound(0,gun);
            m.Restore(new HookState(s.Steps,s.Players,s.Fish,s.Coins,s.Tiles,s.Driftwood,new TackleState(s.Tackle.Config,inv.Capture(),s.Tackle.Merchants),s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,s.Crabs,s.Boss,s.Grenades,s.Eldritch,s.Town));
            Step(m,new HookInput(0,0,town:new TownCommand(TownAction.ReadyBoss,2)));Step(m,new HookInput(0,0,reload:true));for(int i=0;i<60;i++)Step(m);
            Require(m.Capture().Town.Players[0].BoundLoaded==7 && m.Capture().Town.Players[0].BoundReserve==161,"Attempt pool was not used first");
            At(m,1);s=m.Capture();Require(s.Town.Players[0].BoundReserve==0 && s.Town.Players[0].BoundLoaded==0 && s.Tackle!.Inventory.ActiveItem(0)!.Value.LoadedAmmo==0,"Bound ammunition escaped arena");
            Require(s.Tackle!.Inventory.Balance(0,ItemKind.Ammunition)==14,"Paid ammunition was reclaimed");m.Restore(Codec.Decode(Codec.Encode(s)));
            for(int i=0;i<310;i++)Step(m);Require(m.Capture().Boss.Phase==BossPhase.Dormant && m.Capture().Tiles.Count(t=>TownLayout.Destructible(t.X,t.Z))==16,"Abandoned attempt did not reset for free");
        });
        Test("town_finale_reward_return_bridge_and_dock_finish_are_once_only",()=>{
            var m=Model();PrepareFinale(m);Step(m,new HookInput(0,0,town:new TownCommand(TownAction.ReadyBoss,1)));var s=m.Capture();var ps=s.Players.ToArray();
            ps[0].X=ps[0].HookX=TownLayout.ShootingPlatforms[1].X;ps[0].Z=ps[0].HookZ=TownLayout.ShootingPlatforms[1].Z;ps[0].Swimming=default;
            var b=s.Boss;var almostDead=new BossState(BossPhase.Combat,1,b.MaxHealth,b.ParticipantCount,phaseTicks:0,sequence:b.Sequence,limbs:b.Limbs,receipts:b.Receipts,idleTicks:90,inkCooldownTicks:360,townArena:true);
            m.Restore(new HookState(s.Steps,ps,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,s.Crabs,almostDead,s.Grenades,s.Eldritch,s.Town));
            var eye=BossGeometry.Proxies(almostDead).Single(v=>v.Eye);int dx=eye.X-ps[0].X,dy=eye.Y-BossGeometry.MuzzleHeight(ps[0]),dz=eye.Z-ps[0].Z,len=HookGame.Sqrt((long)dx*dx+(long)dy*dy+(long)dz*dz);
            Step(m,new HookInput(0,0,eye.X,eye.Z,fire:true,aimVertical:(short)(dy*1000/len)));Require(m.Capture().Boss.Phase==BossPhase.Dying,"Pistol from center stone platform cannot hit boss body");
            for(int i=0;i<90;i++)Step(m);m.Restore(Codec.Decode(Codec.Encode(m.Capture())));for(int i=0;i<100;i++)Step(m);s=m.Capture();
            Require(s.Boss.Rewarded && s.Town.FinaleCompleted && !s.Won && s.Players[0].Coins==900,"Victory/reward ended exploration or failed to settle");
            Require(TownLayout.ReturnBridgeTiles().All(t=>s.Tiles.Any(v=>v.X==t.X && v.Z==t.Z)),"Free return bridge missing");
            m.Restore(Codec.Decode(Codec.Encode(s)));for(int i=0;i<20;i++)Step(m);Require(m.Capture().Players[0].Coins==900,"Restored victory paid twice");
            At(m,1);Step(m,new HookInput(0,0,town:new TownCommand(TownAction.ReturnToHarbor,2)));Require(m.Capture().Won && m.Capture().Town.Finished,"Dock confirmation did not finish the round");m.Restore(Codec.Decode(Codec.Encode(m.Capture())));
        });
        Test("zero_gold_finale_wipe_recovers_at_beacon_and_allows_a_new_attempt",()=>{
            var m=Model();PrepareFinale(m,true);Step(m,new HookInput(0,0,town:new TownCommand(TownAction.ReadyBoss,1)));var s=m.Capture();var ps=s.Players.ToArray();ps[0].Vitals.Health=0;m.Restore(Copy(s,ps));Step(m);
            Require(!m.Capture().GameOver,"Finale wipe became a permanent zero-gold game over");
            for(int i=0;i<305;i++)Step(m);s=m.Capture();Require(s.Boss.Phase==BossPhase.Dormant && s.Players[0].Vitals.Health>0 && s.Players[0].Coins==0,"Free finale reset failed to return player");
            m.Restore(Codec.Decode(Codec.Encode(s)));for(int i=0;i<15;i++)Step(m);Step(m,new HookInput(0,0,town:new TownCommand(TownAction.ReadyBoss,2)));
            Require(m.Capture().Town.Attempt==2 && m.Capture().Town.Players[0].BoundReserve==168,"Zero-gold second attempt lacks finite supply");
        });
        return failed+RunBoundaries()+(network?TownNetworkTests.Run():0);
    }
}
