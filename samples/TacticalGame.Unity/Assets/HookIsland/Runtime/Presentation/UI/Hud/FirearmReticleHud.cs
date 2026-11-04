using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Local aiming feedback only. Weapon ownership comes from confirmed state; spread may be predicted.</summary>
    public sealed class FirearmReticleHud
    {
        private readonly IslandFishingController room;
        private readonly HudPainter ui;
        private static readonly Color Outline=new Color(.015f,.045f,.055f,.85f);

        public FirearmReticleHud(IslandFishingController room,HudPainter ui){this.room=room;this.ui=ui;}

        public static bool CanShow(HookState state,Fisher player,bool blocked)=>!blocked && state!=null &&
            !state.RoundFinished && !player.WaitingToJoin && ActiveToolView.Firearm(state,player)!=null &&
            FirearmRules.CanMaintain(player,SwimRingCatalog.Equipped(state.Tackle.Inventory,player.Slot)!=null);

        // Use the same screen sample as FishingPlayerInput, including a non-fullscreen camera viewport.
        public static Vector2 Position(bool centerAim,Rect viewport,Vector2 pointer,float screenHeight,float scale)
        {
            var point=centerAim?viewport.center:pointer;
            return new Vector2(point.x,screenHeight-point.y)/scale;
        }
        public static float Gap(FirearmSpreadDefinition profile,int angle)=>Mathf.Lerp(4,16,
            Mathf.InverseLerp(profile.MinimumHalfAngle,profile.MaximumConfiguredHalfAngle,angle));

        public void Draw(Fisher player,float scale)
        {
            if(Event.current.type!=EventType.Repaint || room.ViewCamera==null)return;
            bool blocked=!room.Playing || room.ShopOpen || room.Equipment?.InventoryOpen==true ||
                room.Overlay?.MenuOpen==true || room.Construction?.Open==true;
            if(!CanShow(room.State,player,blocked))return;
            var viewport=room.ViewCamera.pixelRect;
            if(!room.CenterAim && (!viewport.Contains(Input.mousePosition) || room.Overlay?.PointerOverHud==true))return;
            var weapon=ActiveToolView.Firearm(room.State,player);
            if(room.Scoped)
            {ui.Scope(new Rect(viewport.x/scale,(Screen.height-viewport.yMax)/scale,viewport.width/scale,viewport.height/scale),weapon.ScopeMagnification);return;}
            var equipped=room.State.Tackle.Inventory.ActiveItem(player.Slot).Value;
            var motion=room.Client?.Session?.InspectPredictedState();
            int spread=motion!=null && player.Slot<motion.Players.Count?
                motion.Players[player.Slot].Firearm.Spread(equipped.Type):player.Firearm.Spread(equipped.Type);
            var center=Position(room.CenterAim,viewport,Input.mousePosition,Screen.height,scale);
            float gap=Gap(weapon.Spread,spread),length=6,thickness=2;
            Stroke(new Rect(center.x-gap-length,center.y-thickness/2,length,thickness));
            Stroke(new Rect(center.x+gap,center.y-thickness/2,length,thickness));
            Stroke(new Rect(center.x-thickness/2,center.y-gap-length,thickness,length));
            Stroke(new Rect(center.x-thickness/2,center.y+gap,thickness,length));
            Stroke(new Rect(center.x-1,center.y-1,2,2));
        }
        private void Stroke(Rect rect)
        {
            ui.Surface(new Rect(rect.x-1,rect.y-1,rect.width+2,rect.height+2),Outline);
            ui.Surface(rect,Color.white);
        }
    }
}
