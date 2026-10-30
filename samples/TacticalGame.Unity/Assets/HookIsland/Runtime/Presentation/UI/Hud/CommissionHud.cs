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
