namespace Advanture.HookIsland.Simulation;

public sealed partial class HookGame
{
    private sealed partial class Model
    {
        private void AdvanceMelee(HookInput[] commands)
        {
            if(map.Fishing?.Melee.Enabled!=true)return;
            for(int i=0;i<players.Length;i++)
            {
                var p=players[i];
                if(!MeleeRules.CanAct(p,equipment!.Ring(p)!=null))MeleeRules.Cancel(ref p);
                if(!p.Melee.Active){players[i]=p;continue;}
                var weapon=equipment!.Weapon(p);
                if(weapon==null){MeleeRules.Cancel(ref p);players[i]=p;continue;}
                var swing=p.Melee;swing.RemainingTicks--;p.Melee=swing;
                // A fixed tick crossing is the impact receipt. Restoring a post-impact snapshot
                // cannot replay it; all candidates are resolved in stable identity order.
                if(swing.RemainingTicks==weapon.DurationTicks-weapon.WindupTicks)
                {
                    if(enemies!=null)
                    {
                        for(int j=0;j<enemies.PiranhaCount;j++)
                        {
                            var fish=enemies.GetPiranha(j);
                            if(MeleeRules.Contains(p,fish.X,fish.Z,weapon,250) && enemies.Strike(j,weapon.Damage,map.Fishing.Melee))Hit(-1,fish.X,fish.Z);
                        }
                        var monster=enemies.Monster;
                        if(monster.Phase==MonsterPhase.Windup && MeleeRules.Contains(p,monster.X,monster.Z,weapon,EnemyGeometry.MonsterRadius) && enemies.Interrupt())Hit(-2,monster.X,monster.Z);
                    }
                    if(roomRules.FriendlyPranksEnabled)for(int j=0;j<players.Length;j++)
                    {
                        var target=players[j];
                        if(j==i || target.Unavailable || target.Vitals.Downed || target.Revival.Dead || target.Swimming.InWater || target.PulledBy>=0 || target.HookedPlayer>=0 || target.ProtectionTicks>0 || target.Knockback.Active || target.Shopping)continue;
                        int rescued=commands[j].Revival.RescueSlot;
                        if(rescued>=0 && rescued<players.Length && RevivalSystem.CanHelp(map,tiles,target,players[rescued],commands[j]))continue;
                        if(!MeleeRules.Contains(p,target.X,target.Z,weapon))continue;
                        ApplyEnemyHit(new EnemyHit(j,0,true,swing.DirectionX,swing.DirectionZ,weapon.FriendlyKnockback));
                        players[j].ProtectionTicks=ProtectionDuration;Hit(j,target.X,target.Z);
                    }
                }
                players[i]=p;
                void Hit(int target,int x,int z)
                {
                    if(p.Melee.HitCount<int.MaxValue)p.Melee.HitCount++;
                    Record(new HookGameplayEvent(HookGameplayEventKind.MeleeHit,p.Slot,targetId:target,x:x,z:z,itemKind:ItemKind.Weapon,itemType:p.Melee.WeaponType));
                }
            }
        }
    }
}
