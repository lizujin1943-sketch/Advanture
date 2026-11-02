using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

internal static class MerchantStateCodec
{
    internal static void Write(BinaryWriter w,IReadOnlyList<MerchantState> states)
    {
        w.Write(states.Count);
        foreach(var s in states)
        {
            w.Write(s.Id);w.Write(s.Active);w.Write(s.ActivatedAt);w.Write(s.Batch);w.Write(s.NextRestock);w.Write(s.Stock.Count);
            foreach(var i in s.Stock){w.Write((byte)i.Kind);w.Write(i.Type);w.Write(i.X);w.Write(i.Y);w.Write(i.Sold);}
            foreach(int count in s.FoodStock)w.Write(count);
        }
    }
    internal static MerchantState[] Read(BinaryReader r)
    {
        int count=r.ReadInt32();if(count<1 || count>5)throw new InvalidDataException("Invalid merchant state count.");
        var states=new MerchantState[count];
        for(int n=0;n<count;n++)
        {
            int id=r.ReadInt32();bool active=ReadBoolean(r);ulong at=r.ReadUInt64();uint batch=r.ReadUInt32();ulong next=r.ReadUInt64();
            int countStock=r.ReadInt32();if(countStock<0 || countStock>RodShop.Capacity)throw new InvalidDataException("Invalid merchant stock count.");
            var stock=new RodStock[countStock];
            for(int i=0;i<countStock;i++)stock[i]=new RodStock{Kind=(ItemKind)r.ReadByte(),Type=r.ReadInt32(),X=r.ReadInt32(),Y=r.ReadInt32(),Sold=ReadBoolean(r)};
            var food=new int[FoodCatalog.All.Count];for(int i=0;i<food.Length;i++)food[i]=r.ReadInt32();
            states[n]=new MerchantState(id,active,at,batch,next,stock,food);
        }
        return states;
    }
    private static bool ReadBoolean(BinaryReader r)
    {byte b=r.ReadByte();if(b>1)throw new InvalidDataException("Noncanonical merchant boolean.");return b==1;}
    internal static void Validate(FishingConfig config,IReadOnlyList<MerchantState> states,ulong steps)
    {
        if(states.Count!=config.Merchants.Definitions.Count+1)throw new InvalidDataException("Merchant set differs from configuration.");
        ulong interval=(ulong)config.RestockSeconds*HookGame.Rate;
        for(int id=0;id<states.Count;id++)
        {
            var s=states[id];
            if(s.FoodStock.Count!=FoodCatalog.All.Count || s.FoodStock.Where((count,type)=>count<0 || count>(s.Active?FoodCatalog.Get(type).Stock:0)).Any())
                throw new InvalidDataException("Invalid food stock.");
            if(s.Id!=id || id==0 && (!s.Active || s.ActivatedAt!=0))throw new InvalidDataException("Invalid merchant identity or initial activation.");
            if(!s.Active)
            {
                if(s.ActivatedAt!=0 || s.Batch!=0 || s.NextRestock!=0 || s.Stock.Count!=0)throw new InvalidDataException("Inactive merchant has stock or a clock.");
                continue;
            }
            if(s.ActivatedAt>steps || id>0 && s.ActivatedAt==0)throw new InvalidDataException("Invalid merchant activation tick.");
            ulong periods=(steps-s.ActivatedAt)/interval;
            if(periods>=uint.MaxValue || s.ActivatedAt>ulong.MaxValue-(periods+1)*interval ||
                s.Batch!=periods+1 || s.NextRestock!=s.ActivatedAt+(periods+1)*interval)
                throw new InvalidDataException("Invalid merchant restock clock.");
            var expected=RodShop.Generate(config,s.Batch,id);
            if(expected.Count!=s.Stock.Count || expected.Where((e,n)=>e.Kind!=s.Stock[n].Kind || e.Type!=s.Stock[n].Type || e.X!=s.Stock[n].X || e.Y!=s.Stock[n].Y).Any())
                throw new InvalidDataException("Invalid merchant assortment.");
        }
    }
}
