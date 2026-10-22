namespace Advanture.HookIsland.Simulation;

public sealed partial class HookGame
{
    private sealed partial class Model
    {
        private OceanSystem? ocean;
        private void InitializeOcean()=>ocean=map.Fishing?.Ocean.Enabled==true?new OceanSystem(map.Fishing.Ocean):null;
        private Fish CreateFish(int id,int delay,uint generation)
        {
            var c=map.Fishing;
            if(c==null)return FishMotion.Create(id,map.FishSpots[id],delay);
            if(!c.Ocean.Enabled)return FishCatalog.Spawn(id,map.FishSpots[id],delay,generation,c.Seed);
            if(id>=c.Ocean.Unlocks.Count)return OceanRules.Dormant(id,map.FishSpots[id],generation);
            int band=c.Ocean.Unlocks[id];
            return OceanRules.Spawn(id,map.FishSpots[id],band>OceanRules.Stage(commissions.Completed)?0:delay,generation,c.Seed,
                band==0?c.Ocean.Opening:band==1?c.Ocean.Middle:c.Ocean.Far);
        }
    }
}
