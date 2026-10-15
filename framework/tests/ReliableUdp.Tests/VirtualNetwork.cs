using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
namespace ReliableUdp.Tests
{
    // 仅测试工程持有故障策略。协议库不知道测试随机种子、丢包计划或模拟时钟。
    internal sealed class VirtualNetwork
    {
        private readonly Dictionary<DatagramAddress,Port> _ports=new Dictionary<DatagramAddress,Port>();
        private readonly List<(double Due,DatagramAddress To,Datagram Datagram)> _pending=new List<(double,DatagramAddress,Datagram)>();
        internal double Now;
        internal int LinkBytesPerSecond, MaxQueuedDatagrams=4096;
        private readonly Dictionary<DatagramAddress,double> _linkFreeAt=new Dictionary<DatagramAddress,double>();
        internal int Packets, LargestDatagram;
        internal Func<DatagramAddress,DatagramAddress,Packet?,int,double[]>? Schedule;
        internal Port Create(int port)
        { var p=new Port(this,new DatagramAddress(IPAddress.Loopback,port)); _ports.Add(p.LocalAddress,p); return p; }
        internal void Deliver()
        {
            var due=_pending.Where(x=>x.Due<=Now).OrderBy(x=>x.Due).ToArray();
            foreach(var d in due) { _pending.Remove(d); if(_ports.TryGetValue(d.To,out var p)) p.Queue.Enqueue(d.Datagram); }
        }
        internal void Inject(DatagramAddress source,DatagramAddress dest,byte[] bytes) => _ports[dest].Queue.Enqueue(new Datagram(source,bytes));
        internal sealed class Port : IDatagramPort
        {
            private readonly VirtualNetwork _network;
            internal readonly Queue<Datagram> Queue=new Queue<Datagram>();
            internal bool BlockSend;
            internal Port(VirtualNetwork network,DatagramAddress address) { _network=network; LocalAddress=address; }
            public DatagramAddress LocalAddress { get; }
            public bool TrySend(DatagramAddress destination,ReadOnlySpan<byte> bytes)
            {
                if(BlockSend) return false;
                _network.Packets++; _network.LargestDatagram=Math.Max(_network.LargestDatagram,bytes.Length);
                Packet.TryDecode(bytes,1200,out var packet);
                double[] delays=_network.Schedule?.Invoke(LocalAddress,destination,packet,_network.Packets) ?? new[] {0.005};
                foreach(double delay in delays)
                {
                    if(_network._pending.Count>=_network.MaxQueuedDatagrams) continue;
                    double start=_network.Now;
                    if(_network.LinkBytesPerSecond>0)
                    { _network._linkFreeAt.TryGetValue(LocalAddress,out double previous);start=Math.Max(start,previous)+bytes.Length/(double)_network.LinkBytesPerSecond;_network._linkFreeAt[LocalAddress]=start; }
                    _network._pending.Add((start+delay,destination,new Datagram(LocalAddress,bytes)));
                }
                return true;
            }
            public bool TryReceive(out Datagram? datagram) { datagram=Queue.Count==0 ? null : Queue.Dequeue(); return datagram!=null; }
        }
    }

    internal sealed class Pair
    {
        internal readonly VirtualNetwork Network=new VirtualNetwork();
        internal readonly VirtualNetwork.Port APort,BPort;
        internal readonly UdpNode A,B;
        internal ulong AId,BId;
        internal readonly List<ConnectionEvent> Events=new List<ConnectionEvent>();
        internal Pair(UdpOptions? options=null,bool start=true)
        {
            APort=Network.Create(14001); BPort=Network.Create(14002);
            A=new UdpNode(APort,options); B=new UdpNode(BPort,options); B.Listen(); AId=A.Connect(BPort.LocalAddress);
            if(start) Start();
        }
        internal void Step(double seconds=0.005)
        {
            Network.Now+=seconds; Network.Deliver(); A.Poll(TimeSpan.FromSeconds(Network.Now)); B.Poll(TimeSpan.FromSeconds(Network.Now));
            while(A.TryGetEvent(out var a)) Events.Add(a!);
            while(B.TryGetEvent(out var b)) { Events.Add(b!); if(b!.Kind==ConnectionEventKind.Connected) BId=b.ConnectionId; }
        }
        internal void Start() { Until(()=>BId!=0 && Events.Count(x=>x.Kind==ConnectionEventKind.Connected)==2,12,"Handshake did not complete"); }
        internal void Until(Func<bool> done,double seconds=20,string message="Timed out")
        { double end=Network.Now+seconds; bool completed=done(); while(!completed && Network.Now<end) { Step(); completed=done(); } Check.True(completed,message); }
        internal List<byte[]> ReceiveB()
        { var l=new List<byte[]>(); while(B.TryReceive(out var m)) l.Add(m!.Payload.ToArray()); return l; }
    }
}
