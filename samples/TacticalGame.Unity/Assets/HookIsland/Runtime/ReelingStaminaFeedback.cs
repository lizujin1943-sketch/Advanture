using System.Collections.Generic;
using Advanture.HookIsland.Simulation;

namespace Advanture.HookIsland
{
    public enum ReelingFeedbackMode { Hidden, Reeling, Escaped }

    /// <summary>Tracks short-lived presentation feedback from confirmed escape counters.</summary>
    public sealed class ReelingStaminaFeedback
    {
        private const double EscapeNoticeSeconds = 1.8;
        private readonly Dictionary<ushort, Observation> players = new Dictionary<ushort, Observation>();

        private struct Observation
        {
            public ulong Tick;
            public int Escapes;
            public double NoticeUntil;
        }

        public void Reset() { players.Clear(); }

        public ReelingFeedbackMode Observe(Fisher player, ulong tick, double now)
        {
            Observation previous;
            if (!players.TryGetValue(player.Slot, out previous) || tick < previous.Tick || player.Escapes < previous.Escapes)
                previous = new Observation { Escapes = player.Escapes, NoticeUntil = double.NegativeInfinity };
            if (player.Escapes > previous.Escapes) previous.NoticeUntil = now + EscapeNoticeSeconds;

            bool reeling = player.Phase == HookPhase.Returning && (player.FishId >= 0 || player.HookedPiranha > 0);
            if (reeling) previous.NoticeUntil = double.NegativeInfinity;
            previous.Escapes = player.Escapes;
            previous.Tick = tick;
            players[player.Slot] = previous;
            return reeling ? ReelingFeedbackMode.Reeling : now < previous.NoticeUntil ? ReelingFeedbackMode.Escaped : ReelingFeedbackMode.Hidden;
        }
    }
}
