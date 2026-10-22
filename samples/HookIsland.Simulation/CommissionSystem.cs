using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public static class CommissionSystem
{
    public static CommissionState Create(CommissionConfig config)=>new CommissionState(0,0,new CommissionProgress[config.Definitions.Count]);
    public static CommissionState RecordCatch(CommissionConfig config,CommissionState state,bool rare)
    {
        Validate(config,state);
        if(state.Completed==config.Definitions.Count)return state;
        var entries=state.Entries.ToArray();int index=state.Completed;
        var previous=entries[index];var target=config.Definitions[index];
        entries[index]=new CommissionProgress(previous.Fish+1,previous.Rare+(rare?1:0));
        bool completed=entries[index].Fish>=target.Fish || entries[index].Rare>=target.Rare;
        // One caught target contributes to exactly one commission, including when both paths complete.
        return new CommissionState(index+(completed?1:0),state.Bait+(completed?target.Bait:0),entries);
    }
    internal static void Validate(CommissionConfig config,CommissionState state)
    {
        if(state.Entries.Count!=config.Definitions.Count || state.Completed<0 || state.Completed>state.Entries.Count)
            throw new InvalidDataException("Invalid commission progress count.");
        int bait=0;
        for(int i=0;i<state.Entries.Count;i++)
        {
            var e=state.Entries[i];var d=config.Definitions[i];
            if(e.Fish<0 || e.Fish>d.Fish || e.Rare<0 || e.Rare>d.Rare || e.Rare>e.Fish ||
                (i>state.Completed && (e.Fish!=0 || e.Rare!=0)))throw new InvalidDataException("Invalid commission progress.");
            bool reached=e.Fish==d.Fish || e.Rare==d.Rare;
            if(reached!=(i<state.Completed))throw new InvalidDataException("Invalid commission completion order.");
            if(i<state.Completed)bait+=d.Bait;
        }
        if(state.Bait!=bait)throw new InvalidDataException("Invalid commission bait balance.");
    }
}
