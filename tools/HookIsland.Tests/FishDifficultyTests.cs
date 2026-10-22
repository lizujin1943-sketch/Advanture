using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;
using FrameSync.Runtime;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static HookInput Intercept(Fisher p,Fish fish,Point home,int lead=0)
    {
        if(p.Phase!=HookPhase.Ready || p.Cooldown>0 || fish.Phase==FishPhase.Hooked)return default;
        int speed=HookUpgrades.SpeedPerTick(p.SpeedLevel),range=HookUpgrades.Range(p.RangeLevel);
        for(int frame=1;frame<=range/speed+1;frame++)
        {
            var future=FishMotion.Predict(fish,home,frame+lead);
            int distance=HookGame.Sqrt(IslandMap.DistanceSquared(p.X,p.Z,future.X,future.Z));
            if(future.Phase==FishPhase.Jumping && future.Y>=400 && distance<range-200 && Math.Abs(distance-frame*speed)<250)
                return new HookInput(0,0,future.X,future.Z,true);
        }
        return default;
    }
    private static IEnumerable<(string,Action)> FishDifficultyCases()
    {
        yield return ("fish_swims_underwater_before_leaping",()=>{
            var r=Runner();Point? start=null;int travelled=0,submergedFrames=0;
            for(int i=0;i<300;i++)
            {
                Step(r);var f=r.InspectState().Fish[0];
                if(f.Phase==FishPhase.Jumping)break;
                if(f.Y>=0)continue;
                if(!start.HasValue)start=new Point(f.X,f.Z);
                travelled=HookGame.Sqrt(IslandMap.DistanceSquared(f.X,f.Z,start.Value.X,start.Value.Z));submergedFrames++;
            }
            Require(submergedFrames>=30 && travelled>=2000,"Fish must visibly travel underwater for at least a second before takeoff.");
        });
        yield return ("fish_leap_moves_forward_and_lands_continuously",()=>{
            var r=Runner();for(int i=0;i<300 && r.InspectState().Fish[0].Phase!=FishPhase.Jumping;i++)Step(r);
            var takeoff=r.InspectState().Fish[0];var previous=takeoff;int frames=0,peak=0;
            do
            {
                Step(r);var f=r.InspectState().Fish[0];frames++;peak=Math.Max(peak,f.Y);
                Require(IslandMap.DistanceSquared(f.X,f.Z,previous.X,previous.Z)<=180L*180,"Fish teleported between swim, jump or landing.");
                previous=f;
            }while(previous.Phase==FishPhase.Jumping && frames<150);
            Require(IslandMap.DistanceSquared(previous.X,previous.Z,takeoff.X,takeoff.Z)>=3000L*3000 && frames<=60,
                "Fish should cover at least three metres in a short forward leap.");
            Require(peak>=1500,"Leap should remain readable above the water.");
            for(int i=0;i<10;i++)Step(r);
            Require(r.InspectState().Fish[0].Y<0,"Fish did not dive below water after landing.");
        });
        yield return ("starter_hook_is_shorter_and_slower_with_meaningful_upgrades",()=>{
            var r=Runner();Step(r,new HookInput(0,0,-40000,-5000,true));
            var p=r.InspectState().Players[0];
            Require(p.Distance==300,"Starter outbound speed must be 9 m/s.");
            while(r.InspectState().Players[0].Phase==HookPhase.Flying)Step(r);
            p=r.InspectState().Players[0];Require(p.Distance==8000,"Starter hook must stop at eight metres.");
            int previous=p.HookX;Step(r);
            Require(r.InspectState().Players[0].HookX-previous==400,"Empty hook should return at 12 m/s.");
            Require(HookUpgrades.Range(1)==12000 && HookUpgrades.Range(2)==16000 && HookUpgrades.Range(3)==20000,
                "Range upgrades must unlock progressively more distant schools.");
        });
        yield return ("moving_fish_snapshot_restores_future_route_and_jump",()=>{
            var a=Runner();for(int i=0;i<100;i++)Step(a);
            var b=Runner();b.Restore(a.CaptureCheckpoint());
            for(int i=0;i<450;i++){Step(a);Step(b);}
            Require(CheckpointCodec.Hash(a.CaptureCheckpoint())==CheckpointCodec.Hash(b.CaptureCheckpoint()),
                "Restored swimming direction or jump phase diverged.");
        });
        yield return ("offshore_school_routes_preserve_equipment_gates",()=>{
            string file="samples/TacticalGame.Unity/Assets/HookIsland/Data/IslandMap.bytes";
            var root=new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while(root!=null && !System.IO.File.Exists(System.IO.Path.Combine(root.FullName,file)))root=root.Parent;
            Require(root!=null,"Could not locate generated production map.");
            var map=IslandMap.Decode(System.IO.File.ReadAllBytes(System.IO.Path.Combine(root!.FullName,file)));
            int[] counts=new int[3];
            foreach(var home in map.FishSpots.Select((point,id)=>(point,id)))
            {
                int tier=FishMotion.Tier(home.id),minimum=int.MaxValue;counts[tier]++;
                var f=FishMotion.Create(home.id,home.point,0);
                for(int i=0;i<300;i++)
                {
                    FishMotion.Advance(ref f,home.point);int distance=map.DistanceToLand(f.X,f.Z);
                    if(f.Phase==FishPhase.Jumping && f.Y>=300)minimum=Math.Min(minimum,distance);
                    Require(distance>=6000,"A swimming fish approached the island/pier too closely.");
                    if(tier>0)Require(distance-720>HookUpgrades.Range(tier-1),"A distant school entered the previous equipment tier's reach.");
                }
                Require(minimum+330<HookUpgrades.Range(tier),"A school cannot be reached after its intended range upgrade.");
            }
            Require(counts.All(c=>c==8),"Every difficulty tier must have eight schools.");
        });
        yield return ("distant_moving_fish_requires_range_upgrade_to_catch",()=>{
            var map=TestMap();map=new IslandMap(map.Shore,map.Pier,map.Spawns,new[]{new Point(-5000,-16000)});
            foreach(int level in new[]{0,1})
            {
                var model=PullModel(map,1);var s=model.Capture();var players=s.Players.ToArray();
                players[0].RangeLevel=level;players[0].Coins=level==0?0:50;
                model.Restore(new HookState(0,players,s.Fish,0));
                for(int i=0;i<700;i++)
                {
                    s=model.Capture();var input=Intercept(s.Players[0],s.Fish[0],map.FishSpots[0]);
                    // Also try blind casts with starter gear, which must never get a distant fish.
                    if(level==0 && s.Players[0].Phase==HookPhase.Ready)input=new HookInput(0,0,s.Fish[0].X,s.Fish[0].Z,true);
                    ShopStep(model,input.Encode());
                }
                Require(level==0?model.Capture().Players[0].Catches==0:model.Capture().Players[0].Catches>0,
                    "Range upgrade did not unlock a genuinely distant, moving fish.");
            }
        });
        yield return ("submerged_fish_cannot_be_hooked",()=>{
            var r=Runner();Step(r,new HookInput(0,0,-5000,-12000,true));
            for(int i=0;i<45;i++)Step(r);
            Require(r.InspectState().Players[0].Catches==0 && r.InspectState().Fish[0].Phase!=FishPhase.Hooked,
                "A hook caught a fish before it left the water.");
        });
    }
}
