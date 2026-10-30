using System;
using System.Collections.Generic;
using System.Linq;

namespace Advanture.HookIsland.Simulation;

/// <summary>Room-owned enemy AI. Call after hook collisions, so the final windup tick can be interrupted.</summary>
public sealed class EnemySystem
{
    private readonly EnemyConfig config;
    private readonly Piranha[] piranhas;
    private readonly List<Waterball> waterballs;
    private WaterMonster monster;
    private uint serial;
    private int bites,shots,interruptions,waterHits,captures;
    public EnemySystem(EnemyConfig config,EnemyState? state=null)
    {
        this.config=config;
        if(state!=null)
        {
            piranhas=state.Piranhas.ToArray();waterballs=state.Waterballs.ToList();monster=state.Monster;
            serial=state.Serial;bites=state.Bites;shots=state.Shots;interruptions=state.Interruptions;waterHits=state.WaterHits;captures=state.Captures;
            return;
        }
        piranhas=new Piranha[config.PiranhaCount];waterballs=new List<Waterball>();
        for(int i=0;i<piranhas.Length;i++)
        {
            var home=EnemyPlacement.PiranhaHome(i);
            piranhas[i]=new Piranha {Id=i,X=home.X,Z=home.Z,Target=-1,Owner=-1,
                WeightGrams=config.MinWeightGrams+(int)((long)(config.MaxWeightGrams-config.MinWeightGrams)*(2*i+1)/(2*piranhas.Length))};
        }
        monster=config.MonsterEnabled?new WaterMonster {Timer=config.InitialDelayTicks,Target=-1,X=config.MonsterX,Z=config.MonsterZ,Level=config.MonsterLevel}:default;
    }
    public EnemyState Capture()=>new EnemyState(piranhas,monster,waterballs,serial,bites,shots,interruptions,waterHits,captures);
    public int PiranhaCount=>piranhas.Length;
    public WaterMonster Monster=>monster;
    public Piranha GetPiranha(int id)=>piranhas[id];
    public bool TryHook(int id,int owner)
    {
        if(id<0 || id>=piranhas.Length || owner<0 || owner>=4 || piranhas[id].Owner>=0 || piranhas[id].RespawnTicks>0 || piranhas[id].Escape.Active)return false;
        piranhas[id].Owner=owner;piranhas[id].Target=-1;return true;
    }
    public void Release(int id,int owner,Point landing,int height=1250)
    {
        if(id<0 || id>=piranhas.Length || piranhas[id].Owner!=owner)return;
        ref var fish=ref piranhas[id];fish.Owner=fish.Target=-1;fish.StunTicks=0;
        fish.Escape=CatchEscapeMotion.Begin(fish.X,height,fish.Z,landing);
    }
    public void Follow(int id,int owner,int x,int z)
    {if(piranhas[id].Owner==owner){piranhas[id].X=x;piranhas[id].Z=z;}}
    internal void TransferHook(int id,int owner)
    {
        if(piranhas[id].Owner<0 || owner<0 || owner>=4)throw new InvalidOperationException("No held piranha to transfer.");
        piranhas[id].Owner=owner;
    }
    public bool CompleteCapture(int id,int owner)
    {
        if(id<0 || id>=piranhas.Length || piranhas[id].Owner!=owner)return false;
        piranhas[id].Owner=-1;piranhas[id].Target=-1;piranhas[id].StunTicks=0;piranhas[id].RespawnTicks=config.PiranhaRespawnTicks;
        Increment(ref captures);return true;
    }
    internal bool Strike(int id,int damage,MeleeConfig melee)
    {
        ref var f=ref piranhas[id];
        if(f.Owner>=0 || f.RespawnTicks>0 || f.Escape.Active)return false;
        f.DamageTaken=Math.Min(melee.PiranhaHealth,f.DamageTaken+damage);f.Target=-1;f.StunTicks=melee.StunTicks;
        if(f.DamageTaken==melee.PiranhaHealth){f.RespawnTicks=config.PiranhaRespawnTicks;f.StunTicks=0;}
        return true;
    }
    public bool Interrupt()
    {
        if(!config.MonsterEnabled || monster.Phase!=MonsterPhase.Windup)return false;
        // Hooks resolve before Advance: consume the extra tick in the interruption frame itself.
        monster.Phase=MonsterPhase.Cooldown;monster.Timer=config.InterruptDelayTicks+1;monster.Target=-1;
        monster.DiveTicks=16;
        Increment(ref interruptions);return true;
    }
    public void Advance(IReadOnlyList<Fisher> players,Action<EnemyHit> hit,Func<int,int,bool>? waterPosition=null,bool allowNewAttacks=true)
    {
        for(int i=0;i<piranhas.Length;i++)
        {
            var f=piranhas[i];
            if(f.Escape.Active)
            {
                if(CatchEscapeMotion.Advance(ref f.Escape))CatchEscapeMotion.Position(f.Escape,out f.X,out _,out f.Z);
                else f.RespawnTicks=config.PiranhaRespawnTicks;
                piranhas[i]=f;continue;
            }
            if(f.RespawnTicks>0)
            {
                if(--f.RespawnTicks==0){var home=EnemyPlacement.PiranhaHome(i);f.X=home.X;f.Z=home.Z;f.Cooldown=0;f.DamageTaken=f.StunTicks=0;f.TravelRemainder=f.MovementRemainderX=f.MovementRemainderZ=0;}
                piranhas[i]=f;continue;
            }
            if(f.Owner>=0)continue;
            if(f.StunTicks>0){f.StunTicks--;f.Target=-1;piranhas[i]=f;continue;}
            if(f.Cooldown>0)f.Cooldown--;
            f.Target=Nearest(players,f.X,f.Z,config.AggroRange,true);
            if(f.Target>=0)
            {
                var p=players[f.Target];
                // Piranhas swim beneath the deck; only a player in open water can be bitten.
                Move(ref f,p.X,p.Z,config.PiranhaSpeed,config.BiteRange/2);
                if(f.Cooldown==0 && IslandMap.DistanceSquared(f.X,f.Z,p.X,p.Z)<=(long)config.BiteRange*config.BiteRange)
                {f.Cooldown=config.BiteIntervalTicks;Increment(ref bites);hit(new EnemyHit(f.Target,config.BiteDamage(f.WeightGrams)));}
            }
            else
            {
                var goal=EnemyPlacement.PatrolGoal(f);
                if(IslandMap.DistanceSquared(f.X,f.Z,goal.X,goal.Z)<2500){f.PatrolPoint=(f.PatrolPoint+1)%4;goal=EnemyPlacement.PatrolGoal(f);}
                Move(ref f,goal.X,goal.Z,config.PatrolSpeed,0);
            }
            piranhas[i]=f;
        }
        if(config.MonsterEnabled)
        {
            // Interrupt adds one scheduling tick because hooks resolve before this update.
            // Consume that tick even during quiet; the actual configured cooldown then stays frozen.
            bool justInterrupted=monster.Phase==MonsterPhase.Cooldown && monster.DiveTicks==16;
            if(monster.DiveTicks>0)monster.DiveTicks--;
            if(monster.Timer>0 && (monster.Phase==MonsterPhase.Windup || allowNewAttacks || justInterrupted))monster.Timer--;
            if(monster.Phase==MonsterPhase.Windup)
            {
                // A vanished/downed target cancels the pending shot instead of aiming at an abandoned body.
                if(monster.Target<0 || (!Eligible(players[monster.Target],false) || (waterPosition!=null && !waterPosition(monster.X,monster.Z))))
                {monster.Phase=MonsterPhase.Cooldown;monster.Timer=config.AttackIntervalTicks;monster.Target=-1;monster.DiveTicks=15;}
                else if(monster.Timer==0)
                {
                    waterballs.Add(new Waterball {Id=unchecked(++serial),X=monster.X,Z=monster.Z,OriginX=monster.X,
                        OriginZ=monster.Z,DirectionX=monster.DirectionX,DirectionZ=monster.DirectionZ,KnockbackDistance=config.KnockbackDistance});
                    Increment(ref shots);monster.Phase=MonsterPhase.Cooldown;monster.Timer=config.AttackIntervalTicks;monster.Target=-1;monster.DiveTicks=15;
                }
            }
            else if(monster.Timer==0 && allowNewAttacks)
            {
                if(EnemyPlacement.Surface(config,players,ref monster,waterPosition))
                {
                    var p=players[monster.Target];EnemyGeometry.Direction(p.X-monster.X,p.Z-monster.Z,out monster.DirectionX,out monster.DirectionZ);
                    monster.Phase=MonsterPhase.Windup;monster.Timer=config.WindupTicks;monster.DiveTicks=0;
                }
                else {monster.Timer=30;monster.Target=-1;}
            }
        }
        // Stable spawn order and nearest swept hit; removing a projectile is the hit's exactly-once receipt.
        for(int i=0;i<waterballs.Count;)
        {
            var b=waterballs[i];int oldX=b.X,oldZ=b.Z;
            b.Distance+=Math.Min(FishingRules.Travel(config.WaterballSpeed,ref b.TravelRemainder),config.WaterballRange-b.Distance);
            b.X=b.OriginX+(int)((long)b.DirectionX*b.Distance/EnemyGeometry.DirectionScale);
            b.Z=b.OriginZ+(int)((long)b.DirectionZ*b.Distance/EnemyGeometry.DirectionScale);
            int target=-1,nearest=int.MaxValue;
            for(int j=0;j<players.Count;j++)
            {
                var p=players[j];if(!Eligible(p,false))continue;
                int fraction=EnemyGeometry.HitFraction(oldX,oldZ,b.X,b.Z,p.X,p.Z,config.WaterballRadius+EnemyGeometry.PlayerRadius);
                if(fraction<nearest){nearest=fraction;target=j;}
            }
            if(target>=0){Increment(ref waterHits);hit(new EnemyHit(target,config.WaterballDamage,true,b.DirectionX,b.DirectionZ,b.KnockbackDistance));}
            if(target>=0 || b.Distance>=config.WaterballRange)waterballs.RemoveAt(i);
            else {waterballs[i]=b;i++;}
        }
    }
    private static void Move(ref Piranha f,int x,int z,int speed,int stop)
    {
        int dx=x-f.X,dz=z-f.Z,length=HookGame.Sqrt((long)dx*dx+(long)dz*dz);
        int step=Math.Min(FishingRules.Travel(speed,ref f.TravelRemainder),Math.Max(0,length-stop));
        if(step==0)return;
        EnemyGeometry.Direction(dx,dz,out int dirX,out int dirZ);
        long ax=(long)dirX*step+f.MovementRemainderX,az=(long)dirZ*step+f.MovementRemainderZ;
        f.X+=(int)(ax/EnemyGeometry.DirectionScale);f.Z+=(int)(az/EnemyGeometry.DirectionScale);
        f.MovementRemainderX=(int)(ax%EnemyGeometry.DirectionScale);f.MovementRemainderZ=(int)(az%EnemyGeometry.DirectionScale);
    }
    private static bool Eligible(Fisher p,bool water)=>!p.WaitingToJoin && !p.Vitals.Downed && (!water || p.Swimming.InWater);
    private static int Nearest(IReadOnlyList<Fisher> players,int x,int z,int range,bool water)
    {
        int target=-1;long best=(long)range*range+1;
        for(int i=0;i<players.Count;i++)
        {
            var p=players[i];if(!Eligible(p,water))continue;long d=IslandMap.DistanceSquared(x,z,p.X,p.Z);
            if(d<best){target=i;best=d;}
        }
        return target;
    }
    private static void Increment(ref int counter){if(counter<int.MaxValue)counter++;}
}
