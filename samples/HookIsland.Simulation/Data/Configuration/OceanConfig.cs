using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed class FishSchoolProfile
{
    public FishSchoolProfile(FishRarity minRarity,FishRarity maxRarity,int minWeightGrams,int maxWeightGrams,int maxGrade=1)
    {
        if(minRarity>maxRarity || maxRarity>FishRarity.Legendary || minWeightGrams<1000 || maxWeightGrams>10000 ||
            minWeightGrams>maxWeightGrams || minWeightGrams%100!=0 || maxWeightGrams%100!=0 || maxGrade<1 || maxGrade>3)
            throw new InvalidDataException("Invalid fish school profile.");
        MinRarity=minRarity;MaxRarity=maxRarity;MinWeightGrams=minWeightGrams;MaxWeightGrams=maxWeightGrams;MaxGrade=maxGrade;
    }
    public FishRarity MinRarity {get;}
    public FishRarity MaxRarity {get;}
    public int MinWeightGrams {get;}
    public int MaxWeightGrams {get;}
    public int MaxGrade {get;}
    internal void Write(BinaryWriter w)
    {w.Write((byte)MinRarity);w.Write((byte)MaxRarity);w.Write(MinWeightGrams);w.Write(MaxWeightGrams);w.Write(MaxGrade);}
    internal static FishSchoolProfile Read(BinaryReader r)=>new FishSchoolProfile((FishRarity)r.ReadByte(),(FishRarity)r.ReadByte(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32());
}

public sealed class OceanConfig
{
    public static OceanConfig Disabled {get;}=new OceanConfig(enabled:false);
    public OceanConfig(IEnumerable<int>? unlocks=null,int visitSlots=2,int noCatchTicks=2700,int pressureWindowTicks=900,
        int pressureCount=3,int recoveryTicks=600,int recoveryCooldownTicks=1800,int visitTicks=900,
        FishSchoolProfile? opening=null,FishSchoolProfile? middle=null,FishSchoolProfile? far=null,
        FishSchoolProfile? recoveryVisit=null,FishSchoolProfile? middleVisit=null,FishSchoolProfile? rareVisit=null,
        bool pauseMonster=true,bool enabled=true)
    {
        var bands=(unlocks??(enabled?Enumerable.Range(0,24).Select(i=>i%3):Array.Empty<int>())).ToArray();
        if(enabled?(visitSlots<1 || visitSlots>4 || bands.Length<1 || bands.Length+visitSlots>32 || !bands.Contains(0) || bands.Any(x=>x<0 || x>2)):bands.Length!=0)
            throw new InvalidDataException("Invalid ocean fish slots or unlock stages.");
        if(noCatchTicks<1 || noCatchTicks>108000 || pressureWindowTicks<1 || pressureWindowTicks>108000 || pressureCount<1 || pressureCount>100 ||
            recoveryTicks<1 || recoveryTicks>18000 || recoveryCooldownTicks<recoveryTicks || recoveryCooldownTicks>108000 || visitTicks<1 || visitTicks>18000)
            throw new InvalidDataException("Invalid ocean event timing.");
        Enabled=enabled;Unlocks=Array.AsReadOnly(bands);VisitSlots=enabled?visitSlots:0;
        NoCatchTicks=noCatchTicks;PressureWindowTicks=pressureWindowTicks;PressureCount=pressureCount;
        RecoveryTicks=recoveryTicks;RecoveryCooldownTicks=recoveryCooldownTicks;VisitTicks=visitTicks;PauseMonster=pauseMonster;
        Opening=opening??new FishSchoolProfile(FishRarity.Common,FishRarity.Common,1000,3000);
        Middle=middle??new FishSchoolProfile(FishRarity.Common,FishRarity.Rare,2000,6000,3);
        Far=far??new FishSchoolProfile(FishRarity.Rare,FishRarity.Legendary,2000,6000,3);
        RecoveryVisit=recoveryVisit??new FishSchoolProfile(FishRarity.Common,FishRarity.Common,1000,2000);
        MiddleVisit=middleVisit??new FishSchoolProfile(FishRarity.Uncommon,FishRarity.Uncommon,1000,2000);
        RareVisit=rareVisit??new FishSchoolProfile(FishRarity.Rare,FishRarity.Rare,1000,2000);
        if(Opening.MaxRarity!=FishRarity.Common || Opening.MaxGrade!=1 || Opening.MaxWeightGrams>3000)
            throw new InvalidDataException("Opening schools must retain ordinary light fish.");
    }
    public bool Enabled {get;}
    public IReadOnlyList<int> Unlocks {get;}
    public int VisitSlots {get;}
    public int NoCatchTicks {get;}
    public int PressureWindowTicks {get;}
    public int PressureCount {get;}
    public int RecoveryTicks {get;}
    public int RecoveryCooldownTicks {get;}
    public int VisitTicks {get;}
    public bool PauseMonster {get;}
    public FishSchoolProfile Opening {get;}
    public FishSchoolProfile Middle {get;}
    public FishSchoolProfile Far {get;}
    public FishSchoolProfile RecoveryVisit {get;}
    public FishSchoolProfile MiddleVisit {get;}
    public FishSchoolProfile RareVisit {get;}
    internal void Write(BinaryWriter w)
    {
        w.Write(Enabled?(byte)1:(byte)0);w.Write(Unlocks.Count);foreach(int u in Unlocks)w.Write(u);w.Write(VisitSlots);
        w.Write(NoCatchTicks);w.Write(PressureWindowTicks);w.Write(PressureCount);w.Write(RecoveryTicks);w.Write(RecoveryCooldownTicks);w.Write(VisitTicks);
        Opening.Write(w);Middle.Write(w);Far.Write(w);RecoveryVisit.Write(w);MiddleVisit.Write(w);RareVisit.Write(w);w.Write(PauseMonster?(byte)1:(byte)0);
    }
    internal static OceanConfig Read(BinaryReader r)
    {
        byte enabled=r.ReadByte();int count=r.ReadInt32();
        if(enabled>1 || count<0 || count>31)throw new InvalidDataException("Invalid ocean configuration.");
        var unlocks=new int[count];for(int i=0;i<count;i++)unlocks[i]=r.ReadInt32();
        int slots=r.ReadInt32(),stall=r.ReadInt32(),window=r.ReadInt32(),pressure=r.ReadInt32(),recovery=r.ReadInt32(),cooldown=r.ReadInt32(),visit=r.ReadInt32();
        var opening=FishSchoolProfile.Read(r);var middle=FishSchoolProfile.Read(r);var far=FishSchoolProfile.Read(r);
        var relief=FishSchoolProfile.Read(r);var middleVisit=FishSchoolProfile.Read(r);var rareVisit=FishSchoolProfile.Read(r);byte pause=r.ReadByte();
        if(pause>1 || (enabled==0 && slots!=0))throw new InvalidDataException("Invalid ocean configuration flags.");
        return new OceanConfig(unlocks,slots,stall,window,pressure,recovery,cooldown,visit,opening,middle,far,relief,middleVisit,rareVisit,pause==1,enabled==1);
    }
}
