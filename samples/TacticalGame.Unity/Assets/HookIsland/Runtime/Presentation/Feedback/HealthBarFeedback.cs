using System.Collections.Generic;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>
    /// 确认生命的显示时序：受伤先标出损失再缩短拖尾；治疗先显示绿色增量，再让常规填充追上。
    /// 时间只影响显示，既不插值真实生命，也不向模拟或网络回写任何值。
    /// </summary>
    public sealed class HealthBarFeedback
    {
        public readonly struct Frame
        {
            public readonly float Fill, WhiteFrom, WhiteTo;
            public readonly float HealingFrom, HealingTo;
            public Frame(float fill, float whiteFrom, float whiteTo)
                :this(fill,whiteFrom,whiteTo,whiteFrom,whiteFrom){}
            public Frame(float fill,float whiteFrom,float whiteTo,float healingFrom,float healingTo)
            {Fill=fill;WhiteFrom=whiteFrom;WhiteTo=whiteTo;HealingFrom=healingFrom;HealingTo=healingTo;}
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
            // 首次观察、读档、复活、换装或长时间隐藏均建立基线，不把恢复后的状态当成刚刚发生的治疗。
            if (!visible || maximum != player.Maximum || (player.Health<=0 && health>0) || now < player.LastObserved || now - player.LastObserved > .5f)
            {
                player.From = player.Target = target; player.StartedAt = now;
                player.Delay = 0; player.FlashUntil = now;
            }
            else if (health < player.Health)
            {
                float displayed = player.Fill(now);
                // 治疗过程中受伤立即取消绿色；损失以已经确认的生命为起点，不把未追上的白色当作真实生命。
                if(player.From<player.Target)displayed=player.Target;
                // 连续伤害重定向已有拖尾，不反复停留在延迟阶段。
                bool draining = displayed > player.Target + .0001f;
                player.From = Mathf.Max(displayed, target); player.Target = target;
                player.StartedAt = now; player.Delay = draining ? 0 : .1f; player.FlashUntil = now + .1f;
            }
            else if(health>player.Health)
            {
                float displayed=Mathf.Min(player.Fill(now),player.Target);
                bool filling=displayed<player.Target-.0001f;
                player.From=displayed;player.Target=target;player.StartedAt=now;
                // 先保留 0.15 秒完整绿色增量，再用 0.32 秒让常规填充追上；连续治疗不反复停顿。
                player.Delay=filling?0:.15f;player.FlashUntil=now;
            }
            player.Health = health; player.Maximum = maximum; player.LastObserved = now;
            float fill = player.Fill(now);
            return new Frame(fill,target,now<player.FlashUntil?fill:target,Mathf.Min(fill,target),target);
        }
        public void Reset() { players.Clear(); identity = null; initialized = false; }
    }
}
