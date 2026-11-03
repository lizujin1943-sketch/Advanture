using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.FirearmTests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> UnlimitedAmmoCases()
    {
        yield return ("unlimited_reserve_empty_magazine_still_waits_full_reload_time",()=>{
            var model=FreshGame(1);Step(model,SelectSlot(6,1));Wait(model,9);
            var player=model.Capture().Players[0];var shot=new HookInput(0,0,player.X,player.Z-14000,fire:true);
            for(int cycle=0;cycle<3;cycle++)
            {
                for(int n=0;n<7;n++){Step(model,shot);Wait(model,18);}
                var events=Step(model,shot);
                Require(!events.Any(e=>e.Kind==HookGameplayEventKind.FirearmShot) && Gun(model).LoadedAmmo==0,"Empty magazine fired or refilled automatically");
                events=Step(model,new HookInput(0,0,reload:true));
                Require(model.Capture().Players[0].Firearm.ReloadTicks==54 && events.Count(e=>e.Kind==HookGameplayEventKind.FirearmReloadStarted)==1,"No-reserve reload did not start");
                Require(!IslandSaveSafety.IsSafe(model.Capture()),"Save accepted unfinished reload");
                Wait(model,53);Require(Gun(model).LoadedAmmo==0,"Unlimited supply skipped reload time");
                events=Step(model);
                Require(Gun(model).LoadedAmmo==7 && events.Single(e=>e.Kind==HookGameplayEventKind.FirearmReloaded).Amount==7,"Reload did not refill exactly once at completion");
                Require(Reserve(model)==0 && model.Capture().Players[0].Coins==0,"Unlimited reload required physical ammunition or money");
                var codec=new HookGame();var bytes=codec.Encode(model.Capture());model.Restore(codec.Decode(bytes));
                Require(codec.Encode(model.Capture()).SequenceEqual(bytes),"Restoring unlimited ammunition changed settled state");
                Require(!Step(model,new HookInput(0,0,reload:true)).Any(e=>e.Kind==HookGameplayEventKind.FirearmReloadStarted),"Full magazine started another reload");
            }
            Require(model.Capture().Players[0].Firearm.ShotSequence==21,"Repeated reload lost confirmed shot sequence");
        });
        yield return ("unlimited_reserve_cancelling_reload_does_not_refill_magazine",()=>{
            var model=FreshGame(1);Step(model,SelectSlot(6,1));Wait(model,9);var player=model.Capture().Players[0];
            Step(model,new HookInput(0,0,player.X,player.Z-14000,fire:true));Wait(model,6);
            Step(model,new HookInput(0,0,reload:true));Wait(model,53);
            Step(model,SelectSlot(1,2));Wait(model,60);
            Require(Gun(model).LoadedAmmo==6 && !model.Capture().Players[0].Firearm.Reloading,"Switching away completed a cancelled reload");
            var codec=new HookGame();codec.Decode(codec.Encode(model.Capture()));
        });
        yield return ("previous_save_is_rejected_without_overwriting_the_file",()=>{
            string path=Path.GetFullPath("tools/HookIsland.Firearm.Tests/Fixtures/firearms-v31.hisv");
            var original=File.ReadAllBytes(path);
            Reject(()=>new IslandSaveStore(path).Load());
            Require(original.SequenceEqual(File.ReadAllBytes(path)),"Rejecting an incompatible save overwrote the original file");
        });
    }
}
