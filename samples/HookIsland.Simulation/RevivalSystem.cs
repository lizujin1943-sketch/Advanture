using System;
using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

/// <summary>Deterministic life cycle and personal revival transaction, independent of Unity and transport.</summary>
public static class RevivalSystem
{
    public static bool GameOver(IReadOnlyList<Fisher> players)
    {
        bool entered=false;
        foreach(var p in players)
        {
            if(p.WaitingToJoin)continue;
            entered=true;if(!p.Revival.Dead || p.Coins>=p.Revival.DeathCost)return false;
        }
        return entered;
    }
    public static bool CanHelp(IslandMap map,IReadOnlyList<PlatformTile> tiles,Fisher helper,Fisher target)
    {
        if(map.Fishing==null || helper.Unavailable || target.WaitingToJoin || helper.Knockback.Active || target.Knockback.Active || helper.Slot==target.Slot || helper.Vitals.Downed || helper.Revival.Dead ||
            !target.Vitals.Downed || target.Revival.Dead || helper.Swimming.InWater || target.Swimming.InWater ||
            helper.PulledBy>=0 || target.PulledBy>=0 || helper.Shopping || helper.Phase!=HookPhase.Ready)return false;
        int range=map.Fishing.Revival.RescueRange;
        if(IslandMap.DistanceSquared(helper.X,helper.Z,target.X,target.Z)>(long)range*range)return false;
        int segments=Math.Max(1,(HookGame.Sqrt(IslandMap.DistanceSquared(helper.X,helper.Z,target.X,target.Z))+99)/100);
        for(int n=0;n<=segments;n++)
        {
            int x=helper.X+(target.X-helper.X)*n/segments,z=helper.Z+(target.Z-helper.Z)*n/segments;
            if(SwimmingTerrain.Obstacle(map,x,z) || !SwimmingTerrain.Ground(map,tiles,x,z))return false;
        }
        return true;
    }
    private static bool HoldsHelp(HookInput c,int target)=>c.Revival.RescueSlot==target && c.MoveX==0 && c.MoveZ==0 &&
        !c.Fire && !c.Cancel && !c.Shopping && c.Build==TileKind.None && c.Upgrade==HookUpgrade.None &&
        c.RodAction==RodAction.None && c.Inventory.Action==InventoryAction.None;

