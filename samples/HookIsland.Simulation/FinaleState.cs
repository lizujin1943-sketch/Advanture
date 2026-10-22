namespace Advanture.HookIsland.Simulation;

public enum FinalePhase : byte { Dormant, Summoning, Surfaced, Submerged, Victory }

public sealed class FinaleState
{
    public static FinaleState Empty {get;}=new FinaleState();
    public FinaleState(FinalePhase phase=FinalePhase.Dormant,int completed=0,int timer=0,int progress=0,
        int spentBait=0,int x=0,int z=0,int previousX=0,int previousZ=0,int direction=0,ulong victoryTick=0)
    {
        Phase=phase;Completed=completed;Timer=timer;Progress=progress;SpentBait=spentBait;
        X=x;Z=z;PreviousX=previousX;PreviousZ=previousZ;Direction=direction;VictoryTick=victoryTick;
    }
    public FinalePhase Phase {get;}
    public int Completed {get;}
    public int Timer {get;}
    public int Progress {get;}
    public int SpentBait {get;}
    public int X {get;}
    public int Z {get;}
    public int PreviousX {get;}
    public int PreviousZ {get;}
    public int Direction {get;}
    public ulong VictoryTick {get;}
}
