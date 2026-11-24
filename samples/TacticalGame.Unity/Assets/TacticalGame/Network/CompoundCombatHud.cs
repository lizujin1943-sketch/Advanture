using System.Linq;
using Advanture.HookIsland;
using Advanture.HookIsland.Simulation;
using Advanture.Tactical.Network;
using Advanture.Tactical.Simulation;
using UnityEngine;

namespace Advanture.TacticalGame.NetworkView
{
    /// <summary>将基地会话的确认快照交给沉钟镇的 HUD。打开面板不会触碰战斗状态。</summary>
    internal sealed class CompoundCombatHud
    {
        private readonly NetworkYardController room;
        private readonly HudPainter ui;
        private readonly LocalHealthHud health;
        private readonly HealthBarFeedback healthFeedback=new HealthBarFeedback();
        private readonly ItemIconPainter icons=new ItemIconPainter();
        private static readonly int RifleIcon=FirearmCatalog.All.ToList().FindIndex(gun=>gun.Id=="ak47");
        private uint lastHits;
        private float hitUntil;

        public CompoundCombatHud(NetworkYardController room,HudPainter ui)
        {this.room=room;this.ui=ui;health=new LocalHealthHud(ui);}

        public void Reset(){healthFeedback.Reset();lastHits=0;hitUntil=0;}

        public void Draw(HudLayout layout,float scale)
        {
            var state=room.ConfirmedState;
            var player=room.LocalPlayer.Value;
            var c=player.Combat;
            float now=Time.unscaledTime;
            healthFeedback.BeginFrame(room.Client,0,state.Steps,now,true);
            var team=layout.Squad(state.Players.Count-1);
            int index=0;
            foreach(var other in state.Players)
            {
                if(other.Slot==player.Slot)continue;
                var pos=new Vector2(team.x+80*index++,team.y);
                ui.SquadMember(pos,other.Slot,healthFeedback.Observe(other.Slot,other.Combat.Health,100));
                if(!other.Combat.IsAlive)ui.Icon(new Rect(pos.x+48,pos.y+3,16,16),HudIcon.Skull);
            }
            health.Draw(layout.LocalHealth,c.Health,100,c.IsAlive?"守卫 · P"+(player.Slot+1):"已阵亡",
                c.IsAlive?(HudIcon?)null:HudIcon.Skull,healthFeedback.Observe(player.Slot,c.Health,100),now);
            FirearmAmmoHud.DrawReadout(ui,layout.FirearmAmmo,c.Ammo,c.Reserve,SecondsLeft(c.ReloadUntilTick,state.Steps),"备弹耗尽 · 前往武器库补给");
            if(!c.IsAlive)
            {
                var panel=new Rect(layout.Width/2-130,layout.Height/2-42,260,84);ui.Surface(panel);
                ui.Text(panel,$"{SecondsLeft(c.RespawnAtTick,state.Steps):0.0} 秒后在广场重生",20,HudPainter.White,TextAnchor.MiddleCenter);
                return;
            }
            if(c.Hits>lastHits)hitUntil=now+.18f;
            lastHits=c.Hits;
            var viewport=room.ViewCamera.pixelRect;
            var center=FirearmReticleHud.Position(true,viewport,Vector2.zero,Screen.height,scale);
            float kick=c.ShotSequence==0?0:Mathf.Clamp01(1-(state.Steps-c.LastShotTick)/4f);
            FirearmReticleHud.DrawCross(ui,center,4+4*kick);
            if(now<hitUntil)ui.Surface(new Rect(center.x-2,center.y-2,4,4),HudPainter.Gold);
            DrawDepots(layout,player);
        }

        private static float SecondsLeft(ulong until,ulong now)=>until>now?(until-now)/(float)TacticalClient.TickRate:0;

        private void DrawDepots(HudLayout layout,TacticalPlayer player)
        {
            var map=room.Map;
            if(map.NearbyAmmoDepot(player)>=0)
            {
                var rect=new Rect(layout.Width/2-85,layout.Height*.59f,170,34);
                ui.Surface(rect);ui.Icon(new Rect(rect.x+8,rect.y+6,22,22),HudIcon.Shop,HudPainter.Gold);
                if(player.Combat.Reserve<CombatState.ReserveCapacity)
                {
                    ui.Key(new Rect(rect.x+37,rect.y+7,22,20),"E");
                    ui.Text(new Rect(rect.x+67,rect.y,100,34),"补满备弹");
                }
                else ui.Text(new Rect(rect.x+39,rect.y,124,34),"备弹已满");
                return;
            }
            var camera=room.ViewCamera;
            foreach(var depot in map.AmmoDepots)
            {
                var point=new Vector3(depot.X*.001f,map.GroundHeight(depot.X,depot.Z)*.001f+1.1f,depot.Z*.001f);
                var direction=point-camera.transform.position;
                if(direction.sqrMagnitude>24*24)continue;
                var p=camera.WorldToViewportPoint(point);
                if(p.z<=0 || p.x<.08f || p.x>.92f || p.y<.14f || p.y>.86f ||
                    Physics.Linecast(camera.transform.position,point,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))continue;
                var rect=new Rect(layout.Width*p.x-46,layout.Height*(1-p.y)-14,92,28);
                ui.Surface(rect);ui.Icon(new Rect(rect.x+7,rect.y+5,18,18),HudIcon.Shop,HudPainter.Gold);
                ui.Text(new Rect(rect.x+31,rect.y,54,28),$"{direction.magnitude:0} m",14,HudPainter.White,TextAnchor.MiddleCenter);
            }
        }

        public void DrawEquipment(Rect panel,TacticalPlayer player,bool backpack)
        {
            float x=panel.x+24,y=panel.y+76;
            var c=player.Combat;
            ui.Text(new Rect(x,y,570,30),backpack?"随身弹药":"广场守卫 · P"+(player.Slot+1),20,HudPainter.White,TextAnchor.MiddleLeft,true);
            var card=new Rect(x,y+44,268,107);
            ui.Surface(card,HudPainter.Track);
            if(RifleIcon>=0)icons.Draw(new Rect(card.x+12,card.y+20,86,67),
                backpack?ItemKind.Ammunition:ItemKind.Firearm,backpack?FirearmCatalog.Get(RifleIcon).AmmoType:RifleIcon);
            ui.Text(new Rect(card.x+112,card.y+18,144,32),backpack?"步枪备弹":"AK-47",20,HudPainter.White,TextAnchor.MiddleLeft,true);
            ui.Text(new Rect(card.x+112,card.y+54,150,28),backpack?$"{c.Reserve} / {CombatState.ReserveCapacity}":"主武器 · 手持",15,HudPainter.Mint);
            float details=x+296;
            if(backpack)
            {
                ui.Text(new Rect(details,card.y,290,32),$"弹匣内   {c.Ammo} 发");
                ui.Text(new Rect(details,card.y+39,290,60),"在武器库补给点按 E 补满备弹。",15,HudPainter.Muted);
            }
            else
            {
                ui.Text(new Rect(details,card.y,290,30),$"生命   {c.Health} / 100");
                ui.Bar(new Rect(details,card.y+34,244,7),c.Health/100f,HudPainter.Health);
                ui.Text(new Rect(details,card.y+55,290,40),$"击杀  {c.Kills}     阵亡  {c.Deaths}",15,HudPainter.Muted);
            }
        }
    }
}
