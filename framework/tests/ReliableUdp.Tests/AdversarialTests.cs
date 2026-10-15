using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
namespace ReliableUdp.Tests
{
    // 在真实协议已建立连接后注入经过 CRC 的矛盾报文，验证拒绝发生在 ACK/内存提交之前。
    internal static class AdversarialTests
    {
        internal static IEnumerable<(string,Action)> Cases()
        {
            yield return ("Boundary.FutureAckClosesWithoutCompletingReceipt", FutureAck);
            yield return ("Boundary.FragmentConflictDoesNotCompleteMessage", FragmentConflict);
            yield return ("Boundary.OverlappingMessageRangesFail", Overlap);
            yield return ("Boundary.DuplicateFragmentsDoNotExtendProgressTimeout", NoProgress);
            yield return ("Boundary.SlidingWindowAndAckHistoryRemainBounded", History);
            yield return ("Boundary.HelloCannotAmplifyOrAllocate", HelloBudget);
            yield return ("Boundary.LateAckOfLostCopyCompletesReceipt", LateAck);
            yield return ("Boundary.TotalLossShrinksWindowAndBacksOff", Congestion);
            yield return ("Network.RttLossBandwidthMatrix", NetworkMatrix);
        }
        private static void NetworkMatrix()
        {
            foreach(int rtt in new[]{60,120,200}) foreach(int loss in new[]{1,5,10})
            {
                var p=new Pair();var random=new Random(300+rtt+loss);p.Network.LinkBytesPerSecond=32768;
                p.Network.Schedule=(a,b,k,n)=>random.Next(100)<loss ? Array.Empty<double>() : new[]{(rtt/2.0+random.Next(20))/1000};
                int sent=0,received=0;var receipts=new List<SendReceipt>();double started=p.Network.Now;
                p.Until(()=>{
                    // 中途把链路带宽减半，验证仍能完成且拥塞账本不会越界。
                    if(p.Network.Now-started>0.5)p.Network.LinkBytesPerSecond=16384;
                    while(sent<24)
                    {
                        var bytes=Enumerable.Repeat((byte)sent,1500).ToArray();
                        if(p.A.TrySend(p.AId,0,bytes,out var receipt)!=SendResult.Accepted)break;
                        sent++;receipts.Add(receipt!);
                    }
                    foreach(var bytes in p.ReceiveB()){Check.True(bytes.All(x=>x==(byte)received),"Matrix order/content differs");received++;}
                    Check.True(p.A.GetStatistics(p.AId).InFlightPackets<=64);
                    return received==24 && receipts.All(x=>x.Status==ReceiptStatus.Acknowledged);
                },60,$"RTT={rtt} loss={loss}% failed");
                Console.WriteLine($"NETWORK rtt={rtt}ms loss={loss}% bandwidth=32->16KiB/s simulated={(p.Network.Now-started):F2}s retries={p.A.GetStatistics(p.AId).Retransmissions}");
            }
        }
        private static void FutureAck()
        {
            var p=new Pair();Packet? sent=null;
            p.Network.Schedule=(a,b,k,n)=>{if(a.Equals(p.APort.LocalAddress) && k?.Kind==Kind.Fragment){sent=k;return Array.Empty<double>();}return new[]{0.005};};
            p.A.TrySend(p.AId,0,new byte[]{3},out var r);p.Until(()=>sent!=null);
            p.Network.Inject(p.BPort.LocalAddress,p.APort.LocalAddress,new Packet {Connection=sent!.Connection,Number=100,Ack=ulong.MaxValue}.Encode());
            p.Step();Check.Equal(ReceiptStatus.Failed,r!.Status);p.Until(()=>p.A.ConnectionCount==0,2);
        }
        private static void FragmentConflict()
        {
            var p=new Pair();Packet? first=null;
            p.Network.Schedule=(a,b,k,n)=>{
                if(a.Equals(p.APort.LocalAddress) && k?.Kind==Kind.Fragment){if(first==null){first=k;return new[]{0.005};}return Array.Empty<double>();}return new[]{0.005};};
            p.A.TrySend(p.AId,0,new byte[1800],out _);p.Until(()=>first!=null);for(int i=0;i<5;i++)p.Step();
            var bad=first!;bad.Number+=100;bad.Body[32]=99;p.Network.Inject(p.APort.LocalAddress,p.BPort.LocalAddress,bad.Encode());
            p.Step();Check.Equal(0,p.ReceiveB().Count);p.Until(()=>p.B.ConnectionCount==0,2);
        }
        private static void Overlap()
        {
            var p=new Pair();Packet? first=null;
            p.Network.Schedule=(a,b,k,n)=>{if(a.Equals(p.APort.LocalAddress) && k?.Kind==Kind.Fragment)first=k;return new[]{0.005};};
            p.A.TrySend(p.AId,0,new byte[]{1},out var r);p.Until(()=>r!.Status==ReceiptStatus.Acknowledged);p.ReceiveB();
            var bad=first!;bad.Number+=100;BinaryPrimitives.WriteUInt64LittleEndian(bad.Body,2);BinaryPrimitives.WriteUInt64LittleEndian(bad.Body.AsSpan(8),2);
            // 第二条消息沿用第一条的字节区间 0，不能被当作合法的新消息。
            p.Network.Inject(p.APort.LocalAddress,p.BPort.LocalAddress,bad.Encode());p.Step();Check.Equal(0,p.ReceiveB().Count);p.Until(()=>p.B.ConnectionCount==0,2);
        }
        private static void NoProgress()
        {
            var p=new Pair(new UdpOptions(progressTimeout:TimeSpan.FromSeconds(2)));Packet? first=null;
            p.Network.Schedule=(a,b,k,n)=>{
                if(a.Equals(p.APort.LocalAddress) && k?.Kind==Kind.Fragment){if(first==null){first=k;return new[]{0.005};}return Array.Empty<double>();}return new[]{0.005};};
            p.A.TrySend(p.AId,0,new byte[1800],out _);p.Until(()=>first!=null);
            for(int i=0;i<700 && p.B.ConnectionCount>0;i++)
            {if(i%50==0){first!.Number+=100;p.Network.Inject(p.APort.LocalAddress,p.BPort.LocalAddress,first.Encode());}p.Step();}
            Check.Equal(0,p.B.ConnectionCount);
        }
        private static void History()
        {
            var p=new Pair();int sent=0,received=0;SendReceipt? last=null;var receipts=new List<SendReceipt>();
            p.Until(()=>{
                while(sent<700 && p.A.TrySend(p.AId,1,new byte[]{(byte)sent},out last)==SendResult.Accepted){sent++;receipts.Add(last!);}
                foreach(var data in p.ReceiveB()){Check.Equal((byte)received,data[0]);received++;}
                var stat=p.A.GetStatistics(p.AId);Check.True(stat.InFlightPackets<=64 && stat.InFlightBytes>=0 && stat.PendingSendBytes<=1048576);
                return received==700 && receipts.All(x=>x.Status==ReceiptStatus.Acknowledged);
            },30);
            Check.Equal(0,p.B.GetStatistics(p.BId).BufferedReceiveBytes);
        }
        private static void HelloBudget()
        {
            var net=new VirtualNetwork();var ap=net.Create(18001);var bp=net.Create(18002);var options=new UdpOptions();var b=new UdpNode(bp,options);b.Listen();
            var bytes=new byte[108];new Random(1).NextBytes(bytes);options.Fingerprint().CopyTo(bytes,16);
            int count=0;net.Schedule=(a,d,p,n)=>{count++;Check.True(p!.Encode().Length<=192,"Handshake amplification");return Array.Empty<double>();};
            for(int i=0;i<1000;i++)net.Inject(ap.LocalAddress,bp.LocalAddress,new Packet {Kind=Kind.Hello,Body=bytes}.Encode());
            b.Poll(TimeSpan.Zero);Check.Equal(0,b.ConnectionCount);Check.True(count>0 && count<=16,"Control response budget unbounded");
        }
        private static void LateAck()
        {
            var p=new Pair();bool first=true;Packet? original=null;
            p.Network.Schedule=(a,b,k,n)=>{
                if(a.Equals(p.APort.LocalAddress) && k?.Kind==Kind.Fragment){if(first){first=false;original=k;return new[]{0.005};}return Array.Empty<double>();}
                if(a.Equals(p.BPort.LocalAddress))return new[]{1.2};return new[]{0.005};};
            p.A.TrySend(p.AId,0,new byte[]{4},out var receipt);p.Until(()=>p.A.GetStatistics(p.AId).Retransmissions>0,2);
            // 所有新副本都丢失，原副本的迟到确认仍应完成消息，不必等另一个副本到达。
            p.Until(()=>receipt!.Status==ReceiptStatus.Acknowledged,2,"Lost packet's late ACK was forgotten");
            Check.True(original!=null);Check.Equal(1,p.ReceiveB().Count);
        }
        private static void Congestion()
        {
            var p=new Pair();p.Network.Schedule=(a,b,k,n)=>Array.Empty<double>();
            p.A.TrySend(p.AId,0,new byte[64000],out _);for(int i=0;i<240;i++)p.Step();
            var stat=p.A.GetStatistics(p.AId);Check.Equal(1200,stat.CongestionWindowBytes);Check.True(stat.RetransmitTimeout.TotalSeconds>=2 && stat.InFlightPackets<=64);
            Check.True(stat.PendingSendBytes==64000,"Unacknowledged reliable bytes were discarded");
        }
    }
}
