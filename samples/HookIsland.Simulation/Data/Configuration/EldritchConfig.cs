using System.IO;

namespace Advanture.HookIsland.Simulation;

public sealed class EldritchConfig
{
    public static EldritchConfig Disabled {get;}=new EldritchConfig(false);
    public EldritchConfig(bool enabled=true,int initialDelayTicks=900,int staggerTicks=300,int respawnTicks=2700,int maxAlive=4,int speciesMask=1023)
    {
        if(initialDelayTicks<1 || initialDelayTicks>108000 || staggerTicks<0 || staggerTicks>18000 || respawnTicks<30 || respawnTicks>108000 ||
            maxAlive<1 || maxAlive>10 || speciesMask<1 || speciesMask>1023)throw new InvalidDataException("Invalid eldritch encounter configuration.");
        Enabled=enabled;InitialDelayTicks=initialDelayTicks;StaggerTicks=staggerTicks;RespawnTicks=respawnTicks;MaxAlive=maxAlive;SpeciesMask=speciesMask;
    }
    public bool Enabled {get;} public int InitialDelayTicks {get;} public int StaggerTicks {get;}
    public int RespawnTicks {get;} public int MaxAlive {get;} public int SpeciesMask {get;}
    public bool Includes(int species)=>Enabled && species>=0 && species<10 && (SpeciesMask&(1<<species))!=0;
    internal void Write(BinaryWriter w){w.Write(Enabled);w.Write(InitialDelayTicks);w.Write(StaggerTicks);w.Write(RespawnTicks);w.Write(MaxAlive);w.Write(SpeciesMask);}
    internal static EldritchConfig Read(BinaryReader r)
    {byte flag=r.ReadByte();if(flag>1)throw new InvalidDataException("Invalid eldritch flag.");return new EldritchConfig(flag==1,r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32());}
}
