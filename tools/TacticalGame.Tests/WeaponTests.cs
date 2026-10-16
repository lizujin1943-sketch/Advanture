using System;
using System.Collections.Generic;
using System.Linq;
using FrameSync.Runtime;
using Advanture.Tactical.Simulation;
namespace Advanture.Tactical.Tests;

/// <summary>直接执行真实游戏规则：验证射击节奏、遮挡、生命和恢复后的战斗结果。</summary>
internal static class WeaponTests
{
    internal static TacticalMap Duel(params MapRect[] walls) => new TacticalMap(new MapRect(-20000,-20000,20000,20000),
        new[]{new PlayerSpawn(0,0,0),new PlayerSpawn(0,5000,18000)},walls);
    internal static TacticalInput Fire(ushort yaw=0,short pitch=0) => new TacticalInput(0,0,yaw,pitch,true,fire:true);
    internal static CombatState A(SimulationRunner<TacticalState> r) => r.InspectState().Players[0].Combat;
    internal static CombatState B(SimulationRunner<TacticalState> r) => r.InspectState().Players[1].Combat;
    internal static IEnumerable<(string Name,Action Run)> Cases()
    {
        yield return ("weapon.single_shot_damages_nearest_target",()=>{
            var r=SimulationTests.Runner(Duel()); SimulationTests.Step(r,Fire());
            Check.Equal(29,A(r).Ammo);Check.Equal(75,B(r).Health);Check.Equal(1UL,A(r).ShotSequence);
            Check.Equal(1U,A(r).Hits);Check.Equal(1,A(r).LastHitSlot);Check.Equal(4650,A(r).EndZ);
        });
        yield return ("weapon.fire_rate_and_empty_magazine",()=>{
            foreach(uint rate in new uint[]{30,60})
            {
                var r=SimulationTests.Runner(Duel(),rate);
                for(int i=0;i<rate*4;i++)SimulationTests.Step(r,Fire(9000));
                Check.Equal(30UL,A(r).ShotSequence);Check.Equal(0,A(r).Ammo);Check.Equal(100,B(r).Health);
            }
        });
        yield return ("weapon.reload_blocks_fire_and_conserves_ammunition",()=>{
            var r=SimulationTests.Runner(Duel());SimulationTests.Step(r,Fire(9000));
            SimulationTests.Step(r,new TacticalInput(0,0,fire:true,reload:true));
            Check.Equal(74UL,A(r).ReloadUntilTick);Check.Equal(29,A(r).Ammo);
            for(int i=0;i<71;i++)SimulationTests.Step(r,Fire(9000));
            Check.Equal(1UL,A(r).ShotSequence);Check.Equal(29,A(r).Ammo);
            SimulationTests.Step(r,default);Check.Equal(30,A(r).Ammo);Check.Equal(89,A(r).Reserve);Check.Equal(0UL,A(r).ReloadUntilTick);
        });
        yield return ("weapon.wall_blocks_but_low_cover_can_be_shot_over",()=>{
            var r=SimulationTests.Runner(Duel(new MapRect(-1000,2000,1000,2200)));
            SimulationTests.Step(r,Fire());Check.Equal(100,B(r).Health);Check.Equal(-1,A(r).LastHitSlot);Check.Equal(2000,A(r).EndZ);
            r=SimulationTests.Runner(Duel(new MapRect(-1000,2000,1000,2200,0,1000)));
            SimulationTests.Step(r,Fire());Check.Equal(75,B(r).Health);
        });
        yield return ("weapon_pitch_and_range_affect_hits",()=>{
            var r=SimulationTests.Runner(Duel());SimulationTests.Step(r,Fire(0,-8500));Check.Equal(100,B(r).Health);
            for(int i=0;i<3;i++)SimulationTests.Step(r,Fire(0,8500));Check.Equal(100,B(r).Health);Check.Equal(0,A(r).EndY);
            var map=new TacticalMap(new MapRect(-200000,-200000,200000,200000),new[]{new PlayerSpawn(0,0,0),new PlayerSpawn(0,150000,18000)},Array.Empty<MapRect>());
            r=SimulationTests.Runner(map);SimulationTests.Step(r,Fire());Check.Equal(100,B(r).Health);Check.Equal(100000,A(r).EndZ);
        });
        yield return ("weapon.death_disables_actions_then_respawns",()=>{
            var r=SimulationTests.Runner(Duel());for(int i=0;i<10;i++)SimulationTests.Step(r,Fire());
            Check.Equal(0,B(r).Health);Check.Equal(100UL,B(r).RespawnAtTick);Check.Equal(1U,A(r).Kills);Check.Equal(1U,B(r).Deaths);
            for(int i=0;i<89;i++)SimulationTests.Step(r,default,new TacticalInput(1000,0,fire:true,reload:true,respawn:true));
            Check.Equal(0,B(r).Health);Check.Equal(0UL,B(r).ShotSequence);Check.Equal(5000,r.InspectState().Players[1].Z);
            SimulationTests.Step(r,default);Check.Equal(100,B(r).Health);Check.Equal(30,B(r).Ammo);Check.Equal(90,B(r).Reserve);Check.Equal(1U,B(r).Deaths);
        });
        yield return ("weapon.simultaneous_fatal_shots_do_not_favour_slot_zero",()=>{
            var r=SimulationTests.Runner(Duel());for(int i=0;i<10;i++)SimulationTests.Step(r,Fire(),Fire(18000));
            Check.Equal(0,A(r).Health);Check.Equal(0,B(r).Health);Check.Equal(1U,A(r).Kills);Check.Equal(1U,B(r).Kills);
        });
        yield return ("weapon.snapshot_replay_preserves_reload_death_and_shot_history",()=>{
            var r=SimulationTests.Runner(Duel());for(int i=0;i<10;i++)SimulationTests.Step(r,Fire());
            SimulationTests.Step(r,new TacticalInput(0,0,reload:true));var cp=r.CaptureCheckpoint();var recording=new ReplayRecorder(cp);
            for(int i=0;i<130;i++){var frame=SimulationTests.Frame(r.Tick+1,Fire(9000));recording.Append(frame);r.Advance(frame);}
            var other=SimulationTests.Runner(Duel());other.Restore(CheckpointCodec.Decode(CheckpointCodec.Encode(cp)));
            foreach(var f in ReplayCodec.Decode(ReplayCodec.Encode(recording.Build())).Frames)other.Advance(f);
            Check.Equal(CheckpointCodec.Hash(r.CaptureCheckpoint()),CheckpointCodec.Hash(other.CaptureCheckpoint()));
            Check.Equal(1U,B(other).Deaths);Check.True(A(other).ShotSequence>4);
        });
        yield return ("weapon.invalid_combat_state_is_rejected",()=>{
            Check.Throws(()=>new CombatState(health:101));Check.Throws(()=>new CombatState(ammo:31));Check.Throws(()=>new CombatState(reserve:-1));
            Check.Throws(()=>new CombatState(health:0));Check.Throws(()=>new CombatState(health:100,respawnAtTick:30));
        });
    }
}
