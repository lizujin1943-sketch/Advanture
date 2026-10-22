namespace Advanture.HookIsland.Simulation;

public sealed partial class HookGame
{
    private sealed partial class Model
    {
        private FinaleSystem? finale;
        private void InitializeFinale()=>finale=map.Fishing?.Finale.Enabled==true?new FinaleSystem(map.Fishing.Finale):null;
        private bool CanJoinFinale(Fisher p,FinaleState giant)
        {
            var rod=equipment?.Equipped(p);
            return giant.Phase==FinalePhase.Surfaced && FinaleRules.CanHold(p) && rod!=null &&
                IslandMap.DistanceSquared(p.X,p.Z,giant.X,giant.Z)<=(long)rod.Range*rod.Range;
        }
        private void JoinFinale(ref Fisher p)
        {
            var giant=finale!.Capture();
            if(!CanJoinFinale(p,giant))return;
            p.HookedFinale=true;p.FinaleDrainRemainder=0;p.HookX=giant.X;p.HookZ=giant.Z;
        }
        private void PruneFinale()
        {
            var giant=finale?.Capture();
            for(int i=0;i<players.Length;i++)
                if(players[i].HookedFinale && (giant==null || players[i].Phase!=HookPhase.Returning || !CanJoinFinale(players[i],giant)))
                    FinaleRules.ClearLine(ref players[i]);
        }
        private void AdvanceFinale()
        {
            if(finale==null)return;
            // Damage, rescue, exhaustion and member changes must invalidate lines before the last hold tick.
            PruneFinale();
            finale.Advance(map,tiles,players,commissions,tick+1);
            if(finale.Capture().Phase!=FinalePhase.Victory)return;
            for(int i=0;i<players.Length;i++)
            {
                var p=players[i];StopCharacterHook(ref p);players[i]=p;
            }
        }
    }
}
