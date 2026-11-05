using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

public sealed partial class HookGame
{
    private sealed partial class Model
    {
        private List<GrenadeState> grenades=new List<GrenadeState>();
        private static bool CanThrow(Fisher p)=>!p.Unavailable && !p.WaitingToJoin && !p.Vitals.Downed && !p.Revival.Dead &&
            !p.Knockback.Active && p.PulledBy<0 && p.HookedPlayer<0 && p.Phase==HookPhase.Ready && !p.Melee.Active && !p.Shopping &&
            !p.Firearm.Busy && p.Revival.RescueTicks==0;

        private void InterruptFinishedGrenades()
        {
            for(int i=0;i<players.Length;i++)
            {
                var p=players[i];var a=p.Grenade;if(!a.Active)continue;
                bool released=a.RemainingTicks<=GrenadeConfig.Default.ActionTicks-GrenadeConfig.Default.WindupTicks;
                if(!released && a.LastRequest==a.ActionRequest)a.Result=GrenadeResult.Interrupted;
                if(released){equipment?.Inventory.ReturnFromGrenade(i,a.ReturnWeaponId);equipment?.RefreshSelection(ref p);}
                a.RemainingTicks=0;a.ActionRequest=0;a.ItemId=a.TargetX=a.TargetZ=0;
                p.Grenade=a;SyncFirearm(ref p);players[i]=p;
            }
        }

