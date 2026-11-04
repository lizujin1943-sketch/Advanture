using System;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.GrenadeTests;
internal static partial class Program
{
    static partial void RunSimulationTests()
    {
        Test("damage_falloff_range_and_scatter",()=>{
            Require(GrenadeRules.DamageAt(0)==120 && GrenadeRules.DamageAt(1500)==120 && GrenadeRules.DamageAt(3000)==70 && GrenadeRules.DamageAt(4500)==20 && GrenadeRules.DamageAt(4501)==0,"Wrong falloff");
            var m=Model();var s=m.Capture();
            Require(GrenadeRules.TryPlan(Map(),s.Tiles,-1400,-1400,-1400,-20000,out _)==GrenadeResult.OutOfRange,"Exceeded max range");
            Require(GrenadeRules.TryPlan(Map(),s.Tiles,-1400,-1400,-1400,-1500,out _)==GrenadeResult.OutOfRange,"Too short accepted");
            var merchant=MerchantRules.Position(Map().Fishing!,0);
            Require(GrenadeRules.TryPlan(Map(),s.Tiles,-1400,-1400,merchant.X,merchant.Z,out _)==GrenadeResult.Blocked,"Merchant did not obstruct");
            for(uint i=1;i<1000;i++){var a=GrenadeRules.Scatter(773,0,i);var b=GrenadeRules.Scatter(773,0,i);Require(a.X==b.X && a.Z==b.Z && (long)a.X*a.X+(long)a.Z*a.Z<=350L*350,"Scatter is unstable/outside disk");}
        });
        Test("invalid_request_receipt_never_consumes_or_silently_retries",()=>{
            var m=Model();Step(m,Select());var bad=Throw(m.Capture(),-1400,-30000);Step(m,bad);
            Require(m.Capture().Players[0].Grenade.Result==GrenadeResult.OutOfRange && m.Capture().Grenades.Count==0,"No failure receipt");
            Step(m,Throw(m.Capture(),1400,-1400));Wait(m,12);Require(m.Capture().Grenades.Count==0 && m.Capture().Tackle!.Inventory.Equipped(0,EquipmentSlot.Throwable).HasValue,"Failed ID retried with new target");
        });
        Test("fixed_out_of_hand_frame_and_atomic_consumption",()=>{
            var m=Model();int previous=m.Capture().Tackle!.Inventory.ActiveItem(0)!.Value.Id;Step(m,Select());var request=Throw(m.Capture(),1400,-1400);Step(m,request);
            Require(m.Capture().Players[0].Grenade.Result==GrenadeResult.Pending && m.Capture().Grenades.Count==0,"No windup");
            for(int n=1;n<6;n++){Step(m,request);Require(m.Capture().Tackle!.Inventory.Equipped(0,EquipmentSlot.Throwable).HasValue,"Spent before release");}
            var e=Step(m,request);var s=m.Capture();var g=s.Grenades.Single();
            Require(g.Age==0 && g.SpawnTick==s.Steps && g.X==g.StartX && g.Y==g.StartY,"Spawn frame moved");
            Require(!s.Tackle!.Inventory.Equipped(0,EquipmentSlot.Throwable).HasValue && s.Players[0].Grenade.Sequence==1 && e.Count(v=>v.Kind==HookGameplayEventKind.GrenadeThrown)==1,"Not atomic");
            Wait(m,3);Require(m.Capture().Tackle!.Inventory.ActiveItem(0)?.Id==previous,"Did not return prior gun");
            for(int i=0;i<80;i++)Step(m,request);Require(m.Capture().Grenades.Count==0 && m.Capture().Players[0].Grenade.Sequence==1,"Repeated throw duplicated");
        });
        Test("landing_starts_exact_24_tick_fuse_and_friend_self_damage",()=>{
            var m=Model();Step(m,Select());Step(m,Throw(m.Capture(),1400,-1400));Wait(m,6);
            while(m.Capture().Grenades.Single().Phase!=GrenadePhase.Landed)Step(m);
            var landed=m.Capture();var g=landed.Grenades.Single();Require(g.LandedTick==landed.Steps && landed.Players[1].Vitals.Health==100,"Damaged before blast");
            for(int i=0;i<23;i++){var e=Step(m);Require(!e.Any(v=>v.Kind==HookGameplayEventKind.GrenadeExploded),"Fuse early");}
            var events=Step(m);var final=m.Capture();Require(final.Steps==g.LandedTick+24 && final.Grenades.Count==0 && events.Count(e=>e.Kind==HookGameplayEventKind.GrenadeExploded)==1,"Wrong blast tick");
            Require(final.Players[0].Vitals.Health<100 && final.Players[1].Vitals.Downed,"No self/friendly damage");
            Require(events.Where(e=>e.Kind==HookGameplayEventKind.Damaged).GroupBy(e=>e.Slot).All(group=>group.Count()==1),"Target hit twice");
        });
        Test("cancel_before_release_keeps_item",()=>{
            var m=Model();Step(m,Select());Step(m,Throw(m.Capture(),1400,-1400));Step(m,new HookInput(0,0,cancel:true));Wait(m,12);
            Require(m.Capture().Players[0].Grenade.Result==GrenadeResult.Interrupted && m.Capture().Tackle!.Inventory.Equipped(0,EquipmentSlot.Throwable).HasValue && m.Capture().Grenades.Count==0,"Cancel consumed");
        });
        Test("deactivation_interrupts_windup_but_never_removes_released_grenade",()=>{
            foreach(int elapsed in new[]{2,6})
            {
                var m=Model();Step(m,Select());Step(m,Throw(m.Capture(),1400,-1400));Wait(m,elapsed);var s=m.Capture();
                // 模拟正式成员事件，不能用本机表现对象的销毁来决定投掷物生死。
                m.Step(new FrameSync.Abstractions.FrameContext(s.Steps+1,s.Steps+1,30,new[]{new FrameSync.Abstractions.MemberEvent(1,FrameSync.Abstractions.MemberEventKind.Deactivate,0,1)}),
                    s.Players.Select(p=>new FrameSync.Abstractions.SimulationInput(new FrameSync.Abstractions.Participant(p.Slot,new Guid(p.Slot+1,0,0,new byte[8])),new FrameSync.Abstractions.ByteString(default(HookInput).Encode()))).ToArray());
                var after=m.Capture();Require(after.Players[0].Unavailable,"Member not deactivated");
                if(elapsed<6)Require(after.Grenades.Count==0 && after.Players[0].Grenade.Result==GrenadeResult.Interrupted && after.Tackle!.Inventory.ActiveItem(0)?.Kind==ItemKind.Throwable,"Interrupted throw spent/switched item");
                else Require(after.Grenades.Count==1 && !after.Tackle!.Inventory.Equipped(0,EquipmentSlot.Throwable).HasValue,"Disconnect removed/refunded flight");
                int explosions=0;for(int i=0;i<100;i++)explosions+=Step(m).Count(e=>e.Kind==HookGameplayEventKind.GrenadeExploded);
                Require(explosions==(elapsed<6?0:1),"Wrong disconnected blast count");Codec.Decode(Codec.Encode(m.Capture()));
            }
        });
        Test("explosion_cover_and_corrupt_flight_snapshot_are_rejected",()=>{
            var map=Map();var merchant=MerchantRules.Position(map.Fishing!,0);
            Require(!GrenadeRules.Visible(map,merchant.X-1800,100,merchant.Z,merchant.X+1800,1480,merchant.Z),"Merchant did not block blast");
            Require(GrenadeRules.Visible(map,-1400,100,-1400,1400,1480,-1400),"Open blast blocked");
            var m=Model();Step(m,Select());Step(m,Throw(m.Capture(),1400,-1400));Wait(m,6);
            var bytes=Codec.Encode(m.Capture());bytes[bytes.Length-1]=2;Reject(()=>Codec.Decode(bytes));
        });
        Test("snapshots_during_windup_flight_landing_and_after_explosion_replay_identically",()=>{
            foreach(int delay in new[]{0,3,6,12,26,35,80})
            {
                var m=Model();Step(m,Select());var input=Throw(m.Capture(),1400,-1400);Step(m,input);Wait(m,delay);
                var cp=Codec.Decode(Codec.Encode(m.Capture()));var restored=Model();restored.Restore(cp);
                for(int frame=0;frame<85;frame++){Step(m,input);Step(restored,input);Require(Codec.Encode(m.Capture()).SequenceEqual(Codec.Encode(restored.Capture())),"Replay changed at delay "+delay);}
                Require(m.Capture().Players[0].Grenade.Sequence==1 && !m.Capture().Tackle!.Inventory.Equipped(0,EquipmentSlot.Throwable).HasValue,"Restore regranted item");
            }
        });
        Test("return_to_rod_refreshes_equipment_and_every_snapshot_is_valid",()=>{
            var baseMap=IslandMap.CreatePlatform();var map=new IslandMap(baseMap.Shore,baseMap.Pier,baseMap.Spawns,baseMap.FishSpots,baseMap.Platform,
                new FishingConfig(enemies:EnemyConfig.Disabled,firearms:new FirearmConfig(),fishingRodsEnabled:true));
            var m=Model(map);int rod=m.Capture().Players[0].EquippedRod;Require(rod>0,"Fixture needs rod");
            Step(m,Select());Step(m,Throw(m.Capture(),1400,-1400));
            for(int i=0;i<12;i++){Step(m);Codec.Decode(Codec.Encode(m.Capture()));}
            Require(m.Capture().Tackle!.Inventory.ActiveItem(0)?.Id==rod && m.Capture().Players[0].EquippedRod==rod,"Rod binding not restored");
        });
        Test("water_landing_preserves_fuse_and_ends_below_surface",()=>{
            var m=Model();Step(m,Select());Step(m,Throw(m.Capture(),-1400,-8000));Wait(m,6);
            while(m.Capture().Grenades.Single().Phase!=GrenadePhase.Landed)Step(m);
            Require(m.Capture().Grenades.Single().Water && m.Capture().Grenades.Single().Y== -300,"Not water landing");
            Codec.Decode(Codec.Encode(m.Capture()));
        });
        Test("boss_blast_hits_body_and_each_limb_at_most_once_without_eye_bonus",()=>{
            var basis=IslandMap.CreatePlatform();
            var map=new IslandMap(basis.Shore,basis.Pier,new[]{new Point(0,8000),new Point(-1500,16000),new Point(0,16000),new Point(1500,16000)},basis.FishSpots,basis.Platform,
                new FishingConfig(enemies:EnemyConfig.Disabled,firearms:new FirearmConfig(),boss:new BossConfig(),fishingRodsEnabled:false));
            var m=Model(map);Step(m,new HookInput(0,0,bossRequest:1));Wait(m,120);
            Require(m.Capture().Boss.Damageable,"Boss did not enter combat");
            Step(m,default,default,Select());
            Step(m,default,default,Throw(m.Capture(),BossConfig.CenterX,BossConfig.CenterZ,slot:2));Wait(m,6);
            Require(m.Capture().Grenades.Count==1,"Boss fixture did not release a grenade");
            var before=m.Capture().Boss;int blasts=0;
            for(int i=0;i<90 && blasts==0;i++)blasts+=Step(m).Count(e=>e.Kind==HookGameplayEventKind.GrenadeExploded);
            var after=m.Capture().Boss;
            // 本体与眼睛同属 -1，触手又各有多个关节代理；一颗雷不能将这些代理的伤害相加。
            Require(blasts==1 && before.Health-after.Health>0 && before.Health-after.Health<=120,"Body duplicated damage or used eye multiplier");
            Require(after.Limbs.Any(l=>l.Health<before.Limbs[l.Id].Health),"No limb was in the blast fixture");
            foreach(var limb in after.Limbs)Require(before.Limbs[limb.Id].Health-limb.Health<=120,"Limb joint proxies duplicated damage");
            Codec.Decode(Codec.Encode(m.Capture()));
        });
    }
}
