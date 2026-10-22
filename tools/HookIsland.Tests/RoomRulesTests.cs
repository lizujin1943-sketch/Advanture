using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Advanture.HookIsland.Simulation;
using Advanture.HookIsland.Network;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IslandMap RulesMap()
    {
        var map=IslandMap.CreatePlatform();
        return new IslandMap(map.Shore,map.Pier,map.Spawns,map.FishSpots,map.Platform,
            new FishingConfig(enemies:EnemyConfig.Disabled));
    }
    // Canonical wire bytes keep these behavioral tests runnable against the pre-feature decoder.
    private static byte[] RuleBytes(bool enabled,uint request,HookInput input=default)
    {
        int offset=40+InventoryCommand.ByteCount+RevivalCommand.ByteCount;
        var bytes=new byte[HookInput.ByteCount];input.Encode().CopyTo(bytes,0);
        bytes[offset]=enabled?(byte)2:(byte)1;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset+1),request);return bytes;
    }
    private static IEnumerable<(string,Action)> RoomRulesCases()
    {
        yield return ("room_rules_buffer_retries_until_confirmed_and_resets",()=>{
            var buffer=new RoomRulesRequestBuffer();var state=new RoomRulesState(true,4);
            Require(buffer.TryQueue(state,false) && buffer.Pending,"Rule click was not queued.");
            Require(!buffer.TryQueue(state,true),"Pending click was overwritten.");
            var input=new HookInput(200,0,-3000,5000,true,inventory:new InventoryCommand(InventoryAction.Pickup,target:1,requestId:3));
            var first=buffer.Compose(input);var repeated=buffer.Compose(input);
            Require(first.Encode().SequenceEqual(repeated.Encode()) && first.RoomRules.RequestId==5 && !first.RoomRules.Enabled,
                "Retransmission allocated a new request or changed the desired value.");
            Require(first.Encode().Take(40+InventoryCommand.ByteCount+RevivalCommand.ByteCount).SequenceEqual(input.Encode().Take(40+InventoryCommand.ByteCount+RevivalCommand.ByteCount)) &&
                first.Treasure.Encode().SequenceEqual(input.Treasure.Encode()),
                "Room management consumed another gameplay action.");
            buffer.Observe(state);Require(buffer.Pending,"Unconfirmed request was dropped.");
            buffer.Observe(new RoomRulesState(false,5));Require(!buffer.Pending && buffer.Compose(input).RoomRules.IsEmpty,"Acknowledged request kept sending.");
            Require(!buffer.TryQueue(new RoomRulesState(false,5),false),"Unchanged policy queued an unnecessary request.");
            Require(!buffer.TryQueue(new RoomRulesState(false,uint.MaxValue),true),"Request counter wrapped.");
            Require(buffer.TryQueue(new RoomRulesState(false,5),true),"Next click failed.");buffer.Reset();Require(!buffer.Pending,"Reset retained old intent.");
        });
        yield return ("room_rules_trade_and_build_composers_keep_policy_command",()=>{
            var input=new HookInput(0,0,roomRules:new RoomRulesCommand(false,1));
            var rods=new RodRequestBuffer();rods.TryQueue(0,RodAction.Equip,1,0);
            Require(rods.Compose(input).RoomRules.RequestId==1,"Rod composer swallowed room policy.");
            var build=new PlatformBuildRequestBuffer();build.TryQueue(0,TileKind.Wood,2,0);
            Require(build.Compose(input,true).RoomRules.RequestId==1,"Build composer swallowed room policy.");
        });
        yield return ("room_rules_off_blocks_land_hook",()=>{
            var model=PullModel(RulesMap(),2);var start=model.Capture().Players[0];
            ShopStep(model,RuleBytes(false,1),new HookInput(0,0,start.X,start.Z,true).Encode());
            for(int i=0;i<60;i++)ShopStep(model);
            var s=model.Capture();
            Require(s.Players[1].PlayerHooks==0 && s.Players[0].X==start.X && s.Players[0].Z==start.Z,
                "Disabled friendly pranks still hooked or moved a land teammate.");
        });
        yield return ("room_rules_off_releases_active_pull_before_movement",()=>{
            var model=PullModel(RulesMap(),2);var start=model.Capture().Players[0];
            ShopStep(model,new HookInput().Encode(),new HookInput(0,0,start.X,start.Z,true).Encode());
            for(int i=0;i<30 && model.Capture().Players[1].HookedPlayer<0;i++)ShopStep(model);
            var before=model.Capture().Players[0];Require(before.PulledBy==1,"No active pull to disable.");
            ShopStep(model,RuleBytes(false,1));
            var s=model.Capture();Require(s.Players[0].PulledBy<0 && s.Players[1].HookedPlayer<0 &&
                s.Players[0].X==before.X && s.Players[0].Z==before.Z,"Closing pranks did not release before this tick's movement.");
        });
        yield return ("room_rules_guest_cannot_change_setting",()=>{
            var model=PullModel(RulesMap(),2);var target=model.Capture().Players[1];
            ShopStep(model,new HookInput(0,0,target.X,target.Z,true).Encode(),RuleBytes(false,999));
            for(int i=0;i<60;i++)ShopStep(model);
            Require(model.Capture().Players[0].PlayerHooks==1,"A guest disabled the host's friendly-prank rule.");
        });
        yield return ("room_rules_off_blocks_inflight_hook",()=>{
            var model=PullModel(RulesMap(),2);var target=model.Capture().Players[0];
            ShopStep(model,new HookInput().Encode(),new HookInput(0,0,target.X,target.Z,true).Encode());
            Require(model.Capture().Players[1].Phase==HookPhase.Flying,"Hook was not in flight.");
            ShopStep(model,RuleBytes(false,1));for(int i=0;i<60;i++)ShopStep(model);
            Require(model.Capture().Players[1].PlayerHooks==0,"An in-flight hook ignored the updated rule.");
        });
        yield return ("room_rules_reenable_restores_land_hook",()=>{
            var model=PullModel(RulesMap(),2);var target=model.Capture().Players[1];
            ShopStep(model,RuleBytes(false,1));ShopStep(model,RuleBytes(true,2,new HookInput(0,0,target.X,target.Z,true)));
            for(int i=0;i<60;i++)ShopStep(model);
            Require(model.Capture().Players[0].PlayerHooks==1,"Re-enabled pranks did not restore land hooking.");
        });
        yield return ("room_rules_water_rescue_survives_disable_and_recent_protection",()=>{
            foreach(bool downed in new[]{false,true})foreach(bool closeDuringPull in new[]{false,true})
            {
                var model=PullModel(RulesMap(),2);var initial=model.Capture();var players=initial.Players.ToArray();
                players[0].Z=-3600;players[0].Swimming.InWater=true;players[0].Stamina=0;players[0].RegenDelay=60;
                players[0].ProtectionTicks=50;if(downed)players[0].Vitals.Health=0;
                model.Restore(new HookState(initial.Steps,players,initial.Fish,initial.Coins,initial.Tiles,initial.Driftwood,initial.Tackle,initial.Enemies));
                var target=players[0];
                ShopStep(model,closeDuringPull?new HookInput().Encode():RuleBytes(false,1),new HookInput(0,0,target.X,target.Z,true).Encode());
                for(int i=0;i<30 && model.Capture().Players[1].HookedPlayer<0;i++)ShopStep(model);
                Require(model.Capture().Players[0].PulledBy==1,"Disabled pranks prevented a zero-stamina/protected/downed swimmer's rescue.");
                if(closeDuringPull)ShopStep(model,RuleBytes(false,1));
                bool landed=false;
                for(int i=0;i<60;i++)
                {
                    var state=model.Capture();var swimmer=state.Players[0];
                    if(!swimmer.Swimming.InWater){landed=true;Require(swimmer.PulledBy<0,"Rescue kept dragging a teammate after landing.");break;}
                    ShopStep(model);
                }
                Require(landed && model.Capture().Players[1].PlayerHooks==1,"Rescue was released before reaching land.");
            }
        });
        yield return ("room_rules_water_to_water_pull_stops_at_first_platform_landing",()=>{
            var map=RulesMap();var model=PullModel(map,2);var initial=model.Capture();var players=initial.Players.ToArray();
            players[0].X=-3700;players[0].Z=-1800;players[0].Swimming.InWater=true;players[0].Stamina=0;players[0].RegenDelay=60;
            players[1].X=-1800;players[1].Z=-3700;players[1].Swimming.InWater=true;players[1].Coins=players[1].CoinsEarned=10000;
            var inventory=new InventorySystem(initial.Tackle!.Inventory);
            Require(inventory.Purchase(1,ItemKind.SwimRing,1,true,false,new Point(-2600,0),ref players[1].Coins)==InventoryResult.Success,
                "Could not equip the swimming rescuer.");
            var tackle=new TackleState(initial.Tackle.Config,inventory.Capture(),initial.Tackle.Stock,initial.Tackle.Batch,initial.Tackle.NextRestock);
            model.Restore(new HookState(initial.Steps,players,initial.Fish,players.Sum(p=>p.Coins),initial.Tiles,initial.Driftwood,tackle,initial.Enemies));
            // Both endpoints are water; the segment crosses only a narrow corner of the initial platform.
            ShopStep(model,RuleBytes(false,1),new HookInput(0,0,players[0].X,players[0].Z,true).Encode());
            bool landed=false;Point landing=default;
            for(int i=0;i<30;i++)
            {
                ShopStep(model);var p=model.Capture().Players[0];
                if(!p.Swimming.InWater && !landed){landed=true;landing=new Point(p.X,p.Z);}
                if(landed)Require(!p.Swimming.InWater && p.PulledBy<0 && p.X==landing.X && p.Z==landing.Z,
                    "A disabled-room rescue kept moving after the first landing.");
            }
            Require(model.Capture().Players[1].PlayerHooks==1 && landed,
                "A water-to-water pull crossed the platform corner without stopping on land.");
        });
        yield return ("room_rules_repeated_and_stale_commands_cannot_overwrite_confirmed_value",()=>{
            var model=PullModel(RulesMap(),2);ShopStep(model,RuleBytes(false,7));
            ShopStep(model,RuleBytes(true,7),RuleBytes(true,uint.MaxValue));ShopStep(model,RuleBytes(true,6));
            var s=model.Capture();Require(!s.RoomRules.FriendlyPranksEnabled && s.RoomRules.LastRequest==7,"Repeated, old or guest request replaced host policy.");
            var codec=new HookGame();var restored=PullModel(RulesMap(),2);restored.Restore(codec.Decode(codec.Encode(s)));
            ShopStep(restored,RuleBytes(true,6));Require(!restored.Capture().RoomRules.FriendlyPranksEnabled,"Restoring lost the request receipt.");
            ShopStep(restored,RuleBytes(true,8));Require(restored.Capture().RoomRules.FriendlyPranksEnabled && restored.Capture().RoomRules.LastRequest==8,"Fresh host request was blocked by a forged guest sequence.");
        });
        yield return ("room_rules_incapacitated_host_can_manage_room",()=>{
            var model=PullModel(RulesMap(),2);var s=model.Capture();var players=s.Players.ToArray();players[0].Vitals.Health=0;
            model.Restore(new HookState(s.Steps,players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies));
            ShopStep(model,RuleBytes(false,1));Require(!model.Capture().RoomRules.FriendlyPranksEnabled,"Downed host could not manage room rules.");
        });
        yield return ("room_rules_codec_rejects_malformed_commands_and_land_pull_snapshot",()=>{
            int offset=40+InventoryCommand.ByteCount+RevivalCommand.ByteCount;
            foreach(var mutate in new Action<byte[]>[]{b=>b[offset]=3,b=>b[offset]=0,b=>Array.Clear(b,offset+1,4)})
            {
                var b=RuleBytes(false,1);mutate(b);bool rejected=false;
                try{HookInput.Decode(b);}catch(InvalidDataException){rejected=true;}
                Require(rejected,"Noncanonical room command was accepted.");
            }
            var model=PullModel(RulesMap(),2);var target=model.Capture().Players[0];
            ShopStep(model,new HookInput().Encode(),new HookInput(0,0,target.X,target.Z,true).Encode());
            for(int i=0;i<30 && model.Capture().Players[1].HookedPlayer<0;i++)ShopStep(model);
            var state=model.Capture();Require(state.Players[1].HookedPlayer==0,"Missing active pull fixture.");
            var invalid=new HookState(state.Steps,state.Players,state.Fish,state.Coins,state.Tiles,state.Driftwood,state.Tackle,state.Enemies,new RoomRulesState(false,1));
            bool bad=false;try{new HookGame().Decode(new HookGame().Encode(invalid));}catch(InvalidDataException){bad=true;}
            Require(bad,"Disabled-room checkpoint accepted a land pull.");
            var bytes=new HookGame().Encode(state);bytes[bytes.Length-5]=2;bad=false;
            try{new HookGame().Decode(bytes);}catch(InvalidDataException){bad=true;}Require(bad,"Invalid policy flag accepted.");
        });
    }
}