        /// <summary>先推进已存在的手雷，再处理出手，因此生成帧只生成、不移动。请求只在首次看到时接收。</summary>
        private void AdvanceGrenades(HookInput[] commands)
        {
            AdvanceGrenadeProjectiles();
            if(equipment==null)return;
            var config=GrenadeConfig.Default;
            for(int i=0;i<players.Length;i++)
            {
                var p=players[i];var a=p.Grenade;int previousActive=equipment.Inventory.ActiveItem(i)?.Id??0;
                if(a.Active)
                {
                    bool beforeRelease=a.RemainingTicks>config.ActionTicks-config.WindupTicks;
                    if(beforeRelease && (!CanThrow(p) || Rescuing(p,commands[i]) || commands[i].Cancel))Finish(GrenadeResult.Interrupted);
                    else
                    {
                        a.RemainingTicks--;
                        if(a.RemainingTicks==config.ActionTicks-config.WindupTicks)Release();
                        if(a.RemainingTicks==0 && a.ActionRequest!=0)equipment.Inventory.ReturnFromGrenade(i,a.ReturnWeaponId);
                    }
                }
                var c=commands[i].Grenade;
                if(!c.IsEmpty && c.RequestId>a.LastRequest)
                {
                    a.LastRequest=c.RequestId;
                    if(a.Active)a.Result=GrenadeResult.Busy;
                    else if(map.Fishing?.Firearms.Enabled!=true || !CanThrow(p) || Rescuing(p,commands[i]))a.Result=GrenadeResult.Disabled;
                    else if(equipment.Inventory.ActiveItem(i)?.Id!=c.ItemId || equipment.Inventory.ActiveItem(i)?.Kind!=ItemKind.Throwable)a.Result=GrenadeResult.NotOwned;
                    else if(a.Sequence==uint.MaxValue || grenades.Count>=GrenadeRules.MaxActive)a.Result=GrenadeResult.Exhausted;
                    else
                    {
                        a.Result=GrenadeRules.TryPlan(map,tiles,p.X,p.Z,c.TargetX,c.TargetZ,out _);
                        if(a.Result==GrenadeResult.Success)
                        {
                            a.Result=GrenadeResult.Pending;a.ActionRequest=c.RequestId;a.ItemId=c.ItemId;
                            a.TargetX=c.TargetX;a.TargetZ=c.TargetZ;a.RemainingTicks=config.ActionTicks;
                        }
                    }
                }
                if(previousActive!=(equipment.Inventory.ActiveItem(i)?.Id??0))equipment.RefreshSelection(ref p);
                p.Grenade=a;SyncFirearm(ref p);players[i]=p;

                void Finish(GrenadeResult result)
                {
                    if(a.LastRequest==a.ActionRequest)a.Result=result;
                    a.RemainingTicks=0;a.ActionRequest=0;a.ItemId=a.TargetX=a.TargetZ=0;
                }
                void Release()
                {
                    var result=GrenadeRules.TryPlan(map,tiles,p.X,p.Z,a.TargetX,a.TargetZ,out var flight);
                    if(result!=GrenadeResult.Success){Finish(result);return;}
                    var offset=GrenadeRules.Scatter(map.Fishing!.Seed,i,a.Sequence+1);
                    if(GrenadeRules.TryPlan(map,tiles,p.X,p.Z,a.TargetX+offset.X,a.TargetZ+offset.Z,out var scattered)==GrenadeResult.Success)flight=scattered;
                    if(grenades.Count>=GrenadeRules.MaxActive){Finish(GrenadeResult.Exhausted);return;}
                    // 所有可能失败的校验都已完成。库存移除与唯一投掷物生成属于这一个固定帧事务。
                    if(!equipment.Inventory.TryConsumeGrenade(i,a.ItemId)){Finish(GrenadeResult.NotOwned);return;}
                    a.Sequence++;flight.Id=flight.SourceItemId=a.ItemId;flight.Owner=i;flight.Sequence=a.Sequence;flight.SpawnTick=tick+1;
                    flight.X=flight.StartX;flight.Y=flight.StartY;flight.Z=flight.StartZ;
                    grenades.Add(flight);
                    if(a.LastRequest==a.ActionRequest)a.Result=GrenadeResult.Success;
                    Record(new HookGameplayEvent(HookGameplayEventKind.GrenadeThrown,p.Slot,targetId:flight.Id,x:flight.TargetX,z:flight.TargetZ,originX:flight.StartX,originZ:flight.StartZ,y:flight.TargetY,originY:flight.StartY,itemKind:ItemKind.Throwable));
                }
            }
        }
        private void AdvanceGrenadeProjectiles()
        {
            foreach(var original in grenades.OrderBy(g=>g.Id).ToArray())
            {
                var g=original;int index=grenades.FindIndex(v=>v.Id==g.Id);
                if(g.Phase==GrenadePhase.Landed)
                {
                    if(tick+1-g.LandedTick>=(ulong)GrenadeConfig.Default.FuseTicks){Explode(g);grenades.RemoveAt(index);}
                    continue;
                }
                g.Age++;
                if(g.Phase==GrenadePhase.Flying)
                {
                    for(int sample=1;sample<=8;sample++)
                    {
                        GrenadeRules.Position(g,(g.Age-1)*8+sample,g.FlightTicks*8,out int x,out int y,out int z);
                        if(GrenadeRules.Solid(map,x,y,z,GrenadeRules.BodyRadius))
                        {g.Phase=GrenadePhase.Falling;g.VerticalSpeed=0;break;}
                        g.X=x;g.Y=y;g.Z=z;
                        if(g.Y<=GrenadeRules.LandingHeight(map,tiles,g.X,g.Z)+GrenadeRules.BodyRadius){Land();break;}
                    }
                    if(g.Phase==GrenadePhase.Flying && g.Age>=g.FlightTicks)Land();
                }
                else
                {
                    g.VerticalSpeed=Math.Min(2000,g.VerticalSpeed+11);g.Y-=g.VerticalSpeed;
                    if(g.Y<=GrenadeRules.LandingHeight(map,tiles,g.X,g.Z)+GrenadeRules.BodyRadius)Land();
                }
                grenades[index]=g;
                void Land()
                {
                    int surface=GrenadeRules.LandingHeight(map,tiles,g.X,g.Z);g.Water=surface==0;
                    g.Y=g.Water?-300:surface+GrenadeRules.BodyRadius;g.Phase=GrenadePhase.Landed;g.LandedTick=tick+1;g.VerticalSpeed=0;
                    Record(new HookGameplayEvent(HookGameplayEventKind.GrenadeLanded,(ushort)g.Owner,targetId:g.Id,x:g.X,z:g.Z,y:g.Y,value:g.Water?1:0,itemKind:ItemKind.Throwable));
                }
            }
        }
        private void Explode(GrenadeState g)
        {
            int Damage(int x,int y,int z)
            {
                long d=IslandMap.DistanceSquared(g.X,g.Z,x,z);var c=GrenadeConfig.Default;
                if(d>(long)c.Radius*c.Radius || !GrenadeRules.Visible(map,g.X,Math.Max(100,g.Y),g.Z,x,y,z))return 0;
                return GrenadeRules.DamageAt(Sqrt(d));
            }
            for(int i=0;i<players.Length;i++)
            {
                var p=players[i];if(p.WaitingToJoin || p.Vitals.Downed || p.Revival.Dead)continue;
                int amount=Damage(p.X,BossGeometry.MuzzleHeight(p),p.Z),before=p.Vitals.Health;
                CharacterSystem.Damage(ref p.Vitals,amount,DamageKind.Combat,equipment!.Attributes(p).Armor);
                if(p.Vitals.Health<before)Record(HookGameplayEventKind.Damaged,p,amount:before-p.Vitals.Health);
                if(p.Vitals.Downed){StopCharacterHook(ref p);MeleeRules.Cancel(ref p);FirearmRules.CancelActions(ref p);p.Shopping=false;}
                players[i]=p;
            }
            if(enemies!=null)
            {
                for(int i=0;i<enemies.PiranhaCount;i++)
                {var f=enemies.GetPiranha(i);int amount=Damage(f.X,0,f.Z);if(amount>0)enemies.Strike(i,amount,map.Fishing!.Melee.PiranhaHealth,map.Fishing.Melee.StunTicks,g.Owner);}
                var monster=enemies.Monster;if(monster.Phase==MonsterPhase.Windup && Damage(monster.X,500,monster.Z)>0)enemies.Interrupt();
            }
            foreach(var crab in crabs){var c=crab.Capture();int amount=Damage(c.X,700,c.Z);if(c.Damageable && amount>0)crab.Strike(g.Owner,amount);}
            for(int i=0;i<eldritch.Count;i++)
            {var c=eldritch.Get(i);int amount=Damage(c.X,700,c.Z);if(c.Damageable && amount>0)eldritch.Strike(i,g.Owner,amount,g.X,g.Z,true);}
            var abyss=boss.Capture();
            if(abyss.Damageable && TownCanHitBoss(g.Owner))
            {
                // 一个部位可能有多个关节代理；每部位只取最大一次伤害，眼睛不产生爆炸暴击。
                var damage=new SortedDictionary<int,int>();
                foreach(var proxy in BossGeometry.Proxies(abyss))
                {
                    int dx=g.X-proxy.X,dz=g.Z-proxy.Z,len=Sqrt((long)dx*dx+(long)dz*dz);
                    int x=proxy.X,z=proxy.Z;
                    if(len>0){x+=(int)((long)dx*Math.Min(len,proxy.RadiusX)/len);z+=(int)((long)dz*Math.Min(len,proxy.RadiusZ)/len);}
                    int amount=Damage(x,proxy.Y,z);if(amount>0 && (!damage.TryGetValue(proxy.LimbId,out int old) || amount>old))damage[proxy.LimbId]=amount;
                }
                foreach(var part in damage)boss.Strike(g.Owner,part.Value,part.Key,false);
            }
            Record(new HookGameplayEvent(HookGameplayEventKind.GrenadeExploded,(ushort)g.Owner,targetId:g.Id,x:g.X,z:g.Z,y:g.Y,value:g.Water?1:0,itemKind:ItemKind.Throwable));
        }
    }
}
