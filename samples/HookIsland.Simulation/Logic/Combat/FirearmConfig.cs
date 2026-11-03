using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>Shared firearm rules; weapon-specific values live in the catalog.</summary>
public sealed class FirearmConfig
{
    public static FirearmConfig Disabled {get;}=new FirearmConfig(false);
    public FirearmConfig(bool enabled=true,int friendlyDamagePercent=50,int stunTicks=6,int friendlyKnockback=400)
    {
        if(friendlyDamagePercent<0 || friendlyDamagePercent>100 || stunTicks<1 || stunTicks>300 || friendlyKnockback<0 || friendlyKnockback>3000)throw new InvalidDataException("Invalid firearm config.");
        Enabled=enabled;FriendlyDamagePercent=friendlyDamagePercent;StunTicks=stunTicks;FriendlyKnockback=friendlyKnockback;
    }
    public bool Enabled {get;}
    public int FriendlyDamagePercent {get;}
    public int StunTicks {get;}
    public int FriendlyKnockback {get;}
    internal void Write(BinaryWriter w){w.Write(Enabled);w.Write(FriendlyDamagePercent);w.Write(StunTicks);w.Write(FriendlyKnockback);}
    internal static FirearmConfig Read(BinaryReader r){byte enabled=r.ReadByte();if(enabled>1)throw new InvalidDataException("Invalid firearm flag.");return new FirearmConfig(enabled==1,r.ReadInt32(),r.ReadInt32(),r.ReadInt32());}
}
