using System.Collections.Generic;
using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    public sealed class MerchantHud
    {
        private readonly IslandFishingController room;
        private readonly HudPainter ui;
        private readonly List<Rect> labels=new List<Rect>();
        private static readonly string[] Arrows={"→","↗","↑","↖","←","↙","↓","↘"};
        public MerchantHud(IslandFishingController room,HudPainter painter){this.room=room;ui=painter;}
        public static string Name(FishingConfig config,int id)
        {
            if(id<=0 || !MerchantRules.Exists(config,id))return "平台商店";
            var p=MerchantRules.Position(config,id);
            string side=Mathf.Abs(p.X)>=Mathf.Abs(p.Z)?(p.X>=0?"东":"西"):(p.Z>=0?"北":"南");
            return side+"侧补给船";
        }
        public static string Guidance(Camera view,FishingConfig config,int id,Fisher local)
        {
            var p=MerchantRules.Position(config,id);var delta=new Vector3(p.X-local.X,0,p.Z-local.Z);
            var relative=view!=null?view.transform.InverseTransformDirection(delta):new Vector3(delta.x,delta.z,0);
            int direction=(Mathf.RoundToInt(Mathf.Atan2(relative.y,relative.x)*4/Mathf.PI)+8)%8;
            return $"{Arrows[direction]} {Name(config,id)} · {delta.magnitude/1000f:0.0} m";
        }
        public static bool LabelFits(Rect rect,HudLayout layout,int players,bool materials,bool building)
        {
            return rect.x>=12 && rect.y>=12 && rect.xMax<=layout.Width-12 && rect.yMax<=layout.Height-12 &&
                !rect.Overlaps(layout.Squad(players)) && !rect.Overlaps(layout.Resources(materials)) && !rect.Overlaps(layout.Menu) &&
                !rect.Overlaps(layout.CommissionPanel(true)) && !rect.Overlaps(layout.Merchants) &&
                !rect.Overlaps(layout.Vitals) && !rect.Overlaps(layout.Tools) && (!building || !rect.Overlaps(layout.Construction));
        }
        public void Draw(HudLayout layout,float scale,Fisher local)
        {
            var config=room.Map.Fishing;if(config==null || room.ViewCamera==null)return;
            labels.Clear();
            foreach(var ship in config.Merchants.Definitions)
            {
                var screen=room.ViewCamera.WorldToScreenPoint(new Vector3(ship.X/1000f,2.6f,ship.Z/1000f));
                var rect=new Rect(screen.x/scale-88,layout.Height-screen.y/scale-14,176,28);
                bool visible=screen.z>0 && LabelFits(rect,layout,room.State.Players.Count,room.Map.Platform!=null,room.Construction?.Open==true);
                if(visible)foreach(var used in labels)if(rect.Overlaps(used)){visible=false;break;}
                bool near=room.CanShopAt(ship.Id);
                if(visible)
                {
                    labels.Add(rect);ui.Surface(rect);
                    ui.Text(rect,Name(config,ship.Id),13,near?HudPainter.Gold:HudPainter.Muted,TextAnchor.MiddleCenter);
                }
                else
                {
                    var row=new Rect(layout.Merchants.x,layout.Merchants.y+(ship.Id-1)*44,layout.Merchants.width,40);ui.Surface(row);
                    ui.Text(new Rect(row.x+8,row.y+1,row.width-16,22),Guidance(room.ViewCamera,config,ship.Id,local),12,near?HudPainter.Gold:HudPainter.White);
                    ui.Text(new Rect(row.x+8,row.y+23,row.width-16,16),near?"已可购物 · E":"铺设平台后接近",11,HudPainter.Muted);
                }
            }
        }
    }
}
