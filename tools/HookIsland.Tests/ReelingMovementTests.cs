using System;
using System.Collections.Generic;
using System.Linq;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland.Tests;
internal static partial class Program
{
    private static IEnumerable<(string,Action)> ReelingMovementCases()
    {
        foreach(bool piranha in new[]{false,true})yield return ($"reeling_move_single_{(piranha?"piranha":"fish")}_at_thirty_percent",()=>{
            var model=CoopModel(1,piranha);var before=model.Capture().Players[0];
            for(int i=0;i<30;i++)ShopStep(model,new HookInput(0,1000).Encode());
            var after=model.Capture().Players[0];
            Require(after.Z-before.Z==900 && after.X==before.X,"Reeling must move 0.9 m in one second at the normal 3 m/s speed.");
            Require(CooperativeFishing.Attached(after) && after.Stamina<before.Stamina,"Walking must keep reeling and paying the fish stamina cost.");
        });
        foreach(bool piranha in new[]{false,true})yield return ($"reeling_move_all_four_{(piranha?"piranha":"fish")}_anglers",()=>{
            var model=CoopModel(4,piranha);
            for(int i=1;i<4;i++)CoopJoin(model,i,new Point(-2600,i*700));
            var before=model.Capture();var commands=Enumerable.Range(0,4).Select(_=>new HookInput(0,1000).Encode()).ToArray();
            for(int i=0;i<5;i++)ShopStep(model,commands);
            var after=model.Capture();
            for(int i=0;i<4;i++)Require(after.Players[i].Z-before.Players[i].Z==150 && CooperativeFishing.Attached(after.Players[i]),"Lead and every assistant must move at 30% without breaking their lines.");
        });
        yield return ("reeling_move_scales_configured_speed_and_restores_fractional_steps",()=>{
            var model=CoopModel(1,character:new CharacterConfig(moveSpeed:3110));
            for(int i=0;i<7;i++)ShopStep(model,new HookInput(707,707).Encode());
            var codec=new HookGame();var restored=CoopModel(1,character:new CharacterConfig(moveSpeed:3110));
            restored.Restore(codec.Decode(codec.Encode(model.Capture())));
            for(int i=7;i<30;i++)
            {var input=new HookInput(707,707).Encode();ShopStep(model,input);ShopStep(restored,input);}
            var p=model.Capture().Players[0];
            Require(p.X==-1941 && p.Z==659,"30% of 3.11 m/s with 0.707 diagonal input must travel 659 mm on each axis in one second.");
            Require(codec.Encode(model.Capture()).SequenceEqual(codec.Encode(restored.Capture())),"Restored slow movement lost fractional displacement or changed the catch.");
        });
        yield return ("reeling_move_cancel_locks_empty_hook_then_restores_normal_speed",()=>{
            var model=CoopModel(1);ShopStep(model,new HookInput(0,1000).Encode());var before=model.Capture().Players[0];
            ShopStep(model,new HookInput(0,1000,cancel:true).Encode());
            for(int i=0;i<100 && model.Capture().Players[0].Phase!=HookPhase.Ready;i++)ShopStep(model,new HookInput(0,1000).Encode());
            var returned=model.Capture().Players[0];
            Require(returned.Phase==HookPhase.Ready && returned.Z==before.Z,"Cancel must stop slow movement until the empty hook comes home.");
            ShopStep(model,new HookInput(0,1000).Encode());
            Require(model.Capture().Players[0].Z-returned.Z==100,"A returned hook must restore normal 3 m/s movement.");
        });
        yield return ("reeling_move_still_detaches_when_walking_beyond_line_length",()=>{
            var model=CoopModel(1);CoopEdit(model,p=>p[0].X=-1210);
            ShopStep(model,new HookInput(1000,0).Encode());var after=model.Capture();
            Require(after.Players[0].X==-1180 && after.Players[0].FishId==-1 && after.Fish[0].Owner==-1,"Walking beyond the 4 m rod length must detach on the same frame.");
        });
    }
}
