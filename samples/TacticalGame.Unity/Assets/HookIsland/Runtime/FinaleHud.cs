using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Objective and round results from confirmed state; buttons only request room actions.</summary>
    public sealed class FinaleHud
    {
        private readonly IslandFishingController room;
        private readonly HudPainter ui;
        public FinaleHud(IslandFishingController room,HudPainter painter){this.room=room;ui=painter;}
        public static string Guidance(Camera view,FinaleConfig config,FinaleState state,Fisher local)
        {
            if(state.Phase!=FinalePhase.Surfaced)
                return "下次优先"+new[]{"南岸","东岸","北岸","西岸"}[Mathf.Clamp(state.Completed,0,3)]+" · 等待浮出";
            var anchor=FinaleRules.Anchor(config,state);var delta=new Vector3(anchor.X-local.X,0,anchor.Z-local.Z);
            float distance=delta.magnitude/1000f;
            if(distance<=1.2f)return "已到岸边 · 朝灯笼出钩";
            var relative=view!=null?view.transform.InverseTransformDirection(delta):new Vector3(delta.x,delta.z,0);
            int direction=(Mathf.RoundToInt(Mathf.Atan2(relative.y,relative.x)*4/Mathf.PI)+8)%8;
            string arrow=new[]{"→","↗","↑","↖","←","↙","↓","↘"}[direction];
            return $"岸边 {arrow} · {distance:0.0} m";
        }
        public void DrawObjective(HudLayout layout)
        {
            var state=room.State;var s=state.Finale;var config=state.Tackle.Config.Finale;var rect=layout.Finale;
            ui.Surface(rect);
            ui.Text(new Rect(rect.x+14,rect.y+8,272,24),$"终局 · 灯笼巨鱼  {Mathf.Min(3,s.Completed+1)} / 3",14,HudPainter.Gold,TextAnchor.MiddleLeft,true);
            string status=s.Phase==FinalePhase.Summoning?(s.Timer>0?$"诱饵已投放 · {(s.Timer+29)/30}s 后出现":"等待岸边空出水域"):
                s.Phase==FinalePhase.Submerged?(s.Timer>0?$"潜入水中 · {(s.Timer+29)/30}s 后换岸":"等待另一处可用岸边"):
                $"挂钩 {FinaleRules.Count(state.Players)} / {FinaleRules.RequiredHooks(config,state.Players)} · 连续 {config.HoldTicks/(float)HookGame.Rate:0.#} 秒";
            ui.Text(new Rect(rect.x+14,rect.y+38,272,26),status,14,HudPainter.White);
            room.TryGetLocalPlayer(out var local);string hint=Guidance(room.ViewCamera,config,s,local);
            ui.Text(new Rect(rect.x+14,rect.y+68,272,22),hint,12,HudPainter.Muted);
            ui.Bar(new Rect(rect.x+14,rect.y+98,272,6),HudPainter.Fraction(s.Progress,config.HoldTicks),HudPainter.Gold);
            ui.Text(new Rect(rect.x+14,rect.y+111,272,18),$"已完成 {s.Completed} 轮 · 诱饵剩余 {state.Commissions.Bait-s.SpentBait}",12,HudPainter.Mint);
            ui.Hint(rect,"人数不足会中断本轮 · 受击、拖拽或耐力耗尽会脱钩");
        }
        public void DrawVictory(HudLayout layout)
        {
            var state=room.State;var rect=layout.Victory;ui.Surface(rect);
            ui.Icon(new Rect(rect.center.x-18,rect.y+20,36,36),HudIcon.Check,HudPainter.Gold);
            ui.Text(new Rect(rect.x+20,rect.y+64,480,36),"本局胜利",27,HudPainter.Gold,TextAnchor.MiddleCenter,true);
            ulong seconds=state.Finale.VictoryTick/(ulong)HookGame.Rate;
            ui.Text(new Rect(rect.x+20,rect.y+108,480,26),$"三轮完成 · 委托 {state.Commissions.Completed}/{state.Tackle.Config.Commissions.Definitions.Count} · 用时 {seconds/60}:{seconds%60:00}",14,HudPainter.Muted,TextAnchor.MiddleCenter);
            ui.Text(new Rect(rect.x+28,rect.y+149,60,24),"玩家",13,HudPainter.Muted);
            ui.Text(new Rect(rect.x+110,rect.y+149,75,24),"鱼获",13,HudPainter.Muted);
            ui.Text(new Rect(rect.x+221,rect.y+149,110,24),"金币收入",13,HudPainter.Muted);
            ui.Text(new Rect(rect.x+375,rect.y+149,110,24),"鱼骨收入",13,HudPainter.Muted);
            foreach(var p in state.Players)
            {
                float y=rect.y+180+p.Slot*27;
                ui.Text(new Rect(rect.x+28,y,60,24),$"P{p.Slot+1}",14,FishingPresentation.PlayerColors[p.Slot]);
                ui.Text(new Rect(rect.x+110,y,75,24),p.Catches.ToString("N0"),14);
                ui.Text(new Rect(rect.x+221,y,110,24),p.CoinsEarned.ToString("N0"),14,HudPainter.Gold);
                ui.Text(new Rect(rect.x+375,y,110,24),p.FishbonesEarned.ToString("N0"),14);
            }
            if(room.Host!=null)
            {
                bool enabled=GUI.enabled;GUI.enabled=enabled && room.CanRestart;
                if(ui.ActionButton(new Rect(rect.x+28,rect.y+322,294,42),"全队开始下一局",HudIcon.Recover,true))room.RestartRound();
                GUI.enabled=enabled;
            }
            else ui.Text(new Rect(rect.x+28,rect.y+322,294,42),"等待房主开始下一局",15,HudPainter.Muted,TextAnchor.MiddleCenter);
            if(ui.ActionButton(new Rect(rect.x+338,rect.y+322,154,42),"离开",HudIcon.Close))room.LeaveRoom();
        }
    }
}
