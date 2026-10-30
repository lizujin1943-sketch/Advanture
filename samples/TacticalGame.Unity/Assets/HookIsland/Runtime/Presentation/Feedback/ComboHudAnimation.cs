using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Presentation clock only. Repeated GUI passes and restored history never replay a harvest.</summary>
    public sealed class ComboHudAnimation
    {
        private object identity;
        private ulong epoch,tick;
        private int displayCount,bonus;
        private float increasedAt=-100,brokenAt=-100,strength,colorProgress;
        private bool initialized,visible;
        public void Observe(object session,ulong round,ulong confirmedTick,int count,ComboConfig config,float now)
        {
            bool fresh=!initialized || !ReferenceEquals(identity,session) || epoch!=round || confirmedTick<tick;
            if(!fresh && confirmedTick==tick)return;
            initialized=true;identity=session;epoch=round;tick=confirmedTick;
            bool show=config!=null && config.Enabled && count>=config.DisplayStartCount;
            if(fresh){increasedAt=brokenAt=-100;visible=show;}
            else if(!show && visible)brokenAt=now;
            if(show)
            {
                displayCount=count;bonus=config.BonusPercent(count);
                int maximum=config.GoldBonusPercent[config.GoldBonusPercent.Count-1];
                strength=maximum>0?Mathf.Clamp01(bonus/(float)maximum):0;
                int first=config.BonusPercent(config.DisplayStartCount);
                colorProgress=maximum>first?Mathf.InverseLerp(first,maximum,bonus):maximum>0?1:0;
            }
            visible=show;
        }
        public void ConfirmIncrease(int count,float now)
        {
            // The final confirmed state still controls visibility. An older success in a
            // catch-up batch must not revive a combo that was already broken later in it.
            if(visible && count==displayCount)increasedAt=now;
        }
        public void Reset()
        {identity=null;initialized=visible=false;displayCount=bonus=0;increasedAt=brokenAt=-100;strength=colorProgress=0;}
        public ComboHudFrame Sample(float now)
        {
            float alpha=visible?1:Mathf.Clamp01(1-(now-brokenAt)/.24f);
            float t=Mathf.Clamp01((now-increasedAt)/.42f);
            // Fast rise, small undershoot, then settle. Strength is capped by the reward curve.
            float wave=t<1?Mathf.Sin(t*Mathf.PI*2)*Mathf.Pow(1-t,2):0;
            float flash=t<1?Mathf.Pow(1-t,2):0;
            return new ComboHudFrame(displayCount,bonus,strength,colorProgress,alpha,
                1+wave*Mathf.Lerp(.36f,.72f,strength),-wave*Mathf.Lerp(8,17,strength),flash);
        }
    }
    public readonly struct ComboHudFrame
    {
        public readonly int Count,Bonus;
        public readonly float Strength,ColorProgress,Alpha,Scale,OffsetY,Flash;
        public float FontSize=>Mathf.Lerp(30,50,Strength);
        public ComboHudFrame(int count,int bonus,float strength,float colorProgress,float alpha,float scale,float offsetY,float flash)
        {Count=count;Bonus=bonus;Strength=strength;ColorProgress=colorProgress;Alpha=alpha;Scale=scale;OffsetY=offsetY;Flash=flash;}
    }
}
