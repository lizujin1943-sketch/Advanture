using System;
using System.IO;

namespace Advanture.Tactical.Simulation;

/// <summary>Height is millimetres above the current support. Keeping the fractional position and
/// velocity in the snapshot makes a mid-air restore follow the very same parabola.</summary>
internal readonly struct VerticalMotion
{
    internal const int JumpSpeed = 6000;
    private const int Gravity = 18000;
    internal const int StepHeight = 420;
    internal const int BodyHeight = 1900;
    internal const int ByteCount = 12;
    internal readonly int Height, Fraction, Velocity;
    internal bool Airborne => Height != 0 || Fraction != 0 || Velocity != 0;

    internal VerticalMotion(int height, int fraction, int velocity)
    {
        if (height < 0 || height > 1000000 || fraction < 0 || fraction >= 2000000 ||
            velocity > JumpSpeed * 1000 || velocity < -200000000)
            throw new InvalidDataException("角色垂直运动超出范围。");
        Height = height;
        Fraction = fraction;
        Velocity = velocity;
    }

    internal VerticalMotion Advance(bool jump, int groundDrop, int headroom, int rate)
    {
        if (!Airborne && !jump && groundDrop <= StepHeight) return default;
        // v is scaled by rate, position by 2*rate²: dy = v*dt - g*dt²/2 exactly.
        int units = 2 * rate * rate;
        int velocity = jump && !Airborne ? JumpSpeed * rate : Velocity;
        long start = ((long)Height + groundDrop) * units + Fraction;
        long next = start + 2L * velocity - Gravity;
        if (next <= 0 || headroom <= 0) return default;
        long ceiling = (long)headroom * units;
        if (next >= ceiling && velocity > 0) return new VerticalMotion(headroom, 0, 0);
        return new VerticalMotion((int)(next / units), (int)(next % units), velocity - Gravity);
    }

    internal void Validate(int rate, int headroom)
    {
        if (Fraction >= 2 * rate * rate || Velocity > JumpSpeed * rate || Velocity < -200000L * rate ||
            Height > headroom || (Height == headroom && Fraction > 0) ||
            (Height == 0 && Fraction == 0 && Velocity < 0))
            throw new InvalidDataException("恢复的跳跃状态与地图或帧率不匹配。");
    }
}
