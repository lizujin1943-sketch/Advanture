using System;
using System.IO;
using System.Linq;
namespace Advanture.HookIsland.Simulation;
public static class OceanRules
{
    public static int Stage(int completed)=>Math.Min(2,completed);
    public static IslandMap CreateMap(IslandMap basis,FishingConfig config)
    {
        if(config.Ocean.Enabled && basis.FishSpots.Count!=config.Ocean.Unlocks.Count)
            throw new InvalidDataException("Ocean unlock count must match the base fish schools.");
        return new IslandMap(basis.Shore,basis.Pier,basis.Spawns,
            basis.FishSpots.Concat(Enumerable.Repeat(new Point(0,-5000),config.Ocean.VisitSlots)),basis.Platform,config);
    }
    public static Point Home(IslandMap map,HookState state,int id)
    {
        var c=map.Fishing?.Ocean;
        if(c?.Enabled==true && id>=c.Unlocks.Count)
        {var v=state.Ocean.Visits[id-c.Unlocks.Count];if(v.Active)return new Point(v.HomeX,v.HomeZ);}
        return map.FishSpots[id];
    }
    public static bool Available(HookState state,int id)
    {
        var c=state.Tackle?.Config.Ocean;
        return c?.Enabled!=true || (id<c.Unlocks.Count?c.Unlocks[id]<=Stage(state.Commissions.Completed):state.Ocean.Visits[id-c.Unlocks.Count].Active);
    }
    internal static Fish Dormant(int id,Point home,uint generation)
    {var f=FishMotion.Create(id,home,0);f.Generation=generation;f.WeightGrams=1000;f.Grade=1;return f;}
    internal static Fish Spawn(int id,Point home,int delay,uint generation,int seed,FishSchoolProfile profile)
    {
        var f=FishMotion.Create(id,home,delay);f.Generation=generation;
        uint random=FishCatalog.Mix(unchecked((uint)seed+(uint)id*747796405u+generation*2891336453u));
        var choices=FishCatalog.All.Select((d,i)=>(d,i)).Where(e=>e.d.Rarity>=profile.MinRarity && e.d.Rarity<=profile.MaxRarity).ToArray();
        int roll=(int)(random%(uint)choices.Sum(e=>e.d.Weight));
        foreach(var e in choices){f.Species=e.i;if(roll<e.d.Weight)break;roll-=e.d.Weight;}
        random=FishCatalog.Mix(random);f.WeightGrams=profile.MinWeightGrams+(int)(random%(uint)((profile.MaxWeightGrams-profile.MinWeightGrams)/100+1))*100;
        random=FishCatalog.Mix(random);int grade=(int)(random%100);f.Grade=Math.Min(profile.MaxGrade,grade<90?1:grade<99?2:3);
        return f;
    }
}
