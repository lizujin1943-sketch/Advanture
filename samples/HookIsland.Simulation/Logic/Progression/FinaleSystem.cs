using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed class FinaleSystem
{
    private readonly FinaleConfig config;
    private FinaleState state;
    public FinaleSystem(FinaleConfig config,FinaleState? state=null){this.config=config;this.state=state??FinaleState.Empty;}
    public FinaleState Capture()=>state;
    public void Advance(IslandMap map,IReadOnlyList<PlatformTile> tiles,Fisher[] players,CommissionState commissions,ulong now)
    {
        if(!config.Enabled || state.Phase==FinalePhase.Victory)return;
        if(state.Phase==FinalePhase.Dormant)
        {
            var commissionConfig=map.Fishing?.Commissions;
            if(commissionConfig?.Enabled==true && commissions.Completed==commissionConfig.Definitions.Count && commissions.Bait>=config.BaitCost)
                state=new FinaleState(FinalePhase.Summoning,timer:config.SummonTicks,spentBait:config.BaitCost);
            return;
        }
        if(state.Phase==FinalePhase.Summoning || state.Phase==FinalePhase.Submerged)
        {
            int remaining=state.Timer>0?state.Timer-1:0;
            if(remaining==0 && FinalePlacement.Find(map,config,tiles,players,state.Completed,new Point(state.X,state.Z),out var site))
                state=new FinaleState(FinalePhase.Surfaced,state.Completed,spentBait:state.SpentBait,x:site.Position.X,z:site.Position.Z,
                    previousX:state.X,previousZ:state.Z,direction:site.Direction);
            else state=Copy(timer:remaining);
            return;
        }
        for(int i=0;i<players.Length;i++)if(players[i].HookedFinale && !FinaleRules.CanHold(players[i]))FinaleRules.ClearLine(ref players[i]);
        int needed=FinaleRules.RequiredHooks(config,players),count=FinaleRules.Count(players);
        if(needed==0 || count<needed){state=Copy(progress:0);return;}
        for(int i=0;i<players.Length;i++)if(players[i].HookedFinale)
        {
            // UnitsPerPoint=30,000 at 30 Hz: milli-points/second is the per-tick fixed-point total.
            int drain=CooperativeFishing.Drain(config.StaminaMilliPerSecond,count,ref players[i].FinaleDrainRemainder);
            if(!StaminaSystem.Spend(ref players[i],drain))FinaleRules.ClearLine(ref players[i]);
        }
        if(FinaleRules.Count(players)<needed){state=Copy(progress:0);return;}
        int progress=state.Progress+1;
        if(progress<config.HoldTicks){state=Copy(progress:progress);return;}
        int completed=state.Completed+1;
        for(int i=0;i<players.Length;i++)FinaleRules.ClearLine(ref players[i]);
        state=new FinaleState(completed==3?FinalePhase.Victory:FinalePhase.Submerged,completed,
            timer:completed==3?0:config.RelocateTicks,spentBait:state.SpentBait,x:state.X,z:state.Z,
            previousX:state.PreviousX,previousZ:state.PreviousZ,direction:state.Direction,victoryTick:completed==3?now:0);
    }
    private FinaleState Copy(int? timer=null,int? progress=null)=>new FinaleState(state.Phase,state.Completed,timer??state.Timer,
        progress??state.Progress,state.SpentBait,state.X,state.Z,state.PreviousX,state.PreviousZ,state.Direction,state.VictoryTick);
}
