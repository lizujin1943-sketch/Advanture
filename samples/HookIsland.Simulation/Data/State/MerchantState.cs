using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>A single confirmed shelf and its own activation-relative clock.</summary>
public sealed class MerchantState
{
    public MerchantState(int id,bool active,ulong activatedAt,uint batch,ulong nextRestock,IEnumerable<RodStock> stock,IEnumerable<int>? foodStock=null)
    {Id=id;Active=active;ActivatedAt=activatedAt;Batch=batch;NextRestock=nextRestock;Stock=Array.AsReadOnly(stock.ToArray());
        FoodStock=Array.AsReadOnly(foodStock?.ToArray()??FoodCatalog.All.Select(f=>active?f.Stock:0).ToArray());}
    public IReadOnlyList<int> FoodStock {get;}
    public int Id {get;}
    public bool Active {get;}
    public ulong ActivatedAt {get;}
    public uint Batch {get;}
    public ulong NextRestock {get;}
    public IReadOnlyList<RodStock> Stock {get;}
    public static MerchantState Inactive(int id)=>new MerchantState(id,false,0,0,0,Array.Empty<RodStock>());
}
