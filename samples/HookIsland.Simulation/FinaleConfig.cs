using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed class FinaleConfig
{
    public static FinaleConfig Disabled {get;}=new FinaleConfig(enabled:false);
    public FinaleConfig(int baitCost=3,int summonTicks=300,int relocateTicks=180,int holdTicks=150,
        int staminaMilliPerSecond=12000,IEnumerable<int>? requiredHooks=null,int bodyRadius=1200,
        int edgeOffset=3100,int phaseShift=4000,bool enabled=true)
    {
        var requirements=(requiredHooks??new[]{1,2,2,3}).ToArray();
        if(baitCost<1 || baitCost>16000 || summonTicks<1 || summonTicks>18000 ||
            relocateTicks<1 || relocateTicks>18000 || holdTicks<1 || holdTicks>18000 ||
            staminaMilliPerSecond<1 || staminaMilliPerSecond>1000000 ||
            requirements.Length!=4 || requirements.Where((n,i)=>n<1 || n>i+1 || i>0 && n<requirements[i-1]).Any() ||
            bodyRadius<400 || bodyRadius>2000 || edgeOffset<bodyRadius+1100 || edgeOffset>4000 ||
            phaseShift<1000 || phaseShift>20000)
            throw new InvalidDataException("Invalid finale configuration.");
        Enabled=enabled;BaitCost=baitCost;SummonTicks=summonTicks;RelocateTicks=relocateTicks;HoldTicks=holdTicks;
        StaminaMilliPerSecond=staminaMilliPerSecond;RequiredHooks=Array.AsReadOnly(requirements);
        BodyRadius=bodyRadius;EdgeOffset=edgeOffset;PhaseShift=phaseShift;
    }
    public bool Enabled {get;}
    public int BaitCost {get;}
    public int SummonTicks {get;}
    public int RelocateTicks {get;}
    public int HoldTicks {get;}
    public int StaminaMilliPerSecond {get;}
    public IReadOnlyList<int> RequiredHooks {get;}
    public int BodyRadius {get;}
    public int EdgeOffset {get;}
    public int PhaseShift {get;}
    internal void Write(BinaryWriter w)
    {
        w.Write(Enabled?(byte)1:(byte)0);w.Write(BaitCost);w.Write(SummonTicks);w.Write(RelocateTicks);w.Write(HoldTicks);
        w.Write(StaminaMilliPerSecond);foreach(int n in RequiredHooks)w.Write(n);
        w.Write(BodyRadius);w.Write(EdgeOffset);w.Write(PhaseShift);
    }
    internal static FinaleConfig Read(BinaryReader r)
    {
        byte enabled=r.ReadByte();if(enabled>1)throw new InvalidDataException("Invalid finale enabled flag.");
        int bait=r.ReadInt32(),summon=r.ReadInt32(),relocate=r.ReadInt32(),hold=r.ReadInt32(),stamina=r.ReadInt32();
        var requirements=Enumerable.Range(0,4).Select(_=>r.ReadInt32()).ToArray();
        return new FinaleConfig(bait,summon,relocate,hold,stamina,requirements,r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),enabled==1);
    }
}
