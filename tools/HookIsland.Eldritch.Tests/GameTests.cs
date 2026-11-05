using System;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.EldritchTests;
internal static partial class Program
{
    static partial void RunGameTests()
    {
        Test("island_encounters_place_nine_roles_on_land_and_bell_in_water",()=>{
            foreach(var definition in EldritchCatalog.All)
            {
                var map=Map(new EldritchConfig(initialDelayTicks:1,staggerTicks:0,maxAlive:1,speciesMask:1<<(int)definition.Kind));
                var sys=new EldritchSystem(map);var tiles=PlatformRules.InitialTiles(map);
                sys.Advance(tiles,new[]{Player(0,0,-1500)},_=>{});
                var creature=sys.Get((int)definition.Kind);
                Require(creature.Phase==EldritchPhase.Emerging,"Missing island encounter: "+definition.Kind);
                bool onLand=PlatformRules.OnPlatform(tiles,creature.X,creature.Z);
                Require(onLand==(definition.Kind!=EldritchKind.DrownedBell),"Wrong encounter terrain: "+definition.Kind);
            }
        });
        Test("all_species_spawn_with_emergence_and_cap",()=>{
            var map=Map();var sys=new EldritchSystem(map);var players=new[]{Player(0,0,-1500)};
            var kinds=new System.Collections.Generic.HashSet<EldritchKind>();
            for(int tick=0;tick<1200;tick++)
            {
                sys.Advance(PlatformRules.InitialTiles(map),players,_=>{});
                foreach(var c in sys.Capture().Creatures.Where(c=>c.Damageable))kinds.Add(c.Kind);
            }
            Require(kinds.Count==10,"Not all species can enter formal terrain: "+string.Join(",",kinds));
            map=Map(new EldritchConfig(initialDelayTicks:1,staggerTicks:0,maxAlive:2));sys=new EldritchSystem(map);
            for(int tick=0;tick<120;tick++){sys.Advance(PlatformRules.InitialTiles(map),players,_=>{});Require(sys.Capture().Creatures.Take(10).Count(c=>c.Damageable)<=2,"Encounter cap exceeded");}
        });
        Test("no_reachable_target_releases_ground_slot",()=>{
            var c=Creature(EldritchKind.DeepOne,EldritchPhase.Hunting);var sys=System(c);var player=Player(0,12000,12000);player.Swimming.InWater=true;
            Advance(sys,new[]{player},301);Require(sys.Get(0).Phase==EldritchPhase.Dormant,"Unreachable enemy monopolized slot");
        });
        Test("blocked_ranged_enemy_moves_or_retires_without_occupying_slot_forever",()=>{
            var c=Creature(EldritchKind.RiftEye,EldritchPhase.Hunting);c.X=-3000;c.Z=1000;
            var sys=System(c);var player=Player(0,2000,1000);
            Require(Advance(sys,new[]{player},301).Count==0,"Beam passed through merchant cover");
            Require(sys.Get(c.Id).Phase==EldritchPhase.Dormant,"Blocked ranged enemy monopolized encounter slot");
            c.Z=-1700;player.Z=-1700;sys=System(c);
            Require(Advance(sys,new[]{player},100).Count>0 && sys.Get(c.Id).Damageable,"Visible target incorrectly retired ranged enemy");
        });
        Test("game_snapshots_restore_identically_mid_ability_and_minions",()=>{
            var map=Map();var a=Model(map);var b=Model(map);bool sawActive=false,sawMinion=false;
            // A snapshot test starts at the state boundary it promises to exercise;
            // random emergence on a crowded 3x3 deck is covered separately.
            var initial=a.Capture();var roster=initial.Eldritch.Creatures.ToArray();
            var poison=Creature(EldritchKind.BlightSac);poison.EffectX=-1800;roster[5]=poison;
            var child=Creature(EldritchKind.DeepOne,EldritchPhase.Emerging);child.Id=10;child.Health=30;child.Timer=45;child.AttackSerial=0;child.X=2300;child.Z=-1700;roster[10]=child;
            a.Restore(With(initial,new EldritchState(roster)));b.Restore(a.Capture());
            for(int tick=0;tick<750;tick++)
            {
                Step(a);if(tick%37==0)b.Restore(Codec.Decode(Codec.Encode(a.Capture())));else Step(b);
                var s=a.Capture();sawActive|=s.Eldritch.Creatures.Any(c=>c.Phase==EldritchPhase.Active);sawMinion|=s.Eldritch.Creatures.Any(c=>c.IsMinion && c.Damageable);
                Require(Codec.Encode(s).SequenceEqual(Codec.Encode(b.Capture())),"Rollback diverged at "+tick);
                Codec.Decode(Codec.Encode(s));
            }
            Require(sawActive && sawMinion,"Simulation did not exercise active attacks/summons");
        });
        Test("pending_reward_blocks_respawn_and_settles_once_after_restore",()=>{
            var map=Map(new EldritchConfig(initialDelayTicks:108000));var model=Model(map);var s=model.Capture();var roster=s.Eldritch.Creatures.ToArray();
            var c=Creature(EldritchKind.DeepOne);c.Phase=EldritchPhase.Dead;c.Health=0;c.Timer=map.Fishing!.Eldritch.RespawnTicks;c.Target=c.Ally=-1;c.RewardOwner=1;roster[0]=c;
            var pending=With(s,new EldritchState(roster));model.Restore(pending);
            Require(Step(model).Count(e=>e.Kind==HookGameplayEventKind.CoinsReceived)==1 && model.Capture().Players[0].Coins==18,"Missing reward transaction");
            var saved=Codec.Encode(model.Capture());model.Restore(Codec.Decode(saved));
            Require(!Step(model).Any(e=>e.Kind==HookGameplayEventKind.CoinsReceived) && model.Capture().Players[0].Coins==18,"Reward replayed");
            var p=pending.Players.ToArray();p[0].Coins=p[0].CoinsEarned=int.MaxValue;
            model.Restore(With(pending,pending.Eldritch,p,int.MaxValue));for(int i=0;i<40;i++)Step(model);
            var blocked=model.Capture().Eldritch.Creatures[0];Require(!blocked.Rewarded && blocked.Timer==c.Timer,"Overflow lost or respawned pending reward");
        });
        Test("codec_rejects_bad_roster_health_phase_and_receipt",()=>{
            var s=Model().Capture();var cs=s.Eldritch.Creatures.ToArray();cs[0].Id=1;Reject(()=>Codec.Decode(Codec.Encode(With(s,new EldritchState(cs)))));
            cs=s.Eldritch.Creatures.ToArray();cs[0]=Creature(EldritchKind.DeepOne);cs[0].Health=99999;Reject(()=>Codec.Decode(Codec.Encode(With(s,new EldritchState(cs)))));
            cs=s.Eldritch.Creatures.ToArray();cs[0].Rewarded=true;Reject(()=>Codec.Decode(Codec.Encode(With(s,new EldritchState(cs)))));
            cs=s.Eldritch.Creatures.ToArray();cs[0].Phase=(EldritchPhase)200;Reject(()=>Codec.Decode(Codec.Encode(With(s,new EldritchState(cs)))));
            Reject(()=>Codec.Decode(Codec.Encode(With(s,EldritchState.Empty))));
            var bytes=Codec.Encode(s);Reject(()=>Codec.Decode(bytes.Take(bytes.Length-1).ToArray()));
        });
        Test("ordinary_pistol_hits_and_interrupts_new_enemy",()=>{
            var map=Map(new EldritchConfig(initialDelayTicks:108000));var model=Model(map);
            Step(model,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.SelectWeapon,(int)EquipmentSlot.Sidearm,requestId:1)));
            for(int i=0;i<15;i++)Step(model);
            var s=model.Capture();var cs=s.Eldritch.Creatures.ToArray();var c=Creature(EldritchKind.RiftEye);c.X=-1400;c.Z=-3500;c.DirectionX=0;c.DirectionZ=1000000;c.Timer=50;c.Health=75;c.EffectX=-1400;c.EffectZ=0;cs[c.Id]=c;
            model.Restore(With(s,new EldritchState(cs)));
            var events=Step(model,new HookInput(0,0,-1400,-3500,fire:true));
            Require(events.Any(e=>e.Kind==HookGameplayEventKind.FirearmShot && e.TargetId==-8 && e.Amount>0),"Gun never targeted new enemy");
            Require(model.Capture().Eldritch.Creatures[c.Id].Phase==EldritchPhase.Recovering,"Gun did not interrupt spell");
        });
        Test("ordinary_pan_hits_new_enemy",()=>{
            var map=Map(new EldritchConfig(initialDelayTicks:108000));var model=Model(map);
            Step(model,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.SelectWeapon,(int)EquipmentSlot.MeleeWeapon,requestId:1)));
            var s=model.Capture();var cs=s.Eldritch.Creatures.ToArray();var c=Creature(EldritchKind.DeepOne,EldritchPhase.Recovering);c.X=-1400;c.Z=-2400;cs[0]=c;model.Restore(With(s,new EldritchState(cs)));
            Step(model,new HookInput(0,0,-1400,-2400,fire:true));for(int i=0;i<16;i++)Step(model);
            Require(model.Capture().Eldritch.Creatures[0].Health<65,"Pan did not damage new enemy");
        });
        Test("ordinary_grenade_damages_new_enemy_once",()=>{
            var model=Model(Map(new EldritchConfig(initialDelayTicks:108000)));
            Step(model,new HookInput(0,0,inventory:new InventoryCommand(InventoryAction.SelectWeapon,(int)EquipmentSlot.Throwable,requestId:1)));
            var s=model.Capture();var cs=s.Eldritch.Creatures.ToArray();var c=Creature(EldritchKind.ShellWarden,EldritchPhase.Recovering);c.X=1300;c.Z=-2100;cs[c.Id]=c;
            model.Restore(With(s,new EldritchState(cs)));
            var input=new HookInput(0,0,grenade:new GrenadeCommand(1,s.Tackle!.Inventory.Equipped(0,EquipmentSlot.Throwable)!.Value.Id,1400,-1400));
            int blasts=0;for(int i=0;i<80;i++)blasts+=Step(model,input).Count(e=>e.Kind==HookGameplayEventKind.GrenadeExploded);
            Require(blasts==1 && model.Capture().Eldritch.Creatures[c.Id].Health==60,"Grenade did not apply its one full blast to enemy");
            Codec.Decode(Codec.Encode(model.Capture()));
        });
        Test("ordinary_rod_interrupts_without_inflicting_damage",()=>{
            var b=IslandMap.CreatePlatform();var map=new IslandMap(b.Shore,b.Pier,b.Spawns,b.FishSpots,b.Platform,
                new FishingConfig(enemies:EnemyConfig.Disabled,melee:new MeleeConfig(),eldritch:new EldritchConfig(initialDelayTicks:108000)));
            var model=Model(map);var s=model.Capture();var cs=s.Eldritch.Creatures.ToArray();var c=Creature(EldritchKind.RiftEye);c.X=-1400;c.Z=-3400;c.Timer=54;cs[c.Id]=c;
            model.Restore(With(s,new EldritchState(cs)));Step(model,new HookInput(0,0,-1400,-3400,fire:true));
            for(int i=0;i<12;i++)Step(model);
            Require(model.Capture().Eldritch.Creatures[c.Id].Phase==EldritchPhase.Recovering && model.Capture().Eldritch.Creatures[c.Id].Health==75,"Rod failed to interrupt or inflicted damage");
        });
        Test("rod_cannot_interrupt_through_merchant_cover",()=>{
            var b=IslandMap.CreatePlatform();var map=new IslandMap(b.Shore,b.Pier,b.Spawns,b.FishSpots,b.Platform,
                new FishingConfig(enemies:EnemyConfig.Disabled,melee:new MeleeConfig(),eldritch:new EldritchConfig(initialDelayTicks:108000)));
            var model=Model(map);var s=model.Capture();var cs=s.Eldritch.Creatures.ToArray();var c=Creature(EldritchKind.RiftEye);c.X=2000;c.Z=1000;c.Timer=54;cs[c.Id]=c;
            var players=s.Players.ToArray();players[0].X=-2000;players[0].Z=1000;model.Restore(With(s,new EldritchState(cs),players));
            Step(model,new HookInput(0,0,2000,1000,fire:true));for(int i=0;i<24;i++)Step(model);
            Require(model.Capture().Eldritch.Creatures[c.Id].Phase==EldritchPhase.Windup,"Hook crossed merchant and interrupted sheltered spell");
        });
    }
}
