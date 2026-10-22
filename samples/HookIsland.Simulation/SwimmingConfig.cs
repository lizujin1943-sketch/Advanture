using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>Room-owned, serialized water rules. HP/stamina rates are thousandths of a point per second.</summary>
public sealed class SwimmingConfig
{
    public SwimmingConfig(int healthLossMilliPerSecond=5000,int speed=1000,int staminaMilliPerSecond=10000)
    {
        if(healthLossMilliPerSecond<1 || healthLossMilliPerSecond>1000000 || speed<1 || speed>10000 ||
            staminaMilliPerSecond<1 || staminaMilliPerSecond>1000000)throw new InvalidDataException("Invalid swimming configuration.");
        HealthLossMilliPerSecond=healthLossMilliPerSecond;Speed=speed;StaminaMilliPerSecond=staminaMilliPerSecond;
    }
    public int HealthLossMilliPerSecond { get; }
    public int Speed { get; }
    public int StaminaMilliPerSecond { get; }
    public void Write(BinaryWriter w){w.Write(HealthLossMilliPerSecond);w.Write(Speed);w.Write(StaminaMilliPerSecond);}
    public static SwimmingConfig Read(BinaryReader r)=>new SwimmingConfig(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());
}
