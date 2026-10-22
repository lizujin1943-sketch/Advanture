using System;

namespace Advanture.HookIsland.Simulation;

public enum DriftwoodPhase : byte { Floating, Hooked, Waiting }
public struct Driftwood
{
    public int Id,X,Z,Timer,Owner;
    public DriftwoodPhase Phase;
}

public static class DriftwoodMotion
{
    public const int Count=8,Period=360;
    public static int Value(int id)=>id%3==0?4:id%3==1?6:10;
    public static int Radius(int id)=>id%3==0?350:id%3==1?450:600;
    public static Driftwood Create(int id,int delay=0)
    {
        var d=new Driftwood{Id=id,Owner=-1,Timer=delay,Phase=delay>0?DriftwoodPhase.Waiting:DriftwoodPhase.Floating};
        if(delay==0)d.Timer=id*37%Period;
        Place(ref d);return d;
    }
    public static void Advance(ref Driftwood d)
    {
        if(d.Phase==DriftwoodPhase.Hooked)return;
        if(d.Phase==DriftwoodPhase.Waiting)
        {
            if(--d.Timer>0)return;
            d=Create(d.Id);return;
        }
        d.Timer=(d.Timer+1)%Period;Place(ref d);
    }
    private static void Place(ref Driftwood d)
    {
        // Reversible tangential drift: continuous endpoints, no random or floating-point state.
        int t=d.Timer%Period,offset=-1800+(t<=Period/2?t:Period-t)*20;
        int distance=d.Id<4?5100:7800;
        switch(d.Id%4)
        {
            case 0:d.X=offset;d.Z=-distance;break;
            case 1:d.X=distance;d.Z=offset;break;
            case 2:d.X=-offset;d.Z=distance;break;
            default:d.X=-distance;d.Z=-offset;break;
        }
    }
}
