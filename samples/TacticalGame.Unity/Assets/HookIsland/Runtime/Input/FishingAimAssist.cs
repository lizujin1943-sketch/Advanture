using Advanture.HookIsland.Simulation;
using UnityEngine;

namespace Advanture.HookIsland
{
    /// <summary>Screen-space pointing aid only; confirmed simulation still decides hits.</summary>
    internal static class FishingAimAssist
    {
        internal static Vector3 Resolve(IslandFishingController room,Camera viewCamera,Vector3 pointer,Vector3 aim)
        {
            // A small screen-space assist makes clicking an airborne fish match its XZ hit shape.
            // The synchronized simulation still validates timing, range and collision.
            float nearest=27f*27f;
            if(room.State!=null && !room.HoldingMelee && room.Construction?.Open!=true)foreach(var f in room.State.Fish)
            {
                if(f.Phase!=FishPhase.Jumping)continue;
                var screen=viewCamera.WorldToScreenPoint(new Vector3(f.X/1000f,f.Y/1000f,f.Z/1000f));
                float d=((Vector2)screen-(Vector2)pointer).sqrMagnitude;
                if(screen.z>0 && d<14f*14f && d<nearest){nearest=d;aim=new Vector3(f.X/1000f,.75f,f.Z/1000f);}
            }
            if(room.State!=null && !room.HoldingMelee && room.Construction?.Open!=true)foreach(var d in room.State.Driftwood)
            {
                if(d.Phase!=DriftwoodPhase.Floating)continue;
                var screen=viewCamera.WorldToScreenPoint(new Vector3(d.X/1000f,.13f,d.Z/1000f));
                float distanceToPointer=((Vector2)screen-(Vector2)pointer).sqrMagnitude;
                if(screen.z>0 && distanceToPointer<18f*18f && distanceToPointer<nearest)
                {nearest=distanceToPointer;aim=new Vector3(d.X/1000f,.75f,d.Z/1000f);}
            }
            if(room.State!=null && !room.HoldingMelee && room.Construction?.Open!=true)foreach(var chest in room.State.Treasure.Chests)
            {
                if(chest.Phase==TreasurePhase.Waiting || chest.Phase==TreasurePhase.Landed)continue;
                var screen=viewCamera.WorldToScreenPoint(new Vector3(chest.X/1000f,.4f,chest.Z/1000f));
                float d=((Vector2)screen-(Vector2)pointer).sqrMagnitude;
                if(screen.z>0 && d<18f*18f && d<nearest){nearest=d;aim=new Vector3(chest.X/1000f,.75f,chest.Z/1000f);}
            }
            if(room.State!=null && !room.HoldingMelee && room.Construction?.Open!=true)foreach(var friend in room.State.Players)
            {
                if(friend.WaitingToJoin || friend.Revival.Dead || friend.Slot==room.LocalSlot || (!friend.Swimming.InWater && (friend.Unavailable || !room.State.RoomRules.FriendlyPranksEnabled || friend.Shopping || friend.ProtectionTicks>0)) || friend.PulledBy>=0 || friend.HookedPlayer>=0)continue;
                var screen=viewCamera.WorldToScreenPoint(room.Presentation.PlayerPosition(friend.Slot)+Vector3.up*(room.Map.Platform==null?1.2f:.6f));
                float d=((Vector2)screen-(Vector2)pointer).sqrMagnitude;
                if(screen.z>0 && d<nearest){nearest=d;aim=new Vector3(friend.X/1000f,.75f,friend.Z/1000f);}
            }
            if(room.State!=null && !room.HoldingMelee && room.Construction?.Open!=true)foreach(var f in room.State.Enemies.Piranhas)
            {
                if(f.Owner>=0 || f.RespawnTicks>0 || f.Escape.Active)continue;
                var position=new Vector3(f.X/1000f,.12f,f.Z/1000f);var screen=viewCamera.WorldToScreenPoint(position);
                float d=((Vector2)screen-(Vector2)pointer).sqrMagnitude;
                if(screen.z>0 && d<18f*18f && d<nearest){nearest=d;aim=new Vector3(position.x,.75f,position.z);}
            }
            if(room.State!=null && !room.HoldingMelee && room.Construction?.Open!=true && room.Map.Fishing?.Enemies.MonsterEnabled==true && room.State.Enemies.Monster.Phase==MonsterPhase.Windup)
            {
                var enemy=room.State.Enemies.Monster;var position=new Vector3(enemy.X/1000f,.75f,enemy.Z/1000f);
                var screen=viewCamera.WorldToScreenPoint(position);float d=((Vector2)screen-(Vector2)pointer).sqrMagnitude;
                if(screen.z>0 && d<24f*24f && d<nearest)aim=position;
            }
            if(room.State?.Finale.Phase==FinalePhase.Surfaced && !room.HoldingMelee && room.Construction?.Open!=true)
            {
                var giant=room.State.Finale;var position=new Vector3(giant.X/1000f,.75f,giant.Z/1000f);
                var screen=viewCamera.WorldToScreenPoint(position);float d=((Vector2)screen-(Vector2)pointer).sqrMagnitude;
                if(screen.z>0 && d<28f*28f && d<nearest)aim=position;
            }
            return aim;
        }
    }
}
