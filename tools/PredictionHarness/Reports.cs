using System;
using System.Collections.Generic;

namespace FrameSync.PredictionHarness
{
    internal sealed class ClientReport
    {
        public int Slot { get; set; }
        public string Status { get; set; } = "Preparing";
        public ulong ConfirmedTick { get; set; }
        public ulong PredictedTick { get; set; }
        public ulong PresentedTick { get; set; }
        public ulong MaximumPredictedTick { get; set; }
        public ulong MaximumPredictionLead { get; set; }
        public ulong RollbackCount { get; set; }
        public ulong LastRestoreTick { get; set; }
        public long ReplayingPolls { get; set; }
        public ulong FrozenInputs { get; set; }
        public ulong FrozenNeutralInputs { get; set; }
        public ulong ConfirmedFramesChecked { get; set; }
        public long PredictedEvents { get; set; }
        public long InvalidatedEvents { get; set; }
        public long ConfirmedEvents { get; set; }
        public long UdpRetransmissions { get; set; }
        public string FinalHash { get; set; } = "";
    }
    internal sealed class NetworkReport
    {
        public string Profile { get; set; } = "";
        public long SimulatedMilliseconds { get; set; }
        public long SentDatagrams { get; set; }
        public long SentBytes { get; set; }
        public long DeliveredDatagrams { get; set; }
        public long DroppedDatagrams { get; set; }
        public long DuplicateCopies { get; set; }
        public long ReorderedDeliveries { get; set; }
        public long CapacityBackpressure { get; set; }
        public int PeakQueuedDatagrams { get; set; }
        public int LargestDatagramBytes { get; set; }
    }
    internal sealed class CaseReport
    {
        public string Name { get; set; } = "";
        public string Game { get; set; } = "";
        public int ClientCount { get; set; }
        public int Frames { get; set; }
        public bool Passed { get; set; }
        public string? Failure { get; set; }
        public string Phase { get; set; } = "Created";
        public ulong ServerConfirmedTick { get; set; }
        public string ServerFinalHash { get; set; } = "";
        public long AcceptedNeutralInputs { get; set; }
        public long AcceptedScriptedActions { get; set; }
        public ulong ServerRejectedMessages { get; set; }
        public double ElapsedMilliseconds { get; set; }
        public long ManagedAllocatedBytesDuringRun { get; set; }
        public long ManagedHeapAtStartBytes { get; set; }
        public long MaximumSampledManagedHeapBytes { get; set; }
        public long ServerUdpRetransmissions { get; set; }
        public bool AllTransportConnectionsClosed { get; set; }
        public string InitialCheckpointFile { get; set; } = "";
        public string ReplayFile { get; set; } = "";
        public string HashFile { get; set; } = "";
        public object? FinalState { get; set; }
        public List<ClientReport> Clients { get; set; } = new List<ClientReport>();
        public NetworkReport Network { get; set; } = new NetworkReport();
    }
}
