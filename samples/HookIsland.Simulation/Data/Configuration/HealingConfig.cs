using System;
using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>Personal charges granted on a new round or a successful paid respawn.</summary>
public sealed class HealingConfig
{
    public HealingConfig(int charges=3,int healthPermille=400)
    {
        if(charges<1 || charges>99 || healthPermille<1 || healthPermille>1000)
            throw new InvalidDataException("Invalid healing configuration.");
        Charges=charges;HealthPermille=healthPermille;
    }
    public int Charges {get;}
    public int HealthPermille {get;}
    public int Amount(int maximum)=>Math.Max(1,(int)((long)maximum*HealthPermille/1000));
    internal void Write(BinaryWriter writer){writer.Write(Charges);writer.Write(HealthPermille);}
    internal static HealingConfig Read(BinaryReader reader)=>new HealingConfig(reader.ReadInt32(),reader.ReadInt32());
}
