using System;
using System.IO;
using Advanture.HookIsland.Simulation;

namespace Advanture.Tactical.Simulation;

public sealed partial class TacticalGame
{
    private sealed partial class Model
    {
        private const int EncounterSeed=7429;
        private CompoundEncounterTerrain? encounterTerrain;
        private EldritchSystem? monsters;
        private Fisher[] explorers=Array.Empty<Fisher>();

        private void InitializeEncounters()
        {
            encounterTerrain=new CompoundEncounterTerrain(_map);
            monsters=new EldritchSystem(encounterTerrain,CompoundEncounterTerrain.Settings,EncounterSeed,schedule:_map.SpawnSchedule);
            explorers=new Fisher[_players[_players.Length-1].Slot+1];
            for(int i=0;i<explorers.Length;i++)explorers[i]=new Fisher{Slot=(ushort)i,WaitingToJoin=true};
        }
        private void RestoreEncounters(TacticalState state)
        {
            if(encounterTerrain==null)
            {
                if(state.Monsters.Creatures.Count>0)throw new InvalidDataException("本地图没有怪物入口。");
                return;
            }
            var roster=state.Monsters;
            if(roster.Creatures.Count!=_map.SpawnSchedule.Population || roster.NextSpawn!=0 ||
                roster.Director.ElapsedTicks>EncounterClock(state.Steps,_rate) ||
                (roster.Director.PendingCard>0 && !CompoundEncounterTerrain.Settings.Includes((int)_map.SpawnSchedule.Kind(roster.Director.PendingCard-1))))
                throw new InvalidDataException("怪物名册或生成时钟与地图不匹配。");
            foreach(var c in roster.Creatures)
                if(c.Paused || (c.Generation>0 && !CompoundEncounterTerrain.Settings.Includes((int)c.Kind)) ||
                    (c.Alive && !encounterTerrain.CanStand(EldritchCatalog.Get(c.Kind).Habitat,c.X,c.Z)))
                    throw new InvalidDataException("怪物恢复位置或激活状态不合法。");
            monsters=new EldritchSystem(encounterTerrain,CompoundEncounterTerrain.Settings,EncounterSeed,roster,_map.SpawnSchedule);
        }
        private void AdvanceEncounters(ulong tick,bool[] active,CombatModel[] combat)
        {
            for(int i=0;i<_players.Length;i++)
            {
                var p=_players[i];
                explorers[p.Slot]=new Fisher{Slot=p.Slot,X=p.X,Z=p.Z,Unavailable=!active[i],Vitals=new CharacterVitals{Health=combat[i].Health}};
                encounterTerrain!.Observe(p);
            }
            void Hit(EnemyHit hit)
            {
                int i=Array.FindIndex(_players,p=>p.Slot==hit.Slot);
                if(i<0 || !active[i])return;
                DamagePlayer(combat[i],hit.Damage,tick);
                explorers[hit.Slot].Vitals.Health=combat[i].Health;
            }
            ulong substeps=EncounterClock(tick,_rate)-EncounterClock(tick-1,_rate);
            for(ulong i=0;i<substeps;i++)
                monsters!.Advance(Array.Empty<PlatformTile>(),explorers,Hit);
        }
        private int TraceCreatures(int x,int y,int z,int dx,int dy,int dz,ref long nearest)
        {
            int selected=-1;
            if(monsters==null)return selected;
            for(int i=0;i<monsters.Count;i++)
            {
                var c=monsters.Get(i);
                if(!c.Damageable)continue;
                int floor=_map.GroundHeight(c.X,c.Z);
                if(EldritchCatalog.Get(c.Kind).Habitat==EldritchHabitat.Hover)floor+=350;
                int height=c.IsMinion?1100:1900, radius=c.IsMinion?260:EldritchCatalog.BodyRadius;
                if(Intersect(x,y,z,dx,dy,dz,c.X-radius,floor,c.Z-radius,c.X+radius,floor+height,c.Z+radius,nearest,out long at) && at<nearest)
                {nearest=at;selected=i;}
            }
            return selected;
        }
        private void ResolveCreatureHits(CombatModel[] combat)
        {
            for(int i=0;i<combat.Length;i++)
            {
                var shot=combat[i];
                if(shot.LastShotTick!=_steps+1 || shot.LastHitCreature<0)continue;
                int id=shot.LastHitCreature;
                if(monsters!.Strike(id,_players[i].Slot,25,shot.ShotX,shot.ShotZ)>0 && !monsters.Get(id).Alive)
                {
                    monsters.MarkRewarded(id);
                    shot.Kills=checked(shot.Kills+1);
                }
            }
        }
    }
}
