using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using Advanture.HookIsland.Network;
using Advanture.HookIsland.Simulation;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Sessions;

namespace Advanture.HookIsland.RestartTests;
internal static partial class Program
{
    private static int Main(string[] args)
    {
        var tests = Cases().Concat(ProtocolCases()).Concat(AdversarialCases()).Concat(EconomyCases()).Concat(FinaleCases()).Concat(MerchantCases()).Where(t => args.Length == 0 || t.Item1.Contains(args[0])).ToArray();
        int failed = 0;
        foreach (var (name, run) in tests)
            try { run(); Console.WriteLine("PASS " + name); }
            catch (Exception e) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + e); }
        Console.WriteLine($"Restart tests: {tests.Length - failed}/{tests.Length} passed");
        return failed == 0 && tests.Length > 0 ? 0 : 1;
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static bool Restart(IslandHost host) => host.TryRestart();
    private static IEnumerable<(string,Action)> Cases()
    {
        yield return ("starting_coins_four_clients_restart_resets_each_wallet_to_3000",()=>{
            // Keep revival unaffordable in this restart fixture; funded dead players can otherwise still revive.
            using var party=new Party(4,Map(3000,1000000));party.Until(()=>party.Running,_=>default);party.Flush();
            Require(party.Host.Session!.InspectState().Players.All(p=>p.Coins==3000),"Initial wallets differ");
            // Walk outward from each spawn; walking through the opposite player can block departure.
            party.Until(()=>party.Host.Session!.InspectState().GameOver,c=>new HookInput(c.Slot==0 || c.Slot==3?(short)-1000:(short)1000,0));party.Flush();
            var previous=party.Host.Session;Require(Restart(party.Host),"Could not restart funded game");
            party.Until(()=>!ReferenceEquals(previous,party.Host.Session) && party.Running,_=>default);party.Flush();
            var state=party.Host.Session!.InspectState();
            Require(state.Coins==12000 && state.Players.All(p=>p.Coins==3000 && p.CoinsEarned==3000) && state.Tackle!.Inventory.SpentCoins.All(n=>n==0),"Restart stacked starting money or preserved spending");
            string hash=CheckpointCodec.Hash(party.Host.Session.CaptureCheckpoint());
            Require(party.Clients.All(c=>CheckpointCodec.Hash(c.Session!.CaptureCheckpoint())==hash),"Restart wallets diverged");
            Console.WriteLine("STARTING_COINS_RESTART clients=4 coins=3000 hash="+hash);
        });
        foreach(int count in new[]{1,4})yield return ($"room_rules_restart_preserves_disabled_policy_{count}_players",()=>{
            using var party=new Party(count);party.Until(()=>party.Running,_=>default);
            party.Until(()=>!party.Host.Session!.InspectState().RoomRules.FriendlyPranksEnabled,
                c=>c.Slot==0?new HookInput(0,0,roomRules:new RoomRulesCommand(false,1)):default);
            party.Until(()=>party.Host.Session!.InspectState().GameOver,LeavePlatform);party.Flush();
            var retired=party.Host.Session!;Require(Restart(party.Host),"Could not restart disabled room.");
            party.Until(()=>!ReferenceEquals(retired,party.Host.Session),_=>default);
            var state=party.Host.Session!.InspectState();
            Require(state.Steps==0 && !state.RoomRules.FriendlyPranksEnabled && state.RoomRules.LastRequest==0,
                "Restart did not retain the confirmed rule from tick zero while resetting its request receipt.");
            party.Until(()=>party.Running,_=>default);party.Flush();
            Require(party.Clients.All(c=>!c.Session!.InspectState().RoomRules.FriendlyPranksEnabled),"Client initial policy differs.");
            party.Until(()=>party.Clients.All(c=>c.Session!.InspectState().RoomRules.FriendlyPranksEnabled),
                c=>c.Slot==0?new HookInput(0,0,roomRules:new RoomRulesCommand(true,1)):default);
            party.Flush();var checkpoint=CheckpointCodec.Hash(party.Host.Session.CaptureCheckpoint());
            Require(party.Clients.All(c=>CheckpointCodec.Hash(c.Session!.CaptureCheckpoint())==checkpoint),"Room policy differs after restart.");
        });
        yield return ("network_single_player_two_rounds_keep_socket_and_reset_everything", () => RestartRounds(1));
        yield return ("network_four_players_two_rounds_keep_identity_and_reset_everything", () => RestartRounds(4));
    }
    private static IslandMap Map(int startingCoins=0,int baseFee=10)
    {
        var m = IslandMap.CreatePlatform();
        return new IslandMap(m.Shore, m.Pier, m.Spawns, m.FishSpots, m.Platform,
            new FishingConfig(swimming: new SwimmingConfig(healthLossBasisPointsPerSecond: 10000),
                revival: new RevivalConfig(downedTicks: 30,baseFee:baseFee),startingCoins:startingCoins));
    }
    // 出生和开发场景的队形可能不同；按实际所处半边向外走，避免同排玩家交叉离场时互相阻挡。
    private static HookInput LeavePlatform(IslandClient c) => new HookInput(c.Session!.InspectState().Players[c.Slot].X<0?(short)-1000:(short)1000,0);
    private static void RestartRounds(int count)
    {
        using var party = new Party(count);
        Require(!Restart(party.Host), "Unstarted room restarted.");
        party.Until(() => party.Running, _ => default);
        Require(!Restart(party.Host), "Living team restarted.");
        var first = party.Host.Session!.CaptureCheckpoint();
        int hostPort = party.Host.Port;
        int[] ports = party.Clients.Select(c => c.LocalPort).ToArray();
        ushort[] slots = party.Clients.Select(c => c.Slot).ToArray();
        for (ulong epoch = 2; epoch <= 3; epoch++)
        {
            party.Until(() => party.Host.Session!.InspectState().GameOver, LeavePlatform);
            party.Flush();
            var oldServer = party.Host.Session!;
            var oldClient = party.Clients[0].Session!;
            Require(Restart(party.Host), "Failed team could not restart.");
            Require(!Restart(party.Host), "Double click restarted twice.");
            if (count == 4 && epoch == 2)
            {
                for (int i = 0; i < 100; i++) { party.Poll(_ => default, skip: 3); Thread.Sleep(2); }
                Require(ReferenceEquals(party.Host.Session, oldServer) && party.Host.Restarting, "Room started without the final peer.");
                Require(party.Clients.Take(3).All(c => c.Restarting && c.LastSubmittedTick == 0), "Waiting peers sampled new input too early.");
            }
            party.Until(() => !ReferenceEquals(party.Host.Session, oldServer), _ => default);
            var initial = party.Host.Session!.CaptureCheckpoint();
            Require(initial.Tick == 0 && initial.Session.Epoch == epoch, "New round did not begin at tick zero and next epoch.");
            Require(initial.Session.SessionId == first.Session.SessionId && initial.Session.BaseTick == 0, "Room identity/round timer changed incorrectly.");
            Require(initial.Session.Participants.Select(p => p.ParticipantId).SequenceEqual(first.Session.Participants.Select(p => p.ParticipantId)), "Participant identities changed.");
            var model = new HookGame().Create();
            model.Initialize(new SimulationInitialization(initial.Session, initial.InitializationData));
            Require(new HookGame().Encode(model.Capture()).AsSpan().SequenceEqual(initial.ApplicationState.Span), "New round did not restore the complete fresh state.");
            party.Until(() => party.Running, _ => default);
            Require(party.Host.Port == hostPort && party.Clients.Select(c => c.LocalPort).SequenceEqual(ports) && party.Clients.Select(c => c.Slot).SequenceEqual(slots), "Restart recreated sockets or seats.");
            Require(party.Clients.All(c => c.Session!.CaptureCheckpoint().Session.Epoch == epoch), "Client skipped the round switch.");
            // Delayed old input and terminal Close must be harmless in the new session.
            Require(oldClient.TrySubmit(oldClient.ConfirmedTick + 100, new HookInput(1000, 0, fire: true).Encode()), "Could not enqueue delayed old input.");
            oldClient.Poll();
            oldServer.SaveAndClose(_ => { });
            oldServer.Poll(party.Now);
            party.Until(() => party.Host.Session!.ConfirmedTick >= 15, _ => default);
            party.Flush();
            Require(party.Host.Session!.RejectedMessages > 0, "Old-epoch input was not rejected.");
            Require(party.Clients.All(c => c.Session!.RejectedMessages > 0 && c.Failure == null), "Old Close disrupted the new session.");
            var state = party.Host.Session.InspectState();
            Require(!state.GameOver && state.Players.All(p => !p.Vitals.Downed && p.Coins == 0 && p.Revival.Deaths == 0 && p.Casts == 0), "Old actions or lifecycle leaked into the new round.");
            string hash = CheckpointCodec.Hash(party.Host.Session.CaptureCheckpoint());
            Require(party.Clients.All(c => CheckpointCodec.Hash(c.Session!.CaptureCheckpoint()) == hash), "Full restart checkpoint differs.");
            Console.WriteLine($"RESTART_NETWORK players={count} epoch={epoch} tick={state.Steps} port={hostPort} hash={hash}");
        }
        party.Until(() => party.Host.Session!.InspectState().Players.All(p => p.Casts > 0), c => new HookInput(0, 0, 6000, 0, true));
        Require(!Restart(party.Host), "Healthy new round restarted.");
    }
    private sealed class Party : IDisposable
    {
        internal readonly IslandHost Host;
        internal readonly IslandClient[] Clients;
        internal readonly int[] Changes;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        internal TimeSpan Now;
        internal Party(int count, IslandMap? map = null)
        {
            map ??= Map();
            Host = new IslandHost(map, 0, count);
            Clients = Enumerable.Range(0, count).Select(i => new IslandClient(map, IPAddress.Loopback, Host.Port, i == 0 ? (ushort)0 : ushort.MaxValue)).ToArray();
            Changes = new int[count];
            for (int i = 0; i < count; i++)
            {
                int index = i; var c = Clients[i];
                c.RoundChanged += () => {
                    Require(c.LastSubmittedTick == 0 && c.Session?.LocalParticipant == null, "Round event ran after input/initial state.");
                    Changes[index]++;
                };
            }
        }
        internal bool Running => Host.Session?.Status == SessionStatus.Running && Clients.All(c => c.Session?.Status == SessionStatus.Running);
        internal void Poll(Func<IslandClient,HookInput> input, bool advance = true, int skip = -1)
        {
            if (advance) Now = clock.Elapsed;
            Host.Poll(Now);
            for (int i = 0; i < Clients.Length; i++)
            {
                if (i != skip) Clients[i].Poll(Now, _ => {
                    Require(Changes[i] == (int)Clients[i].RoundEpoch - 1, "Sample ran before new-round cleanup.");
                    return input(Clients[i]);
                });
                Require(Clients[i].Failure == null, Clients[i].Failure ?? "");
            }
            Require(Host.Failure == null, Host.Failure ?? "");
        }
        internal void Until(Func<bool> done, Func<IslandClient,HookInput> input, int seconds = 12)
        {
            var deadline = clock.Elapsed + TimeSpan.FromSeconds(seconds);
            while (!done() && clock.Elapsed < deadline) { Poll(input); Thread.Sleep(1); }
            Require(done(), "Room condition timed out.");
        }
        internal void Flush() { for (int i = 0; i < 300; i++) Poll(_ => default, false); }
        public void Dispose() { foreach (var c in Clients) c.Dispose(); Host.Dispose(); }
    }
}
