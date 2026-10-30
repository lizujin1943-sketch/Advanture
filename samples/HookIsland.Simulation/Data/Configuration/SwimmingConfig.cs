using System.IO;

namespace Advanture.HookIsland.Simulation;

/// <summary>Room-owned water rules. Health uses basis points of maximum HP; stamina uses milli-points/second.</summary>
public sealed class SwimmingConfig
{
    public SwimmingConfig(int healthLossBasisPointsPerSecond=100,int speed=1000,int staminaMilliPerSecond=10000)
    {
        if(healthLossBasisPointsPerSecond<1 || healthLossBasisPointsPerSecond>10000 || speed<1 || speed>10000 ||
            staminaMilliPerSecond<1 || staminaMilliPerSecond>1000000)throw new InvalidDataException("Invalid swimming configuration.");
        HealthLossBasisPointsPerSecond=healthLossBasisPointsPerSecond;Speed=speed;StaminaMilliPerSecond=staminaMilliPerSecond;
    }
    public int HealthLossBasisPointsPerSecond { get; }
    public int Speed { get; }
    public int StaminaMilliPerSecond { get; }
    public void Write(BinaryWriter w){w.Write(HealthLossBasisPointsPerSecond);w.Write(Speed);w.Write(StaminaMilliPerSecond);}
    public static SwimmingConfig Read(BinaryReader r)=>new SwimmingConfig(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());
}