    public static void Resolve(IslandMap map,IReadOnlyList<PlatformTile> tiles,Fisher[] players,HookInput[] commands,
        int[] healthAtFrameStart,ulong tick,Func<Fisher,CharacterStats> attributes)
    {
        var config=map.Fishing!.Revival;
        var helpers=new int[players.Length];
        // Select from one pre-resolution state: a newly revived player cannot help in the same frame.
        for(int target=0;target<players.Length;target++)
        {
            int owner=players[target].Revival.Rescuer-1;
            bool Eligible(int h)=>h>=0 && h<players.Length && HoldsHelp(commands[h],target) &&
                players[h].Vitals.Health>=healthAtFrameStart[h] && CanHelp(map,tiles,players[h],players[target]);
            if(!Eligible(owner))
            {owner=-1;for(int h=0;h<players.Length;h++)if(Eligible(h)){owner=h;break;}}
            helpers[target]=owner+1;
        }
        for(int i=0;i<players.Length;i++)
        {
            var p=players[i];var life=p.Revival;
            if(p.WaitingToJoin || life.Dead)continue;
            if(!p.Vitals.Downed){life.DownedTicks=life.Rescuer=life.RescueTicks=0;p.Revival=life;players[i]=p;continue;}
            int helper=helpers[i];
            life.RescueTicks=helper==0?0:life.Rescuer==helper?life.RescueTicks+1:1;life.Rescuer=helper;
            if(life.RescueTicks>=config.RescueTicks)
            {
                p.Vitals.Health=Health(attributes(p).HealthMax,config.RescueHealthPermille);
                life.DownedTicks=life.Rescuer=life.RescueTicks=0;
                p.RegenDelay=map.Fishing.RegenDelayTicks;p.Swimming.DamageRemainder=0;
            }
            else
            {
                // A fresh health-zero transition starts the full countdown at this confirmed frame.
                if(healthAtFrameStart[i]==0)life.DownedTicks++;
                if(life.DownedTicks>=config.DownedTicks)
                {
                    life.Dead=true;life.DeathTick=tick;life.DeathBalance=p.Coins;life.DeathCost=config.Cost(tick,p.Coins);
                    if(life.Deaths<uint.MaxValue)life.Deaths++;
                    life.Rescuer=life.RescueTicks=0;
                }
            }
            p.Revival=life;players[i]=p;
        }
    }
    public static void Respawn(IslandMap map,IReadOnlyList<PlatformTile> tiles,IReadOnlyList<Fisher> players,
        ref Fisher p,RevivalCommand command,CharacterStats attributes,ref int totalCoins)
    {
        if(command.RespawnRequest==0 || command.RespawnRequest<=p.Revival.LastRequest)return;
        p.Revival.LastRequest=command.RespawnRequest;
        if(p.Unavailable){p.Revival.LastResult=RespawnResult.Unavailable;return;}
        if(!p.Revival.Dead){p.Revival.LastResult=RespawnResult.NotDead;return;}
        if(command.DeathId!=p.Revival.Deaths){p.Revival.LastResult=RespawnResult.StaleDeath;return;}
        if(GameOver(players)){p.Revival.LastResult=RespawnResult.GameOver;return;}
        int cost=p.Revival.DeathCost;
        if(p.Coins<cost){p.Revival.LastResult=RespawnResult.InsufficientCoins;return;}
        if(!Landing(map,tiles,players,p.Slot,out var point)){p.Revival.LastResult=RespawnResult.NoLanding;return;}
        // No fallible work follows the commit. Income bounds guarantee the loss ledger cannot overflow.
        p.Coins-=cost;totalCoins-=cost;p.Revival.CoinsSpent+=cost;
        p.Revival.Dead=false;p.Revival.DownedTicks=p.Revival.Rescuer=p.Revival.RescueTicks=p.Revival.DeathCost=0;
        p.Revival.DeathTick=0;p.Revival.DeathBalance=0;
        p.Revival.LastResult=RespawnResult.Success;
        p.Vitals.Health=Health(attributes.HealthMax,map.Fishing!.Revival.RespawnHealthPermille);
        p.Stamina=(int)((long)attributes.StaminaMax*map.Fishing.Revival.RespawnStaminaPermille/1000);
        p.RegenDelay=map.Fishing.RegenDelayTicks;p.Swimming=default;p.Knockback=default;
        p.X=point.X;p.Z=point.Z;p.HookX=point.X;p.HookZ=point.Z;
        p.MoveRemainderX=p.MoveRemainderZ=p.RemainderX=p.RemainderZ=0;
        p.HookTravelRemainder=p.ReelRemainder=0;p.Shopping=false;
    }
    private static int Health(int maximum,int permille)=>Math.Max(1,(int)((long)maximum*permille/1000));
    private static bool Landing(IslandMap map,IReadOnlyList<PlatformTile> tiles,IReadOnlyList<Fisher> players,int slot,out Point point)
    {
        var spawn=map.Spawns[slot];
        if(SwimmingTerrain.Landing(map,tiles,players,slot,spawn.X,spawn.Z)){point=spawn;return true;}
        // Deterministic outward search, limited to the safe starting area around the configured spawn.
        for(int radius=250;radius<=3000;radius+=250)
            for(int dz=-radius;dz<=radius;dz+=250)for(int dx=-radius;dx<=radius;dx+=250)
            {
                if(Math.Abs(dx)!=radius && Math.Abs(dz)!=radius)continue;
                int x=spawn.X+dx,z=spawn.Z+dz;
                if(SwimmingTerrain.Landing(map,tiles,players,slot,x,z)){point=new Point(x,z);return true;}
            }
        point=default;return false;
    }
}
