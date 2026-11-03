using System;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed partial class HookGame
{
    private sealed partial class Model
    {
        private bool FirearmsEnabled=>map.Fishing?.Firearms.Enabled==true;
        private InventoryItem? ActiveFirearm(Fisher p)
        {
            if(!FirearmsEnabled || equipment?.ActiveTool(p)!=true)return null;
            var item=equipment.Inventory.Equipped(p.Slot,EquipmentSlot.RightHand);
            return item?.Kind==ItemKind.Firearm?item:null;
        }
        private bool Rescuing(Fisher p,HookInput command)
        {
            int target=command.Revival.RescueSlot;
            return target>=0 && target<players.Length && RevivalSystem.CanHelp(map,tiles,p,players[target],command);
        }
        private void PrepareFirearm(ref Fisher p,HookInput command)
        {
            if(!FirearmsEnabled)return;
            if(p.Firearm.DrawTicks>0)p.Firearm.DrawTicks--;
            if(p.Firearm.RecoveryTicks>0)p.Firearm.RecoveryTicks--;
            if(p.Firearm.CooldownTicks>0)p.Firearm.CooldownTicks--;
            if(!FirearmRules.CanMaintain(p,equipment!.Ring(p)!=null))FirearmRules.CancelActions(ref p);
            if(Rescuing(p,command))FirearmRules.CancelReload(ref p);
        }
        private void SyncFirearm(ref Fisher p)
        {
            if(!FirearmsEnabled)return;
            var gun=ActiveFirearm(p);int id=gun?.Id??0;
            if(id==p.Firearm.WeaponId)return;
            FirearmRules.CancelActions(ref p);p.Firearm.WeaponId=id;
            if(gun.HasValue && FirearmRules.CanMaintain(p,equipment!.Ring(p)!=null))p.Firearm.DrawTicks=FirearmCatalog.Get(gun.Value.Type).DrawTicks;
        }
        private void UpdateFirearmMovement(ref Fisher p,int previousX,int previousZ)
        {
            if(!FirearmsEnabled)return;
            int speed=Sqrt(IslandMap.DistanceSquared(previousX,previousZ,p.X,p.Z))*Rate;
            int owner=p.Slot;
            FirearmSpread.Advance(ref p.Firearm,speed,equipment!.Inventory.Capture().Items.Where(v=>v.Owner==owner && v.Kind==ItemKind.Firearm).Select(v=>v.Type));
        }
        private void AdvanceFirearms(HookInput[] commands)
        {
            if(!FirearmsEnabled)return;
            for(int i=0;i<players.Length;i++)
            {
                var p=players[i];var c=commands[i];var item=ActiveFirearm(p);
                if(!FirearmRules.CanMaintain(p,equipment!.Ring(p)!=null)){FirearmRules.CancelActions(ref p);players[i]=p;continue;}
                if(p.Shopping)continue;
                if(!item.HasValue || p.Firearm.Busy || Rescuing(p,c) || c.Build!=TileKind.None || c.RodAction!=RodAction.None || c.Inventory.Action!=InventoryAction.None || !c.Treasure.IsEmpty)continue;
                var gun=item.Value;var definition=FirearmCatalog.Get(gun.Type);
                if(c.Reload && gun.LoadedAmmo<definition.Capacity)
                {
                    p.Firearm.ReloadWeaponId=gun.Id;p.Firearm.ReloadTicks=definition.ReloadTicks;
                    Record(HookGameplayEventKind.FirearmReloadStarted,p,value:definition.ReloadTicks,itemKind:ItemKind.Firearm,itemType:gun.Type);
                }
                else if(c.Fire && p.Firearm.CooldownTicks==0 && p.Firearm.ShotSequence<uint.MaxValue &&
                    IslandMap.DistanceSquared(p.X,p.Z,p.AimX,p.AimZ)>10000 && equipment.Inventory.TryFireRound(i,gun.Id))
                {
                    p.Firearm.ShotSequence++;p.Firearm.CooldownTicks=definition.CooldownTicks;p.Firearm.RecoveryTicks=definition.RecoveryTicks;
                    int angle=FirearmSpread.Sample(map.Fishing!.Seed,p.Slot,p.Firearm.ShotSequence,gun.Id,p.Firearm.Spread(gun.Type),definition.Spread.Sampling);
                    FirearmSpread.Direction(p.AimX-p.X,p.AimZ-p.Z,angle,out int dx,out int dz);
                    // Commit shooter before impacts; subsequent players observe earlier hits in stable slot order.
                    players[i]=p;Shoot(p,gun.Type,dx,dz,c.AimVertical,commands);
                }
                players[i]=p;
            }
        }
        private void FinishFirearmReloads(int[] previousReloads)
        {
            if(!FirearmsEnabled)return;
            for(int i=0;i<players.Length;i++)
            {
                var p=players[i];
                if(!FirearmRules.CanMaintain(p,equipment!.Ring(p)!=null)){FirearmRules.CancelActions(ref p);players[i]=p;continue;}
                if(previousReloads[i]==0 || !p.Firearm.Reloading)continue;
                var gun=ActiveFirearm(p);
                if(gun?.Id!=p.Firearm.ReloadWeaponId){FirearmRules.CancelReload(ref p);players[i]=p;continue;}
                if(--p.Firearm.ReloadTicks==0)
                {
                    int loaded=equipment.Inventory.ReloadFirearm(i,p.Firearm.ReloadWeaponId);p.Firearm.ReloadWeaponId=0;
                    Record(HookGameplayEventKind.FirearmReloaded,p,amount:loaded,itemKind:ItemKind.Firearm,itemType:gun.Value.Type);
                }
                players[i]=p;
            }
        }
        private void Shoot(Fisher shooter,int type,int horizontalX,int horizontalZ,short vertical,HookInput[] commands)
        {
            var weapon=FirearmCatalog.Get(type);int distance=weapon.Range,kind=0,target=-1;
            BossGeometry.Direction3(horizontalX,horizontalZ,vertical,out int dx,out int dy,out int dz);
            int muzzleY=BossGeometry.MuzzleHeight(shooter),horizontalScale=Sqrt((long)dx*dx+(long)dz*dz);
            int Circle(int x,int z,int radius,int bottom=-700,int top=2200)
            {
                int horizontalRange=(int)((long)weapon.Range*horizontalScale/EnemyGeometry.DirectionScale);
                int entry=FirearmRules.RayCircle(shooter.X,shooter.Z,horizontalX,horizontalZ,x,z,radius,horizontalRange);
                if(entry==int.MaxValue)return entry;
                int range=(int)((long)entry*EnemyGeometry.DirectionScale/Math.Max(1,horizontalScale));
                int y=muzzleY+(int)((long)dy*range/EnemyGeometry.DirectionScale);return y>=bottom && y<=top?range:int.MaxValue;
            }
            void Candidate(int entry,int nextKind,int id){if(entry<=weapon.Range && (entry<distance || kind==0)){distance=entry;kind=nextKind;target=id;}}
            // Solids win exact ties. Platform decking is below the horizontal firing plane.
            if(map.Platform==null)
            {
                Candidate(Circle(0,1050,2050,0,7000),1,-4);
                Candidate(Circle(-2150,1550,390,0,3000),1,-4);
            }
            if(map.Fishing!=null)for(int id=0;id<=map.Fishing.Merchants.Definitions.Count;id++)
            {var center=MerchantRules.Position(map.Fishing,id);Candidate(FirearmRules.RayBox3(shooter.X,muzzleY,shooter.Z,dx,dy,dz,center.X,1000,center.Z,MerchantRules.BodyHalfSize,1000,MerchantRules.BodyHalfSize,weapon.Range),1,-4);}
            var giant=finale?.Capture();
            if(giant?.Phase==FinalePhase.Surfaced)Candidate(Circle(giant.X,giant.Z,map.Fishing!.Finale.BodyRadius),1,-5);
            if(dy<0 && muzzleY>=450)
            {int floor=(int)((450L-muzzleY)*EnemyGeometry.DirectionScale/dy);int x=shooter.X+(int)((long)dx*floor/EnemyGeometry.DirectionScale),z=shooter.Z+(int)((long)dz*floor/EnemyGeometry.DirectionScale);
                if(floor<=weapon.Range && PlatformRules.OnPlatform(tiles,x,z))Candidate(floor,1,-4);
            }
            for(int i=0;i<players.Length;i++)
            {
                var p=players[i];if(i==shooter.Slot || p.WaitingToJoin || p.Vitals.Downed || p.Revival.Dead)continue;
                Candidate(Circle(p.X,p.Z,EnemyGeometry.PlayerRadius),2,i);
            }
            if(enemies!=null)
            {
                for(int i=0;i<enemies.PiranhaCount;i++)
                {var f=enemies.GetPiranha(i);if(f.Owner>=0 || f.RespawnTicks>0 || f.Escape.Active)continue;Candidate(Circle(f.X,f.Z,250),3,i);}
                var monster=enemies.Monster;
                if(monster.Phase==MonsterPhase.Windup)Candidate(Circle(monster.X,monster.Z,EnemyGeometry.MonsterRadius),4,-3);
            }
            for(int i=0;i<crabs.Length;i++)
            {
                var crab=crabs[i].Capture();
                if(crab.Damageable)Candidate(Circle(crab.X,crab.Z,CrabConfig.BodyRadius),5,i);
            }
            var abyss=boss.Capture();int bossEntry=BossGeometry.RayHit(abyss,shooter.X,muzzleY,shooter.Z,dx,dy,dz,weapon.Range,out int limbId,out bool eye);
            Candidate(bossEntry,6,limbId);
            int hitY=muzzleY+(int)((long)dy*distance/EnemyGeometry.DirectionScale);
            int hitX=shooter.X+(int)((long)dx*distance/EnemyGeometry.DirectionScale),hitZ=shooter.Z+(int)((long)dz*distance/EnemyGeometry.DirectionScale);
            if(kind==2)HitFriend(shooter,players[target],weapon,dx,dz,commands[target]);
            int damage=0,entity=0;
            if(kind==3)
            {
                var fish=enemies!.GetPiranha(target);entity=fish.Id;
                damage=Math.Min(weapon.Damage,map.Fishing!.Melee.PiranhaHealth-fish.DamageTaken);
                enemies.Strike(target,weapon.Damage,map.Fishing.Melee.PiranhaHealth,map.Fishing.Firearms.StunTicks,shooter.Slot);
            }
            if(kind==4)enemies!.Interrupt();
            if(kind==5)
            {
                entity=target;damage=Math.Min(weapon.Damage,crabs[target].Capture().Health);
                crabs[target].Strike(shooter.Slot,weapon.Damage);
            }
            if(kind==6)
            {
                entity=target+1;int hp=target<0?abyss.Health:abyss.Limbs[target].Health;
                if(boss.Strike(shooter.Slot,weapon.Damage,target,eye))damage=Math.Min(hp,eye?(weapon.Damage*3+1)/2:weapon.Damage);
            }
            Record(new HookGameplayEvent(HookGameplayEventKind.FirearmShot,shooter.Slot,amount:damage,value:entity,targetId:kind==3?-2:kind==5?-6:kind==6?-7:target,x:hitX,z:hitZ,itemKind:ItemKind.Firearm,itemType:type,originX:shooter.X,originZ:shooter.Z,y:hitY,originY:muzzleY));
        }
        private void HitFriend(Fisher shooter,Fisher target,FirearmDefinition weapon,int dx,int dz,HookInput command)
        {
            int amount=FirearmRules.FriendlyDamage(weapon.Damage,equipment!.Attributes(target).Armor,map.Fishing!.Firearms.FriendlyDamagePercent);
            int before=target.Vitals.Health;
            CharacterSystem.Damage(ref target.Vitals,amount,DamageKind.Direct,0);
            if(target.Vitals.Health<before)Record(HookGameplayEventKind.Damaged,target,amount:before-target.Vitals.Health);
            players[target.Slot]=target;
            bool push=roomRules.FriendlyPranksEnabled && !target.Unavailable && !target.Vitals.Downed && !target.Swimming.InWater && target.PulledBy<0 && target.HookedPlayer<0 && target.ProtectionTicks==0 && !target.Knockback.Active && !target.Shopping && !Rescuing(target,command);
            if(push && map.Fishing.Firearms.FriendlyKnockback>0)
            {ApplyEnemyHit(new EnemyHit(target.Slot,0,true,dx,dz,map.Fishing.Firearms.FriendlyKnockback));players[target.Slot].ProtectionTicks=ProtectionDuration;}
        }
    }
}
