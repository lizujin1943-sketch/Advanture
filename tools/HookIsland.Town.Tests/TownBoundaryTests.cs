using System;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;

public static partial class TownGameplayTests
{
    static HookState With(HookState s,Fisher[]? players=null,TownState? town=null,EldritchState? eldritch=null,TackleState? tackle=null,BossState? boss=null,PlatformTile[]? tiles=null)
    {var ps=players??s.Players.ToArray();return new HookState(s.Steps,ps,s.Fish,ps.Sum(p=>p.Coins),tiles??s.Tiles.ToArray(),s.Driftwood,tackle??s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,s.Crabs,boss??s.Boss,s.Grenades,eldritch??s.Eldritch,town??s.Town);}
    static Point ZonePoint(int zone)
    {
        var a=TownLayout.EncounterAreas[zone];var map=IslandMap.CreateTown();
        for(int dz=0;dz<a.Depth/2;dz+=500)for(int dx=0;dx<a.Width/2;dx+=500)
            if(SwimmingTerrain.Ground(map,Array.Empty<PlatformTile>(),a.X+dx,a.Z+dz))return new Point(a.X+dx,a.Z+dz);
        throw new Exception("No walkable zone point "+zone);
    }
    static void Position(ref Fisher p,Point point)
    {p.X=p.HookX=point.X;p.Z=p.HookZ=point.Z;p.Swimming=default;p.Shopping=false;}
    static EldritchCreature Living(EldritchCreature c,bool paused=false)
    {
        var port=TownLayout.SpawnPorts.First(p=>p.ZoneId==TownEncounters.Zone(c.Id) && p.Water==(EldritchCatalog.Get(c.Kind).Habitat==EldritchHabitat.Water));
        c.Phase=EldritchPhase.Emerging;c.Generation=1;c.Timer=45;c.Health=50;c.X=port.X;c.Z=port.Z;c.DirectionX=1000000;c.Paused=paused;return c;
    }
    static IDeterministicSimulation<HookState> LoadedAttempt()
    {
        var m=Model(finite:true);PrepareFinale(m,true);Step(m,new HookInput(0,0,town:new TownCommand(TownAction.ReadyBoss,1)));
        Step(m,new HookInput(0,0,reload:true));for(int k=0;k<60;k++)Step(m);
        Require(m.Capture().Town.Players[0].BoundLoaded==7,"Fixture failed to load attempt magazine");return m;
    }
    static IDeterministicSimulation<HookState> LoadedTransferableAttempt()
    {
        var m=Model(finite:true);var s=m.Capture();var inv=new InventorySystem(s.Tackle!.Inventory);var ps=s.Players.ToArray();
        Require(inv.Purchase(0,ItemKind.Firearm,0,true,false,default,ref ps[0].Coins)==InventoryResult.Success,"Transferable pistol fixture purchase failed");
        int gun=inv.ActiveItem(0)!.Value.Id;for(int k=0;k<7;k++)inv.TryFireRound(0,gun);
        // Empty both weapons through the inventory's real firing ledger before the attempt.
        int starter=inv.Capture().Items.Single(i=>i.Kind==ItemKind.Firearm && i.Id!=gun).Id;
        inv.Equip(0,starter,false,default);for(int k=0;k<7;k++)inv.TryFireRound(0,starter);inv.Equip(0,gun,false,default);
        m.Restore(With(s,ps,tackle:new TackleState(s.Tackle.Config,inv.Capture(),s.Tackle.Merchants)));PrepareFinale(m);
        Step(m,new HookInput(0,0,town:new TownCommand(TownAction.ReadyBoss,1)));Step(m,new HookInput(0,0,reload:true));for(int k=0;k<60;k++)Step(m);
        Require(m.Capture().Town.Players[0].BoundLoaded==7,"Transferable weapon did not load bound rounds");return m;
    }
    public static int RunBoundaries()
    {
        int failed=0;
        void Test(string name,Action f){try{f();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
        Test("legacy_town_save_keeps_loaded_attempt_rounds_in_unlimited_mode",()=>{
            var saved=LoadedAttempt().Capture();
            var restored=Model();
            // Legacy towns used the default town config byte and could carry a bound magazine.
            var oldConfig=restored.Capture().Tackle!.Config;
            var oldTackle=new TackleState(oldConfig,saved.Tackle!.Inventory,saved.Tackle.Merchants);
            var bytes=Codec.Encode(With(saved,tackle:oldTackle));
            restored.Restore(Codec.Decode(bytes));
            Require(Codec.Encode(restored.Capture()).SequenceEqual(bytes),"Restoring changed the saved checkpoint before play resumed");
            Step(restored);
            var after=restored.Capture();
            var supply=after.Town.Players[0];
            Require(supply.BoundLoaded==0 && supply.BoundWeaponId==0 && supply.BoundReserve==0 && Reserve(after)==null,"Legacy supply still restricted unlimited reloads");
            restored.Restore(Codec.Decode(Codec.Encode(after)));
            At(restored,1);
            var inventory=restored.Capture().Tackle!.Inventory;
            Require(inventory.ActiveItem(0)!.Value.LoadedAmmo==7 && inventory.ConsumedAmmunition[0]==saved.Tackle.Inventory.ConsumedAmmunition[0],"Leaving the arena discarded rounds loaded in the old save");
        });
        Test("paused_adults_resume_only_with_global_slots_and_roundtrip",()=>{
            var m=Model(2);var s=m.Capture();var ps=s.Players.ToArray();Position(ref ps[0],ZonePoint(2));Position(ref ps[1],ZonePoint(3));
            var roster=s.Eldritch.Creatures.ToArray();for(int id=5;id<11;id++)roster[id]=Living(roster[id],id<8);
            var zones=s.Town.Zones.ToArray();zones[2]=new TownZoneState{Started=true,Paused=true,IdleTicks=360};zones[3]=new TownZoneState{Started=true};
            m.Restore(Codec.Decode(Codec.Encode(With(s,ps,new TownState(true,s.Town.Players,zones),new EldritchState(roster)))));MultiStep(m);s=m.Capture();
            Require(s.Eldritch.Creatures.Count(c=>c.TownAdult && c.Damageable)<=4,"Resume exceeded four global adults");
            Require(s.Eldritch.Creatures.Skip(5).Take(3).Count(c=>c.Paused)==2,"Resume did not wait individually for free slots");
            Require(s.Eldritch.Creatures.Skip(5).Take(3).All(c=>c.Health==50),"Resume changed retained health");m.Restore(Codec.Decode(Codec.Encode(s)));
            roster=s.Eldritch.Creatures.ToArray();roster[8].Health=0;roster[8].Phase=EldritchPhase.Dead;roster[8].Timer=2700;roster[8].Target=roster[8].Ally=-1;roster[8].RewardOwner=1;roster[8].Rewarded=true;
            m.Restore(With(s,eldritch:new EldritchState(roster)));MultiStep(m);s=m.Capture();
            Require(s.Eldritch.Creatures.Count(c=>c.TownAdult && c.Damageable)==4 && s.Eldritch.Creatures.Skip(5).Take(3).Count(c=>c.Paused)==1,"Freed slot did not resume exactly one waiting adult");m.Restore(Codec.Decode(Codec.Encode(s)));
        });
        Test("third_district_preserves_occupied_enemies_and_360_tick_grace",()=>{
            var m=Model(3);var s=m.Capture();var ps=s.Players.ToArray();Position(ref ps[0],ZonePoint(2));Position(ref ps[1],ZonePoint(3));Position(ref ps[2],ZonePoint(0));
            var zones=s.Town.Zones.ToArray();zones[2]=zones[3]=new TownZoneState{Started=true};var roster=s.Eldritch.Creatures.ToArray();roster[5]=Living(roster[5]);roster[8]=Living(roster[8]);
            m.Restore(With(s,ps,new TownState(true,s.Town.Players,zones),new EldritchState(roster)));MultiStep(m);s=m.Capture();
            Require(!s.Town.Zones[0].Started && !s.Town.Zones[2].Paused && !s.Town.Zones[3].Paused && !s.Eldritch.Creatures[8].Paused,"Third low-number district displaced an occupied encounter");
            ps=s.Players.ToArray();Position(ref ps[1],TownLayout.Buildings[0].Entrance);zones=s.Town.Zones.ToArray();zones[3].IdleTicks=358;
            m.Restore(With(s,ps,new TownState(true,s.Town.Players,zones)));MultiStep(m);s=m.Capture();
            Require(s.Town.Zones[3].IdleTicks==359 && !s.Eldritch.Creatures[8].Paused && !s.Town.Zones[0].Started,"Encounter paused before 360 absent ticks");
            MultiStep(m);s=m.Capture();Require(s.Town.Zones[3].Paused && s.Eldritch.Creatures[8].Paused && s.Town.Zones[0].Started,"Vacated slot was not released at 360 ticks");m.Restore(Codec.Decode(Codec.Encode(s)));
        });
        Test("district_queue_keeps_trigger_order_after_restore",()=>{
            var m=Model(4);var s=m.Capture();var ps=s.Players.ToArray();Position(ref ps[0],ZonePoint(2));Position(ref ps[1],ZonePoint(3));Position(ref ps[2],ZonePoint(1));Position(ref ps[3],TownLayout.Buildings[0].Entrance);
            var zones=s.Town.Zones.ToArray();zones[2]=zones[3]=new TownZoneState{Started=true};m.Restore(With(s,ps,new TownState(true,s.Town.Players,zones)));MultiStep(m);s=m.Capture();
            ps=s.Players.ToArray();Position(ref ps[3],ZonePoint(0));m.Restore(With(s,ps));MultiStep(m);m.Restore(Codec.Decode(Codec.Encode(m.Capture())));s=m.Capture();
            ps=s.Players.ToArray();Position(ref ps[0],TownLayout.Buildings[0].Entrance);zones=s.Town.Zones.ToArray();zones[2].IdleTicks=359;
            // Preserve the queued metadata when changing only the existing occupant's idle clock.
            var t=s.Town;m.Restore(With(s,ps,new TownState(true,t.Players,zones,t.Goals,t.Shortcuts,t.ReadyMask,t.FinishMask,t.MapKnownMask,t.ContractMask,t.ContractsPaidMask,t.Attempt,t.Members,t.FinaleCompleted,t.Finished)));MultiStep(m);s=m.Capture();
            Require(s.Town.Zones[1].Started && !s.Town.Zones[0].Started && !s.Town.Zones[3].Paused,"Lower id jumped an earlier trigger after save/restore");
        });
        Test("district_preview_inner_trigger_and_eight_meter_nonshopping_hold",()=>{
            var map=IslandMap.CreateTown();var a=TownLayout.EncounterAreas[0];var p=Model().Capture().Players[0];p.X=a.X+a.Width/2+6000;p.Z=a.Z;
            Require(TownEncounters.NearZone(0,p),"Six-meter outer combat position does not retain occupancy");
            p.Shopping=true;Require(!TownEncounters.NearZone(0,p),"Shopping character retains combat occupancy");
            var m=Model();var s=m.Capture();var ps=s.Players.ToArray();Point? preview=null;
            for(int z=a.Z-a.Depth/2-7500;z<=a.Z+a.Depth/2+7500 && preview==null;z+=500)
                for(int x=a.X-a.Width/2-7500;x<=a.X+a.Width/2+7500;x+=500)
                    if((Math.Abs(x-a.X)>a.Width/2+4500 || Math.Abs(z-a.Z)>a.Depth/2+4500) && SwimmingTerrain.Ground(map,s.Tiles,x,z)){preview=new Point(x,z);break;}
            Position(ref ps[0],preview??throw new Exception("No valid preview position"));m.Restore(With(s,ps));Step(m);
            Require(!m.Capture().Town.Zones[0].Started,"Preview band started an encounter before inner trigger");
        });
        foreach(var action in new[]{InventoryAction.Move,InventoryAction.BindQuickSlot,InventoryAction.Drop,InventoryAction.Unequip})
            Test("attempt_magazine_survives_inventory_"+action+"_and_replay",()=>{
                var m=LoadedAttempt();var before=m.Capture();int gun=before.Town.Players[0].BoundWeaponId;
                var c=action==InventoryAction.Move?new InventoryCommand(action,999999,requestId:1):action==InventoryAction.BindQuickSlot?new InventoryCommand(action,gun,amount:1,requestId:1):action==InventoryAction.Drop?new InventoryCommand(action,gun,amount:1,requestId:1):new InventoryCommand(action,(int)EquipmentSlot.Sidearm,requestId:1);
                Step(m,new HookInput(0,0,inventory:c));var after=m.Capture();
                Require(after.Town.Players[0].BoundLoaded==7 && after.Tackle!.Inventory.Items.Single(i=>i.Id==gun).LoadedAmmo==7 && after.Tackle.Inventory.ConsumedAmmunition[0]==before.Tackle!.Inventory.ConsumedAmmunition[0],"Inventory request destroyed attempt rounds");
                m.Restore(Codec.Decode(Codec.Encode(after)));Step(m,new HookInput(0,0,inventory:c));after=m.Capture();Require(after.Town.Players[0].BoundLoaded==7,"Repeated inventory receipt destroyed attempt rounds");
            });
        Test("successful_drop_reclaims_only_bound_source_and_duplicate_is_inert",()=>{
            var m=LoadedTransferableAttempt();var s=m.Capture();int gun=s.Town.Players[0].BoundWeaponId;
            Step(m,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Unequip,(int)EquipmentSlot.Sidearm,requestId:1)));
            Require(m.Capture().Town.Players[0].BoundLoaded==7,"Moving bound weapon to own backpack discarded source");
            s=m.Capture();int consumed=s.Tackle!.Inventory.ConsumedAmmunition[0];var drop=new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Drop,gun,1,requestId:2));Step(m,drop);s=m.Capture();
            Require(s.Players[0].LastInventoryResult==InventoryResult.Success && s.Tackle!.Inventory.Items.Single(i=>i.Id==gun).Location==ItemLocation.Ground && s.Tackle.Inventory.Items.Single(i=>i.Id==gun).LoadedAmmo==0 && s.Town.Players[0].BoundLoaded==0 && s.Town.Players[0].BoundWeaponId==0 && s.Tackle.Inventory.ConsumedAmmunition[0]==consumed+7,"Successful transfer did not atomically reclaim bound source");
            m.Restore(Codec.Decode(Codec.Encode(s)));Step(m,drop);Require(m.Capture().Tackle!.Inventory.ConsumedAmmunition[0]==consumed+7,"Duplicate successful drop consumed source twice");
        });
        Test("switching_basic_pistols_cannot_overwrite_a_loaded_attempt_source",()=>{
            var m=LoadedTransferableAttempt();var s=m.Capture();int first=s.Town.Players[0].BoundWeaponId;
            int second=s.Tackle!.Inventory.Items.Single(i=>i.Kind==ItemKind.Firearm && i.Id!=first).Id;
            Step(m,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Equip,second,requestId:1)));for(int k=0;k<15;k++)Step(m);
            Step(m,new HookInput(0,0,reload:true));for(int k=0;k<60;k++)Step(m);s=m.Capture();
            Require(s.Town.Players[0].BoundWeaponId==first && s.Town.Players[0].BoundLoaded==7 && s.Town.Players[0].BoundReserve==161 && s.Tackle!.Inventory.Items.Single(i=>i.Id==second).LoadedAmmo==0,"Second pistol overwrote the first loaded source");
            m.Restore(Codec.Decode(Codec.Encode(s)));
            Step(m,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.Drop,first,1,requestId:2)));Require(m.Capture().Town.Players[0].BoundLoaded==0,"Successful source transfer did not clear binding");
            Step(m,new HookInput(0,0,reload:true));for(int k=0;k<60;k++)Step(m);s=m.Capture();
            Require(s.Town.Players[0].BoundWeaponId==second && s.Town.Players[0].BoundLoaded==7 && s.Town.Players[0].BoundReserve==154,"Cleared source did not allow a new pistol binding");m.Restore(Codec.Decode(Codec.Encode(s)));
        });
        Test("harbor_discards_departed_confirmation_before_final_vote",()=>{
            var m=Model(2);PrepareFinale(m);MultiStep(m,new HookInput(0,0,town:new TownCommand(TownAction.ReadyBoss,1)),new HookInput(0,0,town:new TownCommand(TownAction.ReadyBoss,1)));var s=m.Capture();var b=s.Boss;
            var defeated=new BossState(BossPhase.Defeated,0,b.MaxHealth,2,1,true,sequence:b.Sequence,limbs:b.Limbs,receipts:b.Receipts,townArena:true);
            var people=s.Town.Players.ToArray();foreach(int i in Enumerable.Range(0,2)){people[i].BoundReserve=0;people[i].BoundLoaded=0;people[i].BoundWeaponId=0;}
            var t=s.Town;var town=new TownState(true,people,t.Zones,goals:7,attempt:t.Attempt,members:3,finaleCompleted:true);var ps=s.Players.ToArray();var dock=TownLayout.Buildings[0].Entrance;
            Position(ref ps[0],new Point(dock.X+2950,dock.Z));Position(ref ps[1],dock);
            m.Restore(Codec.Decode(Codec.Encode(With(s,ps,town,boss:defeated,tiles:s.Tiles.Concat(TownLayout.ReturnBridgeTiles()).ToArray()))));
            MultiStep(m,new HookInput(1000,0,town:new TownCommand(TownAction.ReturnToHarbor,2)));s=m.Capture();Require(s.Town.FinishMask==1 && IslandMap.DistanceSquared(s.Players[0].X,s.Players[0].Z,dock.X,dock.Z)>3000L*3000,"Fixture failed to leave harbor on confirmation frame");
            MultiStep(m,default,new HookInput(0,0,town:new TownCommand(TownAction.ReturnToHarbor,2)));s=m.Capture();Require(!s.Won && !s.Town.Finished && s.Town.FinishMask==2,"Stale departed vote ended exploration");
            ps=s.Players.ToArray();Position(ref ps[0],dock);m.Restore(With(s,ps));MultiStep(m,new HookInput(0,0,town:new TownCommand(TownAction.ReturnToHarbor,3)));Require(m.Capture().Won,"Fresh valid online votes did not finish");m.Restore(Codec.Decode(Codec.Encode(m.Capture())));
        });
        foreach(int balance in new[]{0,AmmunitionCatalog.Get(0).Price-1,AmmunitionCatalog.Get(0).Price})
            Test("emergency_ammo_affordability_boundary_"+balance,()=>{
                var m=Model(finite:true);At(m,2);var s=m.Capture();var ps=s.Players.ToArray();ps[0].Coins=balance;var people=s.Town.Players.ToArray();people[0].ServiceSpent=300-balance;
                var inv=new InventorySystem(s.Tackle!.Inventory);int gun=inv.ActiveItem(0)!.Value.Id;for(int k=0;k<7;k++)inv.TryFireRound(0,gun);
                m.Restore(Codec.Decode(Codec.Encode(With(s,ps,new TownState(true,people,s.Town.Zones),tackle:new TackleState(s.Tackle.Config,inv.Capture(),s.Tackle.Merchants)))));
                Step(m,new HookInput(0,0,town:new TownCommand(TownAction.EmergencyAmmo,1)));s=m.Capture();bool granted=balance<AmmunitionCatalog.Get(0).Price;
                Require(s.Town.Players[0].EmergencyReserve==(granted?7:0) && s.Players[0].Coins==balance,"Emergency grant used zero-only balance instead of configured price");m.Restore(Codec.Decode(Codec.Encode(s)));
            });
        return failed;
    }
}
