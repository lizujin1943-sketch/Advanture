using System.Collections.Generic;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Confirmed HP presentation: a white hit segment, then a short red drain. Never changes HP.</summary>
    public sealed class HealthBarFeedback
    {
        public readonly struct Frame
        {
            public readonly float Fill, WhiteFrom, WhiteTo;
            public Frame(float fill, float whiteFrom, float whiteTo)
            { Fill = fill; WhiteFrom = whiteFrom; WhiteTo = whiteTo; }
        }
        private sealed class Player
        {
            public int Health, Maximum;
            public float Target, From, StartedAt, Delay, FlashUntil, LastObserved;
            public float Fill(float now) => Mathf.Lerp(From, Target, Mathf.SmoothStep(0, 1, (now - StartedAt - Delay) / .32f));
        }
        private readonly Dictionary<ushort, Player> players = new Dictionary<ushort, Player>();
        private object identity;
        private ulong epoch, tick;
        private float now;
        private bool initialized, visible;

        public void BeginFrame(object session, ulong round, ulong confirmedTick, float time, bool show)
        {
            if (!initialized || !ReferenceEquals(identity, session) || epoch != round || confirmedTick < tick) Reset();
            initialized = true; identity = session; epoch = round; tick = confirmedTick; now = time; visible = show;
        }
        public Frame Observe(ushort slot, int health, int maximum, bool waiting = false)
        {
            if (waiting) { players.Remove(slot); return default; }
            float target = HudPainter.Fraction(health, maximum);
            if (!players.TryGetValue(slot, out var player)) players[slot] = player = new Player { Maximum = -1 };
            // Restoration, hidden UI, healing and changed equipment establish a baseline, never a fake hit.
            if (!visible || maximum != player.Maximum || health > player.Health || now < player.LastObserved || now - player.LastObserved > .5f)
            {
                player.From = player.Target = target; player.StartedAt = now;
                player.Delay = 0; player.FlashUntil = now;
            }
            else if (health < player.Health)
            {
                float displayed = player.Fill(now);
                // Continuous damage can retarget an ongoing drain without repeatedly holding it in place.
                bool draining = displayed > player.Target + .0001f;
                player.From = Mathf.Max(displayed, target); player.Target = target;
                player.StartedAt = now; player.Delay = draining ? 0 : .1f; player.FlashUntil = now + .1f;
            }
            player.Health = health; player.Maximum = maximum; player.LastObserved = now;
            float fill = player.Fill(now);
            return new Frame(fill, target, now < player.FlashUntil ? fill : target);
        }
        public void Reset() { players.Clear(); identity = null; initialized = false; }
    }
}
