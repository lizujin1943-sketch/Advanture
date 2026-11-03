using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

internal static class PlatformStateCodec
{
    internal static void Write(BinaryWriter w,HookState s)
    {
        w.Write(s.Tiles.Count);
        foreach(var t in s.Tiles){w.Write(t.X);w.Write(t.Z);w.Write((byte)t.Kind);w.Write(t.Builder);w.Write(t.Paid);}
        w.Write(s.Driftwood.Count);
        foreach(var d in s.Driftwood){w.Write(d.Id);w.Write(d.X);w.Write(d.Z);w.Write(d.Timer);w.Write(d.Owner);w.Write((byte)d.Phase);}
    }
    internal static HookState Read(BinaryReader r,ulong tick,Fisher[] players,Fish[] fish,int coins)
    {
        int count=r.ReadInt32();if(count<0 || count>PlatformRules.MaxTiles)throw new InvalidDataException("Invalid tile count.");
        var tiles=new PlatformTile[count];
        for(int i=0;i<count;i++)tiles[i]=new PlatformTile(r.ReadInt32(),r.ReadInt32(),(TileKind)r.ReadByte(),r.ReadInt32(),r.ReadInt32());
        int woodCount=r.ReadInt32();if(woodCount!=0 && woodCount!=DriftwoodMotion.Count)throw new InvalidDataException("Invalid driftwood count.");
        var wood=new Driftwood[woodCount];
        for(int i=0;i<woodCount;i++)wood[i]=new Driftwood{Id=r.ReadInt32(),X=r.ReadInt32(),Z=r.ReadInt32(),Timer=r.ReadInt32(),Owner=r.ReadInt32(),Phase=(DriftwoodPhase)r.ReadByte()};
        return new HookState(tick,players,fish,coins,tiles,wood); // 地图配置与损毁账本在后续尾段读取后统一校验。
    }
    /// <summary>统一库存允许转交与地面余料，因此材料在全局守恒；个人 Earned 只记录本人收获。</summary>
    internal static void ValidateBalances(HookState s)
    {
        if(s.Tackle==null)
        {
            foreach(var p in s.Players)
                if(p.WoodEarned-(long)p.Wood!=s.Tiles.Where(t=>t.Builder==p.Slot && t.Kind==TileKind.Wood).Sum(t=>(long)t.Paid) ||
                    p.FishbonesEarned-(long)p.Fishbones!=s.Tiles.Where(t=>t.Builder==p.Slot && t.Kind==TileKind.Fishbone).Sum(t=>(long)t.Paid))
                    throw new InvalidDataException("Invalid legacy material balance.");
            return;
        }
        var inventory=s.Tackle.Inventory;
        foreach(var p in s.Players)
            if(p.Wood!=inventory.Balance(p.Slot,ItemKind.Wood) || p.Fishbones!=inventory.Balance(p.Slot,ItemKind.Fishbone))
                throw new InvalidDataException("Material summary differs from canonical inventory.");
        foreach(var kind in new[]{ItemKind.Wood,ItemKind.Fishbone})
        {
            long earned=s.Players.Sum(p=>kind==ItemKind.Wood?(long)p.WoodEarned:p.FishbonesEarned);
            long remaining=inventory.Items.Where(i=>i.Kind==kind).Sum(i=>(long)i.Count);
            long spent=s.Tiles.Where(t=>t.Kind==(kind==ItemKind.Wood?TileKind.Wood:TileKind.Fishbone)).Sum(t=>(long)t.Paid)
                +s.Boss.DestroyedTiles.Where(t=>t.Kind==(kind==ItemKind.Wood?TileKind.Wood:TileKind.Fishbone)).Sum(t=>t.Paid);
            if(earned!=remaining+spent)throw new InvalidDataException("Global material conservation failed.");
        }
    }
    internal static void Validate(HookState s)
    {
        if((s.Tiles.Count==0)!=(s.Driftwood.Count==0))throw new InvalidDataException("Incomplete platform world.");
        var initialTiles=s.Tackle?.Config.Boss.Enabled==true?BossArena.InitialTiles(s.Tackle?.Config):PlatformRules.InitialTiles();
        var initialCells=new HashSet<(int,int)>(initialTiles.Select(t=>(t.X,t.Z)));
        var retainedInitial=new HashSet<(int,int)>();
        var cells=new HashSet<(int,int)>();var reachable=new HashSet<(int,int)>();
        var queue=new Queue<(int,int)>();
        foreach(var t in s.Tiles)
        {
            if(!PlatformRules.InBounds(t.X,t.Z) || !cells.Add((t.X,t.Z)) || t.Kind<TileKind.Wood || t.Kind>TileKind.Fishbone ||
                t.Builder< -1 || t.Builder>=s.Players.Count || t.Paid<0 || t.Paid>1000 || (t.Builder<0)!=(t.Paid==0) ||
                (t.Builder<0 && (!initialCells.Contains((t.X,t.Z)) || t.Kind!=TileKind.Wood)))
                throw new InvalidDataException("Invalid platform tile.");
            if(t.Builder==-1)retainedInitial.Add((t.X,t.Z));
        }
        if(s.Tiles.Count>0)
        {
            foreach(var loss in s.Boss.DestroyedTiles)if(loss.Builder==-1)retainedInitial.Add((loss.X,loss.Z));
            if(!initialCells.IsSubsetOf(retainedInitial))throw new InvalidDataException("Missing initial platform tile.");
            // 损毁格只为证明原连通性参与遍历；它们不会变回可站立地板。
            foreach(var loss in s.Boss.DestroyedTiles)cells.Add((loss.X,loss.Z));
            queue.Enqueue((0,0));reachable.Add((0,0));
            while(queue.Count>0)
            {
                var (x,z)=queue.Dequeue();
                foreach(var p in new[]{(x-1,z),(x+1,z),(x,z-1),(x,z+1)})
                    if(cells.Contains(p) && reachable.Add(p))queue.Enqueue(p);
            }
            if(reachable.Count!=cells.Count)throw new InvalidDataException("Disconnected platform.");
        }
        foreach(var p in s.Players)
        {
            if(p.Wood<0 || p.Fishbones<0 || p.WoodEarned<0 || p.FishbonesEarned<0 ||
                p.LastBuildResult>BuildResult.LimitReached || (p.LastBuildId==0)!=(p.LastBuildResult==BuildResult.None) ||
                p.DriftwoodId< -1 || p.DriftwoodId>=s.Driftwood.Count ||
                (p.DriftwoodId>=0 && (p.Phase!=HookPhase.Returning || p.FishId>=0 || p.HookedPlayer>=0 || s.Driftwood[p.DriftwoodId].Owner!=p.Slot)))
                throw new InvalidDataException("Invalid construction inventory or wood ownership.");
        }
        for(int i=0;i<s.Driftwood.Count;i++)
        {
            var d=s.Driftwood[i];
            if(d.Id!=i || d.Phase>DriftwoodPhase.Waiting || d.Owner< -1 || d.Owner>=s.Players.Count ||
                d.Timer<0 || d.Timer>=DriftwoodMotion.Period || Math.Abs((long)d.X)>100000 || Math.Abs((long)d.Z)>100000 ||
                (d.Phase==DriftwoodPhase.Hooked)!=(d.Owner>=0) || (d.Owner>=0 && s.Players[d.Owner].DriftwoodId!=i))
                throw new InvalidDataException("Invalid floating wood.");
        }
    }
}
