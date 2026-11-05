using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>可选海域挑战；未显式开启时不生成竞技场，也不改变钓鱼终章。</summary>
public sealed class BossConfig
{
    public static BossConfig Disabled { get; }=new BossConfig(false);
    public const int CenterX=0,CenterZ=28000,BeaconX=0,BeaconZ=8000;
    public const int ArenaInnerRadius=12000,ArenaOuterRadius=20000,AggroRadius=23000;
    public const int EmergeTicks=120,EnrageTicks=90,DyingTicks=180,ResetTicks=300;
    public const int SlamImpactTicks=45,SlamFinishTicks=90,SweepStartTicks=36,SweepEndTicks=60,SweepFinishTicks=108;
    public const int TownInkWindupTicks=60,TownInkFinishTicks=120;
    public const int InkWindupTicks=36,InkFinishTicks=96,InkDurationTicks=180,InkSlowPermille=650;
    public BossConfig(bool enabled=true,int bodyHealth=2400,int limbHealth=300,int supportHealth=220,int rewardCoins=600)
    {
        if(bodyHealth<1 || bodyHealth>100000 || limbHealth<1 || limbHealth>10000 || supportHealth<1 || supportHealth>10000 || rewardCoins<0 || rewardCoins>100000)
            throw new InvalidDataException("Invalid boss configuration.");
        Enabled=enabled;BodyHealth=bodyHealth;LimbHealth=limbHealth;SupportHealth=supportHealth;RewardCoins=rewardCoins;
    }
    public bool Enabled {get;} public int BodyHealth {get;} public int LimbHealth {get;} public int SupportHealth {get;} public int RewardCoins {get;}
    public int ScaledBody(int participants)=>BodyHealth*(100+65*(participants-1))/100;
    public int ScaledLimb(int id,int participants)=>(id<4?LimbHealth:SupportHealth)*(100+45*(participants-1))/100;
    internal void Write(BinaryWriter w){w.Write((byte)(Enabled?1:0));w.Write(BodyHealth);w.Write(LimbHealth);w.Write(SupportHealth);w.Write(RewardCoins);}
    internal static BossConfig Read(BinaryReader r)
    {byte enabled=r.ReadByte();if(enabled>1)throw new InvalidDataException("Invalid boss flag.");return new BossConfig(enabled==1,r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32());}
}
