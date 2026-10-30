using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    /// <summary>Samples authored poses from confirmed observations, including recovery and escape ticks.</summary>
    internal static class FishAppearance
    {
        internal static void Present(FishModelView view, Fish fish, ulong steps)
        {
            view.SetGrade(fish.Grade);
            if (fish.Escape.Active) { Escape(view, fish.Escape); return; }
            switch (fish.Phase)
            {
                case FishPhase.Swimming: view.Sample(FishPose.Swim, fish.Timer / (double)HookGame.Rate); break;
                case FishPhase.Jumping: view.Sample(FishPose.Leap, fish.Timer / (double)FishMotion.JumpTicks, true); break;
                case FishPhase.Diving: view.Sample(FishPose.Dive, fish.Timer / (double)FishMotion.DiveTicks, true); break;
                case FishPhase.Hooked: view.Sample(FishPose.Hooked, Clock(steps, fish.Id)); break;
                default: view.Sample(FishPose.Idle, Clock(steps, fish.Id)); break;
            }
        }

        internal static void Escape(FishModelView view, CatchEscapeState escape)
        {
            int age = escape.Tick - 1;
            if (age < CatchEscapeMotion.StruggleTicks) view.Sample(FishPose.Flop, age / (double)HookGame.Rate);
            else if (escape.Tick < CatchEscapeMotion.SplashTick)
                view.Sample(FishPose.Leap, (age - CatchEscapeMotion.StruggleTicks) / (double)CatchEscapeMotion.FlightTicks, true);
            else view.Sample(FishPose.Dive, (escape.Tick - CatchEscapeMotion.SplashTick) / (double)CatchEscapeMotion.SubmergeTicks, true);
        }

        internal static void Piranha(FishModelView view, Simulation.Piranha fish, ulong steps, EnemyConfig config)
        {
            if (fish.Escape.Active) { Escape(view, fish.Escape); return; }
            if (fish.Owner >= 0) { view.Sample(FishPose.Hooked, Clock(steps, fish.Id)); return; }
            double biteAge = (config.BiteIntervalTicks - fish.Cooldown) / (double)HookGame.Rate;
            if (fish.Target >= 0 && fish.Cooldown > 0 && biteAge < .6)
                view.Sample(FishPose.Bite, biteAge);
            else view.Sample(fish.Target >= 0 ? FishPose.Burst : FishPose.Swim, Clock(steps, fish.Id));
        }

        private static double Clock(ulong steps, int id) => (steps % 30000) / (double)HookGame.Rate + id * .137;
    }
}
