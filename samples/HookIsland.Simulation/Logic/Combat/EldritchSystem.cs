using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>Bounded encounter roster. Every attack phase, summon and reward receipt survives rollback.</summary>
public sealed class EldritchSystem
{
    private readonly EldritchConfig config;
    private readonly EldritchTerrain terrain;
    private readonly EldritchCreature[] creatures;
    private int nextSpawn;
    private readonly TownSpawnDirector? director;
    private readonly bool town;
    private readonly bool[] activeZones=new bool[6];
    public EldritchSystem(IslandMap map,EldritchState? restored=null)
    {
        town=map.IsTown;config=map.Fishing?.Eldritch??EldritchConfig.Disabled;terrain=new EldritchTerrain(map);
        creatures=restored?.Creatures.ToArray()??(config.Enabled?Enumerable.Range(0,town?TownEncounters.Population:EldritchCatalog.Population).Select(i=>Dormant(i,0,town?0:i<10?config.InitialDelayTicks+i*config.StaggerTicks:0)).ToArray():Array.Empty<EldritchCreature>());
        nextSpawn=restored?.NextSpawn??0;
        if(town && config.Enabled)director=new TownSpawnDirector(map.Fishing!.Seed,restored?.Director??default);
    }
    public int Count=>creatures.Length;
    public EldritchCreature Get(int id)=>creatures[id];
    public EldritchState Capture()=>new EldritchState(creatures,nextSpawn,director?.Capture()??default);
    public static bool Eligible(Fisher p)=>!p.WaitingToJoin && !p.Unavailable && !p.Vitals.Downed && !p.Revival.Dead;
    public static int Reward(EldritchCreature c)=>c.IsMinion?4:EldritchCatalog.Get(c.Kind).Coins;
    private EldritchCreature Dormant(int id,uint generation,int timer)=>new EldritchCreature
        {Id=id,Kind=town?TownEncounters.Kind(id):id<10?(EldritchKind)id:EldritchKind.DeepOne,TownAdult=town && id<TownEncounters.AdultCount,Generation=generation,Timer=timer,Target=-1,Ally=-1};
    public void ActivateTownZone(int zone,bool active)
    {
        if(!town)return;activeZones[zone]=active;
        int adults=creatures.Take(TownEncounters.AdultCount).Count(c=>c.Damageable);
        for(int i=0;i<Count;i++)
        {
            if(TownEncounters.Zone(i)!=zone)continue;var c=creatures[i];
            if(c.Alive && c.Paused!=!active)
            {
                // Waking an adult consumes the same global slot as a new spawn.
                // Other paused adults keep their health and summon receipts until a slot opens.
                if(active && !c.IsMinion && adults>=config.MaxAlive)continue;
                if(!c.IsMinion)adults+=active?1:-1;
                c.Paused=!active;c.Phase=EldritchPhase.Emerging;c.Timer=EldritchCatalog.EmergenceTicks;c.HitMask=0;c.RouteTicks=c.NoTargetTicks=0;c.Ally=-1;creatures[i]=c;}
        }
    }
    public bool ReachableHit(int id,int x,int z)=>terrain.Visible(x,z,creatures[id].X,creatures[id].Z);
    public int Strike(int id,int slot,int damage,int sourceX,int sourceZ,bool blast=false)
    {
        if(id<0 || id>=Count || slot<0 || slot>=4 || damage<=0)return 0;
        var c=creatures[id];if(!c.Damageable)return 0;
        if(c.Kind==EldritchKind.ShellWarden && c.Phase!=EldritchPhase.Recovering && !blast &&
            (long)(sourceX-c.X)*c.DirectionX+(long)(sourceZ-c.Z)*c.DirectionZ>0)damage=Math.Max(1,damage/4);
        int dealt=Math.Min(c.Health,damage);c.Health-=dealt;
        if(c.Health==0)
        {c.Phase=EldritchPhase.Dead;c.Timer=town?Math.Min(config.RespawnTicks,TownDirectorRules.CorpseTicks):config.RespawnTicks;c.Target=c.Ally=-1;c.RewardOwner=slot+1;c.RouteTicks=c.NoTargetTicks=0;}
        creatures[id]=c;
        if(c.Health>0)Interrupt(id);
        return dealt;
    }
    public bool Interrupt(int id)
    {
        var c=creatures[id];var d=EldritchCatalog.Get(c.Kind);
        if(c.Phase!=EldritchPhase.Windup || !d.Interruptible)return false;
        c.Phase=EldritchPhase.Recovering;c.Timer=d.RecoveryTicks;c.Ally=-1;creatures[id]=c;return true;
    }
    public void MarkRewarded(int id)
    {if(creatures[id].Phase!=EldritchPhase.Dead || creatures[id].Rewarded)throw new InvalidOperationException("No pending eldritch reward.");creatures[id].Rewarded=true;}
    internal bool CanDisplace(int id,Point from,Point to)=>terrain.Segment(EldritchCatalog.Get(creatures[id].Kind).Habitat,from.X,from.Z,to.X,to.Z);
    internal void Displace(int id,int x,int z){creatures[id].X=x;creatures[id].Z=z;}
    public void ReconcileTerrain(IReadOnlyList<PlatformTile> tiles)
    {
        terrain.Refresh(tiles);
        for(int i=0;i<Count;i++)
        {
            var c=creatures[i];
            // Dead bodies are receipts, not occupants. Terrain changes must never erase their reward.
            if(c.Damageable && !c.Paused && !terrain.CanStand(EldritchCatalog.Get(c.Kind).Habitat,c.X,c.Z))
                creatures[i]=Dormant(i,c.Generation,c.IsMinion?0:config.RespawnTicks);
        }
    }
    public void Advance(IReadOnlyList<PlatformTile> tiles,IReadOnlyList<Fisher> players,Action<EnemyHit> hit,bool allowSpawn=true)
    {
        if(!config.Enabled)return;
        ReconcileTerrain(tiles);
        // Freeze roster membership for this update: newly summoned children first act next tick.
        var active=creatures.Select(c=>c.Damageable).ToArray();
        for(int i=0;i<Count;i++)
        {
            var c=creatures[i];var d=EldritchCatalog.Get(c.Kind);
            if(town && (!activeZones[TownEncounters.Zone(i)] || c.Paused))continue;
            if(c.Phase==EldritchPhase.Dormant){if(c.Timer>0)c.Timer--;creatures[i]=c;continue;}
            if(c.Phase==EldritchPhase.Dead)
            {if(c.Rewarded && --c.Timer==0)c=Dormant(i,c.Generation,0);creatures[i]=c;continue;}
            if(!active[i])continue;
            if(c.Phase==EldritchPhase.Emerging || c.Phase==EldritchPhase.Recovering)
            {if(--c.Timer==0){c.Phase=EldritchPhase.Hunting;c.RouteTicks=0;c.Ally=-1;}creatures[i]=c;continue;}
            if(c.Phase==EldritchPhase.Windup)
            {
                if(--c.Timer==0){c.Phase=EldritchPhase.Active;c.Timer=d.ActiveTicks;Apply(ref c,d,players,hit);}
                creatures[i]=c;continue;
            }
            if(c.Phase==EldritchPhase.Active)
            {
                if(--c.Timer==0){c.Phase=EldritchPhase.Recovering;c.Timer=d.RecoveryTicks;c.Ally=-1;}
                else if(d.Ability==EldritchAbility.Charge || d.Ability==EldritchAbility.Miasma)Apply(ref c,d,players,hit);
                creatures[i]=c;continue;
            }
            Hunt(ref c,d,players);creatures[i]=c;
        }
        if(town)
        {
            // 仅统计正在活跃城区探索的玩家。购物、倒地、离线和竞技场内的玩家不会额外增加压力。
            int participants=0;
            foreach(var p in players)
                for(int z=0;z<activeZones.Length;z++)
                    if(activeZones[z] && TownEncounters.NearZone(z,p) && !TownLayout.InArena(p)){participants++;break;}
            director!.Advance(allowSpawn,participants,activeZones,creatures,config.MaxAlive,id=>Spawn(id,players));
            return;
        }
        // 非城镇旧钓鱼地图维持原生态刷新，不混入本地图的预算与区域规则。
        if(!allowSpawn)return;
        int alive=creatures.Take(10).Count(c=>c.Damageable);
        int start=nextSpawn;
        for(int n=0;n<10 && alive<config.MaxAlive;n++)
        {
            int i=(start+n)%10;var c=creatures[i];
            if(!config.Includes(i) || c.Phase!=EldritchPhase.Dormant || c.Timer>0)continue;
            nextSpawn=(i+1)%10;
            if(Spawn(i,players))alive++;
            else creatures[i].Timer=30;
        }
    }
    private bool Spawn(int i,IReadOnlyList<Fisher> players)
    {
        var c=creatures[i];if(c.Generation==uint.MaxValue || !(town?terrain.SpawnTown(c,players,creatures,out var p):terrain.Spawn(c,players,creatures,out p)))return false;
        c=Dormant(i,c.Generation+1,EldritchCatalog.EmergenceTicks);c.Phase=EldritchPhase.Emerging;
        c.TownAdult=town && i<TownEncounters.AdultCount;c.X=p.X;c.Z=p.Z;c.Health=EldritchCatalog.MaxHealth(c);c.DirectionZ=EnemyGeometry.DirectionScale;creatures[i]=c;return true;
    }
    private void Hunt(ref EldritchCreature c,EldritchDefinition d,IReadOnlyList<Fisher> players)
    {
        if(town){int zone=TownEncounters.Zone(c.Id);players=players.Select(p=>{if(!TownEncounters.NearZone(zone,p,8000))p.Unavailable=true;return p;}).ToArray();}
        if(c.RouteTicks>0)c.RouteTicks--;
        if(c.RouteTicks==0 || c.Target>=0 && !Eligible(players[c.Target]))
        {c.Target=terrain.Target(c,players,out var goal);c.GoalX=goal.X;c.GoalZ=goal.Z;c.RouteTicks=6;}
        if(c.Target<0)
        {if(!town && ++c.NoTargetTicks>=300)c=Dormant(c.Id,c.Generation,c.IsMinion?0:config.RespawnTicks);return;}
        var p=players[c.Target];long distance=IslandMap.DistanceSquared(c.X,c.Z,p.X,p.Z);
        bool visible=terrain.Visible(c.X,c.Z,p.X,p.Z);
        // A nearby target behind solid cover must not reserve an encounter slot forever.
        if(visible)c.NoTargetTicks=0;
        else if(!town && ++c.NoTargetTicks>=300){c=Dormant(c.Id,c.Generation,c.IsMinion?0:config.RespawnTicks);return;}
        bool ready=distance<=(long)d.Range*d.Range && visible;
        c.Ally=-1;
        if(d.Ability==EldritchAbility.Mend)
        {
            int missing=0;
            foreach(var ally in creatures)
            {
                int deficit=EldritchCatalog.MaxHealth(ally)-ally.Health;
                if(ally.Id==c.Id || !ally.Damageable || deficit<=missing || IslandMap.DistanceSquared(c.X,c.Z,ally.X,ally.Z)>(long)d.Radius*d.Radius || !terrain.Visible(c.X,c.Z,ally.X,ally.Z))continue;
                c.Ally=ally.Id;missing=deficit;
            }
            ready=c.Ally>=0;
        }
        if(d.Ability==EldritchAbility.Brood && c.Summons>=EldritchCatalog.MaxBroodPerGeneration)ready=false;
        if(ready && c.AttackSerial<uint.MaxValue)
        {
            EnemyGeometry.Direction(p.X-c.X,p.Z-c.Z,out c.DirectionX,out c.DirectionZ);
            c.EffectX=p.X;c.EffectZ=p.Z;
            if(d.Ability==EldritchAbility.Beam || d.Ability==EldritchAbility.Charge)
            {c.EffectX=c.X+(int)((long)c.DirectionX*d.Range/EnemyGeometry.DirectionScale);c.EffectZ=c.Z+(int)((long)c.DirectionZ*d.Range/EnemyGeometry.DirectionScale);}
            if(d.Ability==EldritchAbility.Blink && !terrain.CanStand(d.Habitat,c.EffectX,c.EffectZ))return;
            c.Phase=EldritchPhase.Windup;c.Timer=d.WindupTicks;c.AttackSerial++;c.HitMask=0;return;
        }
        c.Ally=-1;
        // Supports keep their distance while no useful cast is available.
        if((d.Ability==EldritchAbility.Mend || d.Ability==EldritchAbility.Brood) && distance<3000L*3000)
            Move(ref c,d,c.X+(c.X-p.X),c.Z+(c.Z-p.Z),d.Speed);
        else if(!visible || distance>(long)d.Range*d.Range || d.Habitat==EldritchHabitat.Ground)Move(ref c,d,c.GoalX,c.GoalZ,d.Speed);
    }
    private void Move(ref EldritchCreature c,EldritchDefinition d,int x,int z,int speed)
    {
        int dx=x-c.X,dz=z-c.Z,length=HookGame.Sqrt((long)dx*dx+(long)dz*dz);if(length==0)return;
        int amount=Math.Min(length,FishingRules.Travel(speed,ref c.TravelRemainder));
        int nx=c.X+(int)((long)dx*amount/length),nz=c.Z+(int)((long)dz*amount/length);
        if(!terrain.Segment(d.Habitat,c.X,c.Z,nx,nz)){c.RouteTicks=0;return;}
        c.X=nx;c.Z=nz;if(c.Phase==EldritchPhase.Hunting)EnemyGeometry.Direction(dx,dz,out c.DirectionX,out c.DirectionZ);
    }
    private void Apply(ref EldritchCreature c,EldritchDefinition d,IReadOnlyList<Fisher> players,Action<EnemyHit> hit)
    {
        if(d.Ability==EldritchAbility.Mend)
        {
            if(c.Ally>=0)
            {var a=creatures[c.Ally];if(a.Damageable && IslandMap.DistanceSquared(c.X,c.Z,a.X,a.Z)<=(long)d.Radius*d.Radius && terrain.Visible(c.X,c.Z,a.X,a.Z))
                {a.Health=Math.Min(EldritchCatalog.MaxHealth(a),a.Health+EldritchCatalog.HealAmount);creatures[a.Id]=a;}}
            return;
        }
        if(d.Ability==EldritchAbility.Brood)
        {
            int spawned=0;
            for(int i=town?TownEncounters.AdultCount:10;i<Count && spawned<2 && c.Summons<EldritchCatalog.MaxBroodPerGeneration;i++)
                if(creatures[i].Phase==EldritchPhase.Dormant && Spawn(i,players)){spawned++;c.Summons++;}
            return;
        }
        int previousX=c.X,previousZ=c.Z;
        if(d.Ability==EldritchAbility.Charge)Move(ref c,d,c.EffectX,c.EffectZ,EldritchCatalog.ChargeSpeed);
        if(d.Ability==EldritchAbility.Blink)
        {
            if(!terrain.CanStand(d.Habitat,c.EffectX,c.EffectZ) || !terrain.Visible(c.X,c.Z,c.EffectX,c.EffectZ))return;
            c.X=c.EffectX;c.Z=c.EffectZ;
        }
        if(d.Ability==EldritchAbility.Miasma && (d.ActiveTicks-c.Timer)%30!=0)return;
        foreach(var p in players)
        {
            if(!Eligible(p) || d.Habitat==EldritchHabitat.Ground && p.Swimming.InWater)continue;
            int ox=d.Ability==EldritchAbility.Miasma?c.EffectX:c.X,oz=d.Ability==EldritchAbility.Miasma?c.EffectZ:c.Z;
            long distance=IslandMap.DistanceSquared(ox,oz,p.X,p.Z);
            bool contains=distance<=(long)d.Radius*d.Radius;
            if(d.Ability==EldritchAbility.Beam)contains=HookGame.SegmentHit(c.X,c.Z,c.EffectX,c.EffectZ,p.X,p.Z,d.Radius);
            if(d.Ability==EldritchAbility.Charge)contains=HookGame.SegmentHit(previousX,previousZ,c.X,c.Z,p.X,p.Z,d.Radius);
            if(d.Ability==EldritchAbility.Ring)contains&=distance>=(long)EldritchCatalog.RingInnerRadius*EldritchCatalog.RingInnerRadius;
            if(!contains || !terrain.Visible(ox,oz,p.X,p.Z))continue;
            if(d.Ability!=EldritchAbility.Miasma && (c.HitMask&(1<<p.Slot))!=0)continue;
            c.HitMask|=(byte)(1<<p.Slot);
            if(d.Ability==EldritchAbility.Pull)
            {EnemyGeometry.Direction(c.X-p.X,c.Z-p.Z,out int dx,out int dz);hit(new EnemyHit(p.Slot,d.Damage,true,dx,dz,EldritchCatalog.PullDistance));}
            else hit(new EnemyHit(p.Slot,c.IsMinion?5:d.Damage));
        }
    }
}
