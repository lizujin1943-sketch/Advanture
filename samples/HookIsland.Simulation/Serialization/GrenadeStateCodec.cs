using System;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>最后一个世界尾段。既有尾段读完后再重建，避免中间重建 HookState 丢失投掷物。</summary>
internal static class GrenadeStateCodec
{
    internal static void Write(BinaryWriter w,HookState s)
    {
        w.Write(0x314E5247);
        foreach(var p in s.Players)
        {
            var a=p.Grenade;w.Write(a.LastRequest);w.Write((byte)a.Result);w.Write(a.ActionRequest);w.Write(a.Sequence);
            w.Write(a.ItemId);w.Write(a.TargetX);w.Write(a.TargetZ);w.Write(a.RemainingTicks);w.Write(a.ReturnWeaponId);
        }
        w.Write(s.Grenades.Count);
        foreach(var g in s.Grenades)
        {
            w.Write(g.Id);w.Write(g.Owner);w.Write(g.SourceItemId);w.Write(g.Sequence);w.Write(g.SpawnTick);w.Write(g.LandedTick);w.Write((byte)g.Phase);
            w.Write(g.StartX);w.Write(g.StartY);w.Write(g.StartZ);w.Write(g.TargetX);w.Write(g.TargetY);w.Write(g.TargetZ);
            w.Write(g.X);w.Write(g.Y);w.Write(g.Z);w.Write(g.Age);w.Write(g.FlightTicks);w.Write(g.ArcHeight);w.Write(g.VerticalSpeed);w.Write(g.Water);
        }
    }
    internal static HookState Read(BinaryReader r,HookState s)
    {
        if(r.ReadInt32()!=0x314E5247)throw new InvalidDataException("Grenade state version mismatch.");
        var players=s.Players.ToArray();
        for(int i=0;i<players.Length;i++)players[i].Grenade=new GrenadeAction{LastRequest=r.ReadUInt32(),Result=(GrenadeResult)r.ReadByte(),ActionRequest=r.ReadUInt32(),Sequence=r.ReadUInt32(),
            ItemId=r.ReadInt32(),TargetX=r.ReadInt32(),TargetZ=r.ReadInt32(),RemainingTicks=r.ReadInt32(),ReturnWeaponId=r.ReadInt32()};
        int count=r.ReadInt32();if(count<0 || count>GrenadeRules.MaxActive)throw new InvalidDataException("Too many grenades.");
        var grenades=new GrenadeState[count];
        for(int i=0;i<count;i++)
        {
            var g=new GrenadeState{Id=r.ReadInt32(),Owner=r.ReadInt32(),SourceItemId=r.ReadInt32(),Sequence=r.ReadUInt32(),SpawnTick=r.ReadUInt64(),LandedTick=r.ReadUInt64(),Phase=(GrenadePhase)r.ReadByte(),
                StartX=r.ReadInt32(),StartY=r.ReadInt32(),StartZ=r.ReadInt32(),TargetX=r.ReadInt32(),TargetY=r.ReadInt32(),TargetZ=r.ReadInt32(),X=r.ReadInt32(),Y=r.ReadInt32(),Z=r.ReadInt32(),Age=r.ReadInt32(),FlightTicks=r.ReadInt32(),ArcHeight=r.ReadInt32(),VerticalSpeed=r.ReadInt32()};
            byte water=r.ReadByte();if(water>1)throw new InvalidDataException("Invalid grenade water flag.");g.Water=water==1;grenades[i]=g;
        }
        var result=new HookState(s.Steps,players,s.Fish,s.Coins,s.Tiles,s.Driftwood,s.Tackle,s.Enemies,s.RoomRules,s.Treasure,s.Commissions,s.Ocean,s.Finale,s.Crabs,s.Boss,grenades);
        Validate(result);return result;
    }
    private static bool Coordinate(int value)=>Math.Abs((long)value)<=50000;
    private static void Validate(HookState s)
    {
        var c=GrenadeConfig.Default;
        foreach(var p in s.Players)
        {
            var a=p.Grenade;
            if(a.Result>GrenadeResult.Exhausted || (a.LastRequest==0)!=(a.Result==GrenadeResult.None) || a.ActionRequest>a.LastRequest || a.RemainingTicks<0 || a.RemainingTicks>c.ActionTicks ||
                a.ItemId<0 || a.ReturnWeaponId<0 || !Coordinate(a.TargetX) || !Coordinate(a.TargetZ) || a.Active && (a.ActionRequest==0 || a.ItemId==0) ||
                a.Result==GrenadeResult.Pending && (!a.Active || a.ActionRequest!=a.LastRequest || a.RemainingTicks<=c.ActionTicks-c.WindupTicks))
                throw new InvalidDataException("Invalid grenade action or receipt.");
            if(a.Active && a.RemainingTicks>c.ActionTicks-c.WindupTicks &&
                (s.Tackle?.Inventory.ActiveItem(p.Slot)?.Id!=a.ItemId || s.Tackle.Inventory.ActiveItem(p.Slot)?.Kind!=ItemKind.Throwable))
                throw new InvalidDataException("Pending grenade is not owned and equipped.");
        }
        if(s.Grenades.Select(g=>g.Id).Distinct().Count()!=s.Grenades.Count)throw new InvalidDataException("Duplicate grenade ID.");
        foreach(var g in s.Grenades)
        {
            if(g.Id<=0 || g.Id!=g.SourceItemId || g.Owner<0 || g.Owner>=s.Players.Count || g.Sequence==0 || g.Sequence>s.Players[g.Owner].Grenade.Sequence ||
                g.SpawnTick==0 || g.SpawnTick>s.Steps || g.Phase>GrenadePhase.Landed || g.Age<0 || (ulong)g.Age>s.Steps-g.SpawnTick || g.Age>2000 ||
                g.FlightTicks<c.MinFlightTicks || g.FlightTicks>c.MaxFlightTicks || g.ArcHeight<c.MinArc || g.ArcHeight>c.MaxArc || g.VerticalSpeed<0 || g.VerticalSpeed>2000 ||
                !Coordinate(g.X) || !Coordinate(g.Z) || !Coordinate(g.StartX) || !Coordinate(g.StartZ) || !Coordinate(g.TargetX) || !Coordinate(g.TargetZ) ||
                g.Y< -300 || g.Y>20000 || g.StartY<0 || g.StartY>10000 || g.TargetY<0 || g.TargetY>10000 ||
                g.Phase==GrenadePhase.Flying && (g.Age>=g.FlightTicks || g.LandedTick!=0 || g.VerticalSpeed!=0) ||
                g.Phase==GrenadePhase.Falling && g.LandedTick!=0 ||
                g.Phase==GrenadePhase.Landed && (g.LandedTick<=g.SpawnTick || g.LandedTick>s.Steps || s.Steps-g.LandedTick>=(ulong)c.FuseTicks || g.VerticalSpeed!=0) ||
                s.Tackle==null || g.SourceItemId>=s.Tackle.Inventory.NextId || s.Tackle.Inventory.Items.Any(i=>i.Id==g.SourceItemId))
                throw new InvalidDataException("Invalid grenade flight.");
        }
    }
}
