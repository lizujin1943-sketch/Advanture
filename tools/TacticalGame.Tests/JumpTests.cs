using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Advanture.Tactical.Simulation;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Sessions;
using FrameSync.Transport;
using static Advanture.Tactical.Tests.SimulationTests;

namespace Advanture.Tactical.Tests;

internal static class JumpTests
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("jump.single_arc_and_second_press_after_landing", () =>
        {
            foreach (int rate in new[] { 30, 60, 59, 1000 })
            {
                var sim = Runner(rate:(uint)rate);
                int landing = (2*rate + 2)/3;
                for (int tick=1; tick<=landing; tick++)
                {
                    Step(sim,new TacticalInput(1000,0,jump:tick==1 || tick==rate/3));
                    var p = Player(sim);
                    long displacement = 6000L * rate * tick - 9000L * tick * tick;
                    int expected = (int)Math.Max(0, displacement / ((long)rate * rate));
                    Check.Equal(expected, p.HeightAboveGround);
                    Check.Equal(tick < landing, p.IsAirborne);
                }
                Check.True(Player(sim).X > 2900);
                Step(sim,new TacticalInput(0,0,jump:true));
                Check.True(Player(sim).IsAirborne);
                Step(sim,new TacticalInput(0,0,respawn:true));
                Check.True(!Player(sim).IsAirborne);
                Check.Equal(0, Player(sim).X);
            }
        });
        yield return ("jump.midair_checkpoint_replays_submillimetre_motion", () =>
        {
            var first = Runner(rate:59);
            var second = Runner(rate:59);
            Step(first,new TacticalInput(0,0,jump:true));
            for (int i=0;i<8;i++) Step(first,new TacticalInput(333,-555));
            var midair = first.CaptureCheckpoint();
            second.Restore(CheckpointCodec.Decode(CheckpointCodec.Encode(midair)));
            Check.True(Player(second).HeightAboveGround > 600);
            for (int i=0;i<35;i++)
            {
                var input = new TacticalInput(333,-555,jump:i==2);
                Step(first,input);
                Step(second,input);
                Check.Equal(CheckpointCodec.Hash(first.CaptureCheckpoint()), CheckpointCodec.Hash(second.CaptureCheckpoint()));
            }
            Check.True(!Player(first).IsAirborne);
        });
        yield return ("jump.legacy_grounded_state_and_invalid_fractional_height", () =>
        {
            const int header = 14, motionBytes = 12;
            var runner = Runner();
            Step(runner,new TacticalInput(1000,0));
            var checkpoint = runner.CaptureCheckpoint();
            var bytes = checkpoint.ApplicationState.ToArray();
            int stride = (bytes.Length - header) / 2;
            var legacy = bytes.Take(header).Concat(bytes.Skip(header).Take(stride-motionBytes))
                .Concat(bytes.Skip(header+stride).Take(stride-motionBytes)).ToArray();
            legacy[3] = (byte)'2';
            var codec = new TacticalGame();
            var decoded = codec.Decode(legacy);
            Check.True(decoded.Players.All(p=>!p.IsAirborne));
            Check.Equal(150,decoded.Players[0].X);
            Check.True(codec.Encode(decoded).SequenceEqual(bytes));

            var model = codec.Create();
            model.Initialize(new SimulationInitialization(checkpoint.Session,checkpoint.InitializationData));
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(header+stride-motionBytes+4),2*30*30);
            Check.Throws(()=>model.Restore(codec.Decode(bytes)));
        });
        yield return ("jump.hitboxes_and_eye_follow_jump_height", () =>
        {
            var sim = Runner(WeaponTests.Duel());
            Step(sim,new TacticalInput(0,0,jump:true));
            for (int i=0;i<8;i++) Step(sim,default);
            Step(sim,WeaponTests.Fire(),WeaponTests.Fire(18000,1200));
            Check.Equal(2650, WeaponTests.A(sim).ShotY);
            Check.Equal(100, WeaponTests.B(sim).Health);
            Check.Equal(100, WeaponTests.A(sim).Health);
            for (int i=0;i<10;i++) Step(sim,default);
            Step(sim,WeaponTests.Fire(),WeaponTests.Fire(18000,1200));
            Check.Equal(75, WeaponTests.B(sim).Health);
            Check.Equal(75, WeaponTests.A(sim).Health);
        });
        yield return ("jump.four_predicting_clients_match_every_confirmed_frame", FourClients);
    }

    private static void FourClients()
    {
        const int rate = 30;
        var network = new InMemoryNetwork();
        var transport = network.CreateEndpoint(1);
        var module = TacticalGame.CreateModule();
        var neutral = new ByteString(default(TacticalInput).Encode());
        var roster = Enumerable.Range(0,4).Select(i=>new Participant((ushort)i, Guid.NewGuid())).ToArray();
        var map = new TacticalMap(OpenMap().Area, roster.Select(p=>new PlayerSpawn(p.Slot*2000,0,0)).ToArray(), Array.Empty<MapRect>());
        var clients = roster.Select(p=>new ClientSession<TacticalState>(module, Id, 1, network.CreateEndpoint(2UL+p.Slot), 1,
            prediction:new PredictionOptions(maxPredictionFrames:12), neutralInput:_=>neutral)).ToArray();
        var server = new ServerSession<TacticalState>(module, new SessionDescriptor(Id,1,rate,roster), transport,
            roster.Select(p=>new PeerBinding(2UL+p.Slot, p.Slot)).ToArray(), _=>neutral, initialization:new ByteString(map.Encode()));
        TimeSpan now = default;
        for (int i=0;i<20;i++)
        {
            server.Poll(now);
            foreach (var client in clients) client.Poll(0);
        }
        Check.True(clients.All(c=>c.Status==SessionStatus.Running));
        bool allAirborne = false;
        for (ulong tick=1; tick<=45; tick++)
        {
            for (int slot=0;slot<clients.Length;slot++)
            {
                var input = new TacticalInput(0,500,jump:tick==5UL+(ulong)slot*2);
                Check.True(clients[slot].TrySubmit(tick,input.Encode()));
                clients[slot].Poll(tick);
            }
            server.Poll(now);
            now = TimeSpan.FromTicks(((long)tick * TimeSpan.TicksPerSecond + rate-1)/rate);
            server.Poll(now);
            Check.Equal(tick, server.ConfirmedTick);
            string expected = CheckpointCodec.Hash(server.CaptureCheckpoint());
            foreach (var client in clients)
            {
                client.Poll(tick);
                Check.Equal(expected, CheckpointCodec.Hash(client.CaptureCheckpoint()));
            }
            allAirborne |= server.InspectState().Players.All(p=>p.HeightAboveGround>450);
        }
        Check.True(allAirborne && server.InspectState().Players.All(p=>!p.IsAirborne));
        Check.True(clients.All(c=>c.RollbackCount>0));
    }
}
