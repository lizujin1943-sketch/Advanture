using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public readonly struct MerchantDefinition
{
    public MerchantDefinition(int id,int level,int cellX,int cellZ)
    {Id=id;Level=level;CellX=cellX;CellZ=cellZ;}
    public int Id {get;}
    public int Level {get;}
    public int CellX {get;}
    public int CellZ {get;}
    public int X=>CellX*PlatformRules.TileSize;
    public int Z=>CellZ*PlatformRules.TileSize;
}

/// <summary>Optional offshore merchants. The original merchant remains ID zero.</summary>
public sealed class MerchantConfig
{
    public static MerchantConfig Empty {get;}=new MerchantConfig();
    public static MerchantConfig Default {get;}=new MerchantConfig(new[]{
        new MerchantDefinition(1,2,5,0),new MerchantDefinition(2,3,0,7),
        new MerchantDefinition(3,4,-9,0),new MerchantDefinition(4,5,0,-11)});
    public MerchantConfig(IEnumerable<MerchantDefinition>? definitions=null)
    {
        var entries=(definitions??Array.Empty<MerchantDefinition>()).ToArray();
        if(entries.Length>4 || entries.Where((d,i)=>d.Id!=i+1 || d.Level<1 || d.Level>5 ||
            !PlatformRules.InBounds(d.CellX,d.CellZ) || Math.Abs(d.CellX)<=1 && Math.Abs(d.CellZ)<=1).Any() ||
            entries.Select(d=>(d.CellX,d.CellZ)).Distinct().Count()!=entries.Length)
            throw new InvalidDataException("Invalid offshore merchant configuration.");
        Definitions=Array.AsReadOnly(entries);
    }
    public IReadOnlyList<MerchantDefinition> Definitions {get;}
    internal void Write(BinaryWriter w)
    {w.Write(Definitions.Count);foreach(var d in Definitions){w.Write(d.Id);w.Write(d.Level);w.Write(d.CellX);w.Write(d.CellZ);}}
    internal static MerchantConfig Read(BinaryReader r)
    {
        int count=r.ReadInt32();if(count<0 || count>4)throw new InvalidDataException("Invalid merchant count.");
        var definitions=new MerchantDefinition[count];
        for(int i=0;i<count;i++)definitions[i]=new MerchantDefinition(r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32());
        return new MerchantConfig(definitions);
    }
}
