using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>A brief trailing segment makes consumption visible without delaying the actual stamina fill.</summary>
    public sealed class StaminaDrainFeedback
    {
        private float trail, previous, lastTime, holdUntil;
        private bool initialized;
        public float Sample(float remaining, float now)
        {
            remaining = Mathf.Clamp01(remaining);
            if (!initialized || now < lastTime || now - lastTime > .5f)
            { initialized = true; trail = previous = remaining; lastTime = now; holdUntil = now; return trail; }
            float dt = now - lastTime;
            if (remaining > previous) trail = remaining;
            else if (remaining < previous && trail <= previous + .0001f) holdUntil = now + .18f;
            if (now > holdUntil) trail = Mathf.MoveTowards(trail, remaining, dt * .9f);
            previous = remaining; lastTime = now;
            return Mathf.Max(remaining, trail);
        }
    }
}
