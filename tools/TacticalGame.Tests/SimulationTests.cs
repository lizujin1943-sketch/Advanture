using System;
using System.Collections.Generic;
using System.Linq;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using Advanture.Tactical.Simulation;
namespace Advanture.Tactical.Tests;
internal static class SimulationTests
{
    internal static readonly Guid Id=Guid.Parse("b78e09b5-3bd5-45fa-bf6f-8fbed7d05664");
    internal static TacticalMap OpenMap() => new TacticalMap(new MapRect(-20000,-20000,20000,20000),
        new[]{new PlayerSpawn(0,0,0),new PlayerSpawn(5000,0,18000)},Array.Empty<MapRect>());
    internal static SimulationRunner<TacticalState> Runner(TacticalMap? map=null,uint rate=30) =>
        new SimulationRunner<TacticalState>(TacticalGame.CreateModule(),new SessionDescriptor(Id,1,rate,
            new[]{new Participant(0,Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")),new Participant(1,Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"))}),
            new RuntimeLimits(maxInputBytes:9,maxCheckpointBytes:65536),new ByteString((map??OpenMap()).Encode()));
    internal static InputFrame Frame(ulong tick,TacticalInput a,TacticalInput b=default) => new InputFrame(Id,1,tick,
        new[]{new RecordedInput(0,1,a.Encode()),new RecordedInput(1,1,b.Encode())});
    internal static void Step(SimulationRunner<TacticalState> runner,TacticalInput a,TacticalInput b=default)=>runner.Advance(Frame(runner.Tick+1,a,b));
    internal static TacticalPlayer Player(SimulationRunner<TacticalState> runner)=>runner.InspectState().Players[0];
    internal static IEnumerable<(string Name,Action Run)> Cases()
    {
        yield return ("simulation.walk_sprint_and_diagonal_speed",()=>{
            var r=Runner();for(int i=0;i<30;i++)Step(r,new TacticalInput(0,1000));Check.Equal(4500,Player(r).Z);
            r=Runner();for(int i=0;i<30;i++)Step(r,new TacticalInput(1000,0,sprint:true));Check.Equal(7000,Player(r).X);
            r=Runner();for(int i=0;i<30;i++)Step(r,new TacticalInput(707,707));var p=Player(r);Check.True((long)p.X*p.X+(long)p.Z*p.Z<=4500L*4500);Check.True(p.X>3100);
        });
        yield return ("simulation.look_is_shared_and_neutral_preserves_it",()=>{
            var r=Runner();Step(r,new TacticalInput(1000,0,9000,-4000,true),new TacticalInput(0,0,27000,1500,true));
            Check.Equal((ushort)9000,Player(r).Yaw);Check.Equal((short)-4000,Player(r).Pitch);Check.Equal(150,Player(r).X);
            Check.Equal((ushort)27000,r.InspectState().Players[1].Yaw);Step(r,default);Check.Equal((ushort)9000,Player(r).Yaw);Check.Equal(150,Player(r).X);
        });
        yield return ("simulation.swept_wall_and_sliding",()=>{
            var map=new TacticalMap(OpenMap().Area,OpenMap().Spawns,new[]{new MapRect(1000,-2000,1100,2000)});
            var r=Runner(map,1);Step(r,new TacticalInput(1000,0,sprint:true));Check.Equal(650,Player(r).X);
            Step(r,new TacticalInput(707,707));Check.Equal(650,Player(r).X);Check.True(Player(r).Z>3000);
        });
        yield return ("simulation.respawn_resets_orientation_and_remainders",()=>{
            var r=Runner();Step(r,new TacticalInput(333,0,12500,500,true,true));Step(r,new TacticalInput(0,0,respawn:true));
            var p=Player(r);Check.Equal(0,p.X);Check.Equal(0,p.Z);Check.Equal((ushort)0,p.Yaw);Check.Equal((short)0,p.Pitch);Check.Equal(0,p.RemainderX);
        });
        yield return ("simulation.input_rejects_overspeed_and_noncanonical",()=>{
            Check.Throws(()=>new TacticalInput(1000,1000).Encode());Check.Throws(()=>new TacticalInput(0,0,36000,0,true).Encode());
            Check.Throws(()=>new TacticalInput(0,0,0,8501,true).Encode());Check.Throws(()=>TacticalInput.Decode(new byte[10]));
            var bad=new byte[9];bad[8]=128;Check.Throws(()=>TacticalInput.Decode(bad));bad=new byte[9];bad[4]=1;Check.Throws(()=>TacticalInput.Decode(bad));
            var input=new TacticalInput(-500,700,10000,-2000,true,true);Check.True(input.Encode().SequenceEqual(TacticalInput.Decode(input.Encode()).Encode()));
        });
        yield return ("simulation.restore_and_replay_include_fractional_motion",()=>{
            var r=Runner();for(int i=0;i<13;i++)Step(r,new TacticalInput(333,-555,4321,1234,true,true));
            var checkpoint=r.CaptureCheckpoint();var replay=new ReplayRecorder(checkpoint);
            for(int i=0;i<47;i++){var f=Frame(r.Tick+1,new TacticalInput(333,-555,4321,1234,true,true));replay.Append(f);r.Advance(f);}
            string expected=CheckpointCodec.Hash(r.CaptureCheckpoint());var other=Runner();other.Restore(checkpoint);
            foreach(var f in replay.Build().Frames)other.Advance(f);
            Check.Equal(expected,CheckpointCodec.Hash(other.CaptureCheckpoint()));Check.Equal(60UL,other.InspectState().Steps);
        });
        yield return ("simulation.map_and_state_validate_external_bytes",()=>{
            var map=OpenMap();Check.True(map.Encode().SequenceEqual(TacticalMap.Decode(map.Encode()).Encode()));
            Check.Throws(()=>TacticalMap.Decode(new byte[1]));Check.Throws(()=>TacticalMap.Decode(map.Encode().Concat(new byte[1]).ToArray()));
            var codec=new TacticalGame();var state=Runner().InspectState();Check.True(codec.Encode(state).SequenceEqual(codec.Encode(codec.Decode(codec.Encode(state)))));
            Check.Throws(()=>codec.Decode(new byte[9]));Check.Throws(()=>codec.Decode(codec.Encode(state).Concat(new byte[1]).ToArray()));
        });
    }
}
