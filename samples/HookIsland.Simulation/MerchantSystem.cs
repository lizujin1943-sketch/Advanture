using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed class MerchantSystem
{
    private readonly FishingConfig config;
    private readonly MerchantState[] states;
    public MerchantSystem(FishingConfig config,IEnumerable<MerchantState>? state=null)
    {
        this.config=config;
        states=state?.ToArray()??new[]{new MerchantState(0,true,0,1,(ulong)config.RestockSeconds*HookGame.Rate,RodShop.Generate(config,1))}
            .Concat(config.Merchants.Definitions.Select(d=>MerchantState.Inactive(d.Id))).ToArray();
    }
    public IReadOnlyList<MerchantState> Capture()=>Array.AsReadOnly(states.ToArray());
    public MerchantState? Get(int id)=>id>=0 && id<states.Length?states[id]:null;
    public void Advance(IslandMap map,IReadOnlyList<PlatformTile> tiles,IReadOnlyList<Fisher> players,ulong step)
    {
        ulong interval=(ulong)config.RestockSeconds*HookGame.Rate;
        for(int id=0;id<states.Length;id++)
        {
            var s=states[id];
            if(!s.Active)
            {
                if(!players.Any(p=>MerchantRules.CanInteract(p) && MerchantRules.Near(map,tiles,p,id)))continue;
                states[id]=new MerchantState(id,true,step,1,checked(step+interval),RodShop.Generate(config,1,id));
            }
            else if(step>=s.NextRestock)
            {
                // Calculate from the activation epoch so a catch-up call never resets or shifts the clock.
                ulong periods=(step-s.ActivatedAt)/interval;uint batch=checked((uint)(periods+1));
                states[id]=new MerchantState(id,true,s.ActivatedAt,batch,
                    checked(s.ActivatedAt+(periods+1)*interval),RodShop.Generate(config,batch,id));
            }
        }
    }
    internal void Sell(int id,int index)
    {
        var s=states[id];var stock=s.Stock.ToArray();var offer=stock[index];offer.Sold=true;stock[index]=offer;
        states[id]=new MerchantState(id,s.Active,s.ActivatedAt,s.Batch,s.NextRestock,stock);
    }
}
