using System;
using System.Collections.Generic;
using System.Linq;
namespace ReliableUdp.Tests
{
    internal static class ProtocolTests
    {
        internal static IEnumerable<(string,Action)> Cases()
        {
            yield return ("Protocol.CookieHandshakeIsIdempotent", Handshake);
            yield return ("Protocol.ReliableOrderedSurvivesLossDuplicationReorder", Loss);
            yield return ("Protocol.MaximumMessageAndIndependentChannels", Maximum);
            yield return ("Protocol.UnorderedDeliversLaterMessageBeforeGap", Unordered);
            yield return ("Protocol.UnreliableDoesNotRetransmit", Unreliable);
            yield return ("Protocol.AckLossRetransmitsWithoutDoubleDelivery", AckLoss);
            yield return ("Protocol.ApplicationConsumptionReleasesCredit", Flow);
            yield return ("Protocol.StalledConsumptionFailsInsteadOfDiscarding", Stalled);
            yield return ("Protocol.DisconnectFailsPendingReceipts", Disconnect);
            yield return ("Protocol.MonotonicClockAndBounds", Bounds);
            yield return ("Protocol.SocketBackpressureDoesNotConsumeSequence", PortBackpressure);
            yield return ("Protocol.CloseRetriesAndCleansConnections", Close);
            yield return ("Protocol.InvalidTrafficDoesNotAllocateConnections", InvalidTraffic);
            yield return ("Protocol.IncompatibleProfilesCannotConnect", Incompatible);
        }
        private static UdpOptions Single(UdpDelivery mode,int bytes=65536,int messages=64) => new UdpOptions(new[] {new ChannelOptions(mode,bytes)},receiveMessagesPerChannel:messages);
        private static byte[] Data(int length,int seed) { var bytes=new byte[length]; new Random(seed).NextBytes(bytes); return bytes; }
        private static void Handshake()
        {
            var p=new Pair(start:false); int accepts=0;
            p.Network.Schedule=(a,b,packet,n)=> {
                if(packet?.Kind==Kind.Accept && ++accepts==1) return Array.Empty<double>();
                if(packet?.Kind==Kind.AcceptAck && accepts<3) return Array.Empty<double>();
                return new[] {0.005,0.015}; };
            p.Start(); for(int i=0;i<400;i++) p.Step();
            Check.Equal(1,p.A.ConnectionCount); Check.Equal(1,p.B.ConnectionCount);
            Check.Equal(2,p.Events.Count(x=>x.Kind==ConnectionEventKind.Connected));
            Check.Equal(SendResult.Accepted,p.A.TrySend(p.AId,0,new byte[]{7},out var receipt));
            p.Until(()=>receipt!.Status==ReceiptStatus.Acknowledged); Check.Equal(1,p.ReceiveB().Count);
        }
        private static void Loss()
        {
            var p=new Pair(); var random=new Random(9083);
            p.Network.Schedule=(a,b,k,n)=>random.Next(10)==0 ? Array.Empty<double>() : new[] {random.Next(1,35)/1000.0,random.Next(35,60)/1000.0};
            var expected=Enumerable.Range(0,48).Select(i=>Data(1+i*47,i)).ToArray(); var receipts=new List<SendReceipt>(); var received=new List<byte[]>(); int sent=0;
            p.Until(()=> {
                while(sent<expected.Length && p.A.TrySend(p.AId,0,expected[sent],out var r)==SendResult.Accepted) { receipts.Add(r!); sent++; }
                received.AddRange(p.ReceiveB()); return received.Count==expected.Length && receipts.All(r=>r.Status==ReceiptStatus.Acknowledged);
            },60);
            for(int i=0;i<expected.Length;i++) Check.Bytes(expected[i],received[i]);
            var stats=p.A.GetStatistics(p.AId); Check.True(stats.Retransmissions>0); Check.Equal(0,stats.PendingSendBytes);
            Check.True(stats.InFlightBytes>=0 && stats.InFlightPackets<=64); Check.True(p.Network.LargestDatagram<=1200);
        }
        private static void Maximum()
        {
            var p=new Pair(); byte[] big=Data(65536,1); double unlock=p.Network.Now+1.5;
            p.Network.Schedule=(a,b,k,n)=>k?.Kind==Kind.Fragment && k.Channel==3 && p.Network.Now<unlock ? Array.Empty<double>() : new[] {0.005};
            Check.Equal(SendResult.Accepted,p.A.TrySend(p.AId,3,big,out var receipt));
            Check.Equal(SendResult.Accepted,p.A.TrySend(p.AId,1,new byte[]{42},out _));
            var received=new List<byte[]>(); p.Until(()=>{ received.AddRange(p.ReceiveB()); return received.Count>0; });
            Check.Bytes(new byte[]{42},received[0]); Check.True(p.Network.Now<unlock,"Independent control was blocked by large message");
            p.Until(()=>{ received.AddRange(p.ReceiveB()); return received.Count==2 && receipt!.Status==ReceiptStatus.Acknowledged; },30);
            Check.Bytes(big,received[1]);
        }
        private static void Unordered()
        {
            var p=new Pair(Single(UdpDelivery.ReliableUnordered)); bool drop=true;
            p.Network.Schedule=(a,b,k,n)=>drop && k?.Kind==Kind.Fragment && System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(k.Body.AsSpan(8))==1 ? Array.Empty<double>() : new[]{0.005};
            p.A.TrySend(p.AId,0,new byte[]{1},out var first); p.A.TrySend(p.AId,0,new byte[]{2},out _);
            var received=new List<byte[]>(); p.Until(()=>{received.AddRange(p.ReceiveB());return received.Count>0;}); Check.Bytes(new byte[]{2},received[0]);
            drop=false; p.Until(()=>{received.AddRange(p.ReceiveB());return received.Count==2 && first!.Status==ReceiptStatus.Acknowledged;}); Check.Bytes(new byte[]{1},received[1]);
        }
        private static void Unreliable()
        {
            var p=new Pair(Single(UdpDelivery.UnreliableUnordered,900)); int dataPackets=0;
            p.Network.Schedule=(a,b,k,n)=> { if(k?.Kind==Kind.Unreliable) {dataPackets++;return Array.Empty<double>();} return new[]{0.005}; };
            Check.Equal(SendResult.Accepted,p.A.TrySend(p.AId,0,new byte[]{1},out var r)); Check.True(r==null);
            for(int i=0;i<600;i++)p.Step(); Check.Equal(1,dataPackets); Check.Equal(0,p.ReceiveB().Count);
            Check.Equal(SendResult.TooLarge,p.A.TrySend(p.AId,0,new byte[901],out _));
            p.A.TrySend(p.AId,0,new byte[]{2},out _,TimeSpan.FromSeconds(p.Network.Now-1)); p.Step(); Check.Equal(1,dataPackets);
        }
        private static void AckLoss()
        {
            var p=new Pair(); double until=p.Network.Now+2.1;
            p.Network.Schedule=(a,b,k,n)=>a.Equals(p.BPort.LocalAddress) && p.Network.Now<until ? Array.Empty<double>() : new[]{0.005};
            p.A.TrySend(p.AId,0,new byte[]{9},out var receipt); var received=new List<byte[]>();
            p.Until(()=>{received.AddRange(p.ReceiveB());return receipt!.Status==ReceiptStatus.Acknowledged;});
            for(int i=0;i<50;i++)p.Step(); received.AddRange(p.ReceiveB()); Check.Equal(1,received.Count);
            Check.True(p.A.GetStatistics(p.AId).Retransmissions>0); Check.True(p.A.GetStatistics(p.AId).InFlightBytes>=0);
        }
        private static void Flow()
        {
            var p=new Pair(Single(UdpDelivery.ReliableOrdered,900,2));
            p.A.TrySend(p.AId,0,new byte[]{1},out var a); p.A.TrySend(p.AId,0,new byte[]{2},out var b);
            p.Until(()=>a!.Status==ReceiptStatus.Acknowledged && b!.Status==ReceiptStatus.Acknowledged);
            Check.Equal(SendResult.Backpressure,p.A.TrySend(p.AId,0,new byte[]{3},out _));
            Check.Equal(2,p.B.GetStatistics(p.BId).BufferedReceiveMessages);
            p.ReceiveB(); double until=p.Network.Now+1.3;
            p.Network.Schedule=(x,y,k,n)=>k?.Kind==Kind.Credit && p.Network.Now<until ? Array.Empty<double>() : new[]{0.005};
            SendReceipt? last=null; p.Until(()=>p.A.TrySend(p.AId,0,new byte[]{3},out last)==SendResult.Accepted);
            p.Until(()=>last!.Status==ReceiptStatus.Acknowledged); Check.Bytes(new byte[]{3},p.ReceiveB().Single());
        }
        private static void Stalled()
        {
            var p=new Pair(new UdpOptions(progressTimeout:TimeSpan.FromSeconds(2)));
            p.A.TrySend(p.AId,0,new byte[]{7},out var receipt); p.Until(()=>receipt!.Status==ReceiptStatus.Acknowledged);
            p.Until(()=>p.B.ConnectionCount==0,4); Check.True(p.Events.Any(e=>e.Kind==ConnectionEventKind.Disconnected));
        }
        private static void Disconnect()
        {
            var p=new Pair(new UdpOptions(idleTimeout:TimeSpan.FromSeconds(2)));
            p.Network.Schedule=(a,b,k,n)=>Array.Empty<double>(); p.A.TrySend(p.AId,0,new byte[]{1},out var receipt);
            p.Until(()=>receipt!.Status==ReceiptStatus.Failed,4); Check.Equal(0,p.A.ConnectionCount); Check.True(receipt!.Failure!=null);
        }
        private static void Bounds()
        {
            Check.Throws<NotSupportedException>(()=>new UdpOptions(requireAuthenticatedSecurity:true));
            Check.Throws<ArgumentException>(()=>new UdpOptions(fragmentWindow:1));
            var p=new Pair(); Check.Throws<ArgumentOutOfRangeException>(()=>p.A.Poll(TimeSpan.Zero));
            Check.Equal(SendResult.TooLarge,p.A.TrySend(p.AId,0,new byte[65537],out _));
            Check.Throws<ArgumentOutOfRangeException>(()=>p.A.TrySend(p.AId,9,new byte[]{1},out _));
            Check.Equal(SendResult.NotConnected,p.A.TrySend(900,0,new byte[]{1},out _));
        }
        private static void PortBackpressure()
        {
            var p=new Pair(); p.APort.BlockSend=true; p.A.TrySend(p.AId,0,new byte[]{3},out var receipt);
            for(int i=0;i<40;i++)p.Step(); Check.Equal(ReceiptStatus.Pending,receipt!.Status); Check.Equal(0,p.A.GetStatistics(p.AId).InFlightPackets);
            p.APort.BlockSend=false;p.Until(()=>receipt.Status==ReceiptStatus.Acknowledged); Check.Equal(1,p.ReceiveB().Count);
        }
        private static void Close()
        {
            var p=new Pair(); int drops=0; p.Network.Schedule=(a,b,k,n)=> k?.Kind==Kind.Close && drops++==0 ? Array.Empty<double>() : new[]{0.005};
            p.A.Close(p.AId); p.Until(()=>p.A.ConnectionCount==0 && p.B.ConnectionCount==0,3);
            Check.Equal(2,p.Events.Count(e=>e.Kind==ConnectionEventKind.Disconnected));
        }
        private static void InvalidTraffic()
        {
            var p=new Pair(start:false);
            for(int i=0;i<200;i++)p.Network.Inject(p.APort.LocalAddress,p.BPort.LocalAddress,new byte[1200]);
            p.B.Poll(TimeSpan.Zero); Check.Equal(0,p.B.ConnectionCount);
            p.Start(); Check.Equal(1,p.B.ConnectionCount);
        }
        private static void Incompatible()
        {
            var net=new VirtualNetwork(); var ap=net.Create(15001);var bp=net.Create(15002);
            var a=new UdpNode(ap); var b=new UdpNode(bp,Single(UdpDelivery.ReliableUnordered)); b.Listen(); a.Connect(bp.LocalAddress);
            for(int i=0;i<2200;i++) {net.Now+=0.005;net.Deliver();a.Poll(TimeSpan.FromSeconds(net.Now));b.Poll(TimeSpan.FromSeconds(net.Now));}
            Check.Equal(0,a.ConnectionCount);Check.Equal(0,b.ConnectionCount);
            Check.True(a.TryGetEvent(out var e) && e!.Kind==ConnectionEventKind.Disconnected);
        }
    }
}
