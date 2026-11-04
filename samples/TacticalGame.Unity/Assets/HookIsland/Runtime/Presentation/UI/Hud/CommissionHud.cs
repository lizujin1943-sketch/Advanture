using System.Linq;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Public objective display reads only confirmed room state.</summary>
    public sealed class CommissionHud
    {
        private readonly IslandFishingController room;
        private readonly HudPainter ui;
        public CommissionHud(IslandFishingController room,HudPainter painter){this.room=room;ui=painter;}
        /// <summary>常驻两行摘要；详细诱饵、海域和进度条留在 J 抽屉中，不改变任一条件完成的委托规则。</summary>
        public void DrawSummary(Rect rect)
        {
            var state=room.State;var config=state?.Tackle?.Config.Commissions;
            if(config==null || !config.Enabled)return;
            var progress=state.Commissions;bool complete=progress.Completed>=config.Definitions.Count;
            ui.Slanted(new Rect(rect.x,rect.y+8,12,27),HudPainter.Gold);
            ui.FloatingText(new Rect(rect.x+18,rect.y,222,40),complete?"委托全部完成":$"渔业委托 {progress.Completed+1}/{config.Definitions.Count}",24,Color.white);
            string detail="诱饵已备齐";
            if(!complete)
            {
                var goal=config.Definitions[progress.Completed];var current=progress.Entries[progress.Completed];
                detail=$"鱼获 {current.Fish}/{goal.Fish}  或  稀有 {current.Rare}/{goal.Rare}";
            }
            ui.FloatingText(new Rect(rect.x+18,rect.y+38,rect.width-18,26),detail,17,Color.white,bold:false);
        }
        public void Draw(Rect rect)
        {
            var state=room.State;var config=state?.Tackle?.Config.Commissions;
            if(config==null || !config.Enabled)return;
            var progress=state.Commissions;bool complete=progress.Completed==config.Definitions.Count;
            bool ocean=state.Tackle.Config.Ocean.Enabled;ui.Surface(rect);
            ui.Text(new Rect(rect.x+14,rect.y+8,272,24),complete?"渔业委托 · 全部完成":$"渔业委托 {progress.Completed+1} / {config.Definitions.Count}",14,HudPainter.White,TextAnchor.MiddleLeft,true);
            if(complete)
                ui.Text(new Rect(rect.x+14,rect.y+34,272,26),"诱饵已备齐",14,HudPainter.Mint);
            else
            {
                var goal=config.Definitions[progress.Completed];var current=progress.Entries[progress.Completed];
                ui.Text(new Rect(rect.x+14,rect.y+34,272,24),$"鱼获 {current.Fish}/{goal.Fish}   或   稀有 {current.Rare}/{goal.Rare}",13,HudPainter.Muted);
                float fraction=Mathf.Max(HudPainter.Fraction(current.Fish,goal.Fish),HudPainter.Fraction(current.Rare,goal.Rare));
                ui.Bar(new Rect(rect.x+14,rect.y+63,272,4),fraction,HudPainter.Mint);
            }
            ui.Text(new Rect(rect.x+14,rect.y+76,272,22),$"团队诱饵  {progress.Bait-state.Finale.SpentBait} / {config.Definitions.Sum(d=>d.Bait)}",13,HudPainter.Gold);
            if(ocean)
            {
                string phase=$"海域 {OceanRules.Stage(progress.Completed)+1}";
                if(state.Ocean.QuietUntil>state.Steps)phase+=$" · 恢复窗口 {(state.Ocean.QuietUntil-state.Steps+29)/30}s";
                else if(state.Ocean.Visits.Any(v=>v.Active))phase+=" · 鱼群靠近";
                ui.Text(new Rect(rect.x+14,rect.y+103,272,22),phase,13,HudPainter.Mint);
            }
            ui.Hint(rect,"全队共享进度 · 任一条件达成即获得诱饵 · 不消耗捕鱼收益");
        }
    }
}
