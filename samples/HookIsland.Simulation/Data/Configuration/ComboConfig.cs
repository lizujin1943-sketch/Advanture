using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>Integer reward curve shared by the authority, checkpoints and HUD.</summary>
public sealed class ComboConfig
{
    public ComboConfig(bool enabled=true,int displayStartCount=3,IEnumerable<int>? goldBonusPercent=null)
    {
        var curve=(goldBonusPercent??new[]{0,0,10,15,20,25,30,35,40,50}).ToArray();
        if(curve.Length<1 || curve.Length>100 || displayStartCount<1 || displayStartCount>100 ||
            curve.Any(x=>x<0 || x>1000) || curve.Where((x,i)=>i>0 && x<curve[i-1]).Any())
            throw new InvalidDataException("Invalid combo reward curve.");
        Enabled=enabled;DisplayStartCount=displayStartCount;GoldBonusPercent=Array.AsReadOnly(curve);
    }
    public bool Enabled {get;}
    public int DisplayStartCount {get;}
    public IReadOnlyList<int> GoldBonusPercent {get;}
    public int BonusPercent(int count)=>Enabled && count>0?GoldBonusPercent[Math.Min(count,GoldBonusPercent.Count)-1]:0;
    internal void Write(BinaryWriter w)
    {
        w.Write(Enabled);w.Write(DisplayStartCount);w.Write(GoldBonusPercent.Count);
        foreach(int bonus in GoldBonusPercent)w.Write(bonus);
    }
    internal static ComboConfig Read(BinaryReader r)
    {
        byte enabled=r.ReadByte();int start=r.ReadInt32(),length=r.ReadInt32();
        if(enabled>1 || length<1 || length>100)throw new InvalidDataException("Invalid combo configuration.");
        var curve=new int[length];for(int i=0;i<length;i++)curve[i]=r.ReadInt32();
        return new ComboConfig(enabled!=0,start,curve);
    }
}
