using System;

namespace Advanture.HookIsland.Simulation;

/// <summary>30 Hz exact fixed-point displacement. Kept separate from collision and terrain rules.</summary>
public static class CharacterMovement
{
    public const int Denominator = 1000 * HookGame.Rate;
    public static int Delta(int direction, int speed, ref int remainder)
    {
        if (direction < -1000 || direction > 1000 || speed < 0 || speed > CharacterConfig.MaximumSpeed ||
            Math.Abs((long)remainder) >= Denominator) throw new ArgumentOutOfRangeException(nameof(direction));
        int total = direction * speed + remainder;
        remainder = total % Denominator; return total / Denominator;
    }
}
