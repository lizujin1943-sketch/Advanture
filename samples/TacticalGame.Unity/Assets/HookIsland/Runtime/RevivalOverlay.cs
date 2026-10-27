using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Critical rescue and defeat choices, rendered from confirmed state.</summary>
    public sealed class RevivalOverlay : MonoBehaviour
    {
        private IslandFishingController room;
        private RevivalController input;
        private Font font;
        private HudPainter ui;
        private void Awake()
        {room=GetComponent<IslandFishingController>();input=GetComponent<RevivalController>();font=Font.CreateDynamicFontFromOSFont(new[]{"PingFang SC","Microsoft YaHei","Noto Sans CJK SC","Arial"},24);}
        public void Draw(float width,float height)
        {
            if(!room.Playing || room.State?.Tackle==null || room.State.Won || !room.TryGetLocalPlayer(out var player) || GetComponent<FishingOverlay>().MenuOpen)return;
            if(ui==null)ui=new HudPainter(font);
            var config=room.State.Tackle.Config.Revival;
            if(player.Vitals.Downed)
            {
                bool failed=room.State.GameOver,dead=player.Revival.Dead;
                float panelHeight=dead || failed?236:318;
                var rect=new Rect(width/2-220,(height-panelHeight)/2,440,panelHeight);ui.Surface(rect);
                ui.Icon(new Rect(rect.center.x-16,rect.y+20,32,32),dead?HudIcon.Skull:HudIcon.Rescue,HudPainter.Coral);
                ui.Text(new Rect(rect.x+20,rect.y+61,400,32),failed?"航行失败":dead?"等待复活":"等待救援",23,HudPainter.White,TextAnchor.MiddleCenter,true);
                if(failed)
                {
                    bool solo=RevivalSystem.IsSolo(room.State.Players);
                    ui.Text(new Rect(rect.x+20,rect.y+106,400,28),solo?"金币不足以复活":"全队阵亡 · 金币不足以复活",14,HudPainter.Muted,TextAnchor.MiddleCenter);
                    if(room.Host!=null)
                    {
                        bool enabled=GUI.enabled;GUI.enabled=enabled && room.CanRestart;
                        if(ui.ActionButton(new Rect(rect.x+24,rect.y+170,244,42),solo?"重新开始":"全队重新开始",HudIcon.Recover,true))room.RestartRound();
                        GUI.enabled=enabled;
                    }
                    else ui.Text(new Rect(rect.x+24,rect.y+170,244,42),"等待房主重新开始",15,HudPainter.Muted,TextAnchor.MiddleCenter);
                    if(ui.ActionButton(new Rect(rect.x+280,rect.y+170,136,42),"离开",HudIcon.Close))room.LeaveRoom();
                }
                else if(dead)
                {
                    DrawPayment(rect,player,101);
                }
                else
                {
                    ui.Text(new Rect(rect.x+20,rect.y+103,400,36),$"{Mathf.Max(0,(config.DownedTicks-player.Revival.DownedTicks)/30f):0.0}s",25,HudPainter.Coral,TextAnchor.MiddleCenter,true);
                    ui.Text(new Rect(rect.x+20,rect.y+145,400,28),player.Swimming.InWater?"等待队友钩回岸边":"队友靠近按住 R 救起 · 不扣金币",14,HudPainter.Muted,TextAnchor.MiddleCenter);
                    if(player.Revival.Rescuer>0)ui.Bar(new Rect(rect.x+40,rect.y+178,360,8),player.Revival.RescueTicks/(float)config.RescueTicks,HudPainter.Mint,true);
                    DrawPayment(rect,player,192);
                }
                return;
            }
            if(room.ShopOpen || room.Equipment?.InventoryOpen==true || room.Construction?.Open==true)return;
            int target=input.DisplayTarget();if(target<0)return;
            var victim=room.State.Players[target];var rescue=new Rect(width/2-140,height-184,280,64);ui.Surface(rescue);
            ui.Icon(new Rect(rescue.x+14,rescue.y+9,24,24),HudIcon.Rescue,HudPainter.Mint);
            ui.Key(new Rect(rescue.x+49,rescue.y+10,22,22),"R");
            ui.Text(new Rect(rescue.x+83,rescue.y+7,185,29),victim.Revival.Rescuer==player.Slot+1?$"救援中 · P{target+1}":victim.Revival.Rescuer>0?$"P{victim.Revival.Rescuer} 正在施救":$"按住救起 P{target+1}",14);
            ui.Bar(new Rect(rescue.x+16,rescue.y+45,248,6),victim.Revival.RescueTicks/(float)config.RescueTicks,HudPainter.Mint);
        }
        private void DrawPayment(Rect rect,Fisher player,float y)
        {
            ui.Text(new Rect(rect.x+20,rect.y+y,400,26),$"持有 {player.Coins:N0} · 复活需要 {player.Revival.DeathCost:N0} 金币",14,HudPainter.Muted,TextAnchor.MiddleCenter);
            bool enabled=GUI.enabled;GUI.enabled=enabled && !input.Pending && player.Revival.HasQuote && player.Coins>=player.Revival.DeathCost;
            if(ui.ActionButton(new Rect(rect.x+80,rect.y+y+36,280,42),input.Pending?"正在复活…":player.Coins<player.Revival.DeathCost?"金币不足":$"{player.Revival.DeathCost:N0} · 立即复活",HudIcon.Coin,true))input.RequestRespawn();
            GUI.enabled=enabled;
            ui.Text(new Rect(rect.x+16,rect.y+y+85,408,24),input.Message??"返回安全平台 · 保留装备",12,HudPainter.Muted,TextAnchor.MiddleCenter);
        }
        private void OnDestroy(){ui?.Dispose();if(font!=null)Destroy(font);}
    }
}
