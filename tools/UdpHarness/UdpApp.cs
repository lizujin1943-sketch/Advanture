using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FrameSync.Abstractions;
using FrameSync.Runtime;
using FrameSync.Samples.Counter;
using FrameSync.Sessions;
using FrameSync.Transport.Udp;
using ReliableUdp;

namespace FrameSync.Tools.UdpHarness
{
    /// <summary>阶段 E 的真实进程宿主：Socket、计时、命令行与示例玩法均留在工具，不进入同步核心。</summary>
    public static class UdpApp
    {
        /// <summary>运行 server 或 client；协议、状态不一致或超时均返回非零退出码。</summary>
        public static int Run(string[] args)
        {
            try
            {
                var o=Options.Parse(args);
                if(o.Help)
                {Console.WriteLine("UdpHarness server [--bind 127.0.0.1 --port 7777 --clients 2 --frames 40 --rate 20 --manual]");
                 Console.WriteLine("UdpHarness client [--host 127.0.0.1 --port 7777 --slot 0 --interactive]");
                 Console.WriteLine("通用：--report <json> --timeout <seconds>；server 可用 --ready <json> 输出实际绑定端口。");return 0;}
                return o.Server ? Server(o) : Client(o);
            }
            catch(Exception error){Console.Error.WriteLine("ERROR: "+error.Message);return 1;}
        }
        private static int Server(Options o)
        {
            using var port=new SocketDatagramPort(new DatagramAddress(IPAddress.Parse(o.Address),o.Port));
            var node=new UdpNode(port);node.Listen();var transport=new HostTransport(node);var clock=Stopwatch.StartNew();
            Console.WriteLine($"LISTEN {port.LocalAddress} pid={Environment.ProcessId} TrustedNetwork");
            if(o.Ready!=null)WriteJson(o.Ready,new{Port=port.LocalAddress.Port,ProcessId=Environment.ProcessId});
            var peers=new Dictionary<ushort,ulong>();var connected=new HashSet<ulong>();var welcomed=new HashSet<ulong>();var ready=new HashSet<ulong>();
            var results=new Dictionary<ulong,SendReceipt?>();var observed=new List<Observation>();Guid id=Guid.NewGuid();
            ServerSession<CounterState>? session=null;bool finishing=false;double closeStarted=0;
            try
            {
                while(clock.Elapsed.TotalSeconds<o.Timeout)
                {
                    node.Poll(clock.Elapsed);transport.Pump();
                    while(node.TryGetEvent(out var e))
                    {
                        if(e!.Kind==ConnectionEventKind.Connected)connected.Add(e.ConnectionId);
                        else if(!finishing)throw new InvalidOperationException("Peer disconnected before verified completion: "+e.Reason);
                    }
                    while(transport.Controls.Count>0)
                    {
                        var m=transport.Controls.Dequeue();ReadOnlySpan<byte> data=m.Payload;if(data.Length==0)throw new InvalidDataException("Empty host control");
                        if(data[0]==HostMessages.Join && data.Length==3 && session==null)
                        {
                            ushort slot=BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(1));
                            Require(connected.Contains(m.ConnectionId) && slot<o.Clients,"Invalid requested slot");
                            if(peers.TryGetValue(slot,out ulong existing))Require(existing==m.ConnectionId,"Slot already occupied");
                            else {Require(!peers.Values.Contains(m.ConnectionId),"Connection already owns a slot");peers.Add(slot,m.ConnectionId);}
                        }
                        else if(data[0]==HostMessages.WelcomeAck && data.Length==1 && welcomed.Contains(m.ConnectionId))ready.Add(m.ConnectionId);
                        else if(data[0]==HostMessages.Stop && data.Length==1 && o.Manual && peers.Values.Contains(m.ConnectionId))session?.Close();
                        else if(data[0]==HostMessages.Result && data.Length==81 && session?.Status==SessionStatus.Closed && peers.Values.Contains(m.ConnectionId))
                        {
                            Require(BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(1))==session.ConfirmedTick &&
                                BinaryPrimitives.ReadInt64LittleEndian(data.Slice(9))==session.InspectState().Total &&
                                Encoding.ASCII.GetString(data.Slice(17))==CheckpointCodec.Hash(session.CaptureCheckpoint()),"Client final result differs from server");
                            if(!results.ContainsKey(m.ConnectionId))results.Add(m.ConnectionId,null);
                        }
                        else throw new InvalidDataException("Unexpected host control");
                    }
                    if(session==null)
                    {
                        foreach(ulong peer in peers.Values)
                            if(!welcomed.Contains(peer) && node.TrySend(peer,2,HostMessages.WelcomeMessage(id,o.Clients,o.Frames,o.Rate,o.Manual),out _)==SendResult.Accepted)welcomed.Add(peer);
                        if(peers.Count==o.Clients && ready.Count==o.Clients)
                        {
                            var roster=peers.OrderBy(p=>p.Key).Select(p=>new Participant(p.Key,Guid.Parse("aaaaaaaa-0000-0000-0000-"+(p.Key+1).ToString("D12")))).ToArray();
                            session=new ServerSession<CounterState>(CounterGame.CreateModule(),new SessionDescriptor(id,1,(uint)o.Rate,roster),transport,
                                peers.Select(p=>new PeerBinding(p.Value,p.Key)).ToArray(),_=>new ByteString(CounterGame.Input(0)),options:new SessionOptions(maxFramesPerPoll:1));
                        }
                    }
                    if(session!=null && !finishing)
                    {
                        var frames=session.Poll(clock.Elapsed);
                        if(frames.Count>0)Observe("server",session.LastFrame!,session.InspectState().Total,observed);
                        Require(session.Status!=SessionStatus.Failed,"Server session failed: "+session.Failure);
                        if(session.ConfirmedTick>=(ulong)o.Frames)session.Close();
                        foreach(ulong peer in results.Keys.ToArray())
                            if(results[peer]==null && node.TrySend(peer,2,new[]{HostMessages.FinishAck},out var receipt)==SendResult.Accepted)results[peer]=receipt;
                        if(results.Count==o.Clients && results.Values.All(r=>r?.Status==ReceiptStatus.Acknowledged))
                        {
                            if(!o.Manual)Require(session.InspectState().Total==Expected(o.Frames) && observed.Count==o.Frames/5,"Automated inputs missed their deadline");
                            Report(o,"server",session.CaptureCheckpoint(),session.InspectState().Total,observed);
                            finishing=true;closeStarted=clock.Elapsed.TotalSeconds;foreach(ulong peer in peers.Values)node.Close(peer);
                        }
                    }
                    if(finishing && (node.ConnectionCount==0 || clock.Elapsed.TotalSeconds-closeStarted>=1.2))return 0;
                    Thread.Sleep(1);
                }
                throw new TimeoutException("Server deadline elapsed before all clients verified the final state");
            }
            finally{node.Shutdown();}
        }

        private static int Client(Options o)
        {
            IPAddress host=IPAddress.Parse(o.Address);
            using var port=new SocketDatagramPort(new DatagramAddress(host.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any,0));
            var node=new UdpNode(port);ulong peer=node.Connect(new DatagramAddress(host,o.Port));var transport=new HostTransport(node);var clock=Stopwatch.StartNew();
            bool connected=false,joined=false,welcomeAck=false,resultSent=false,finished=false,manual=false,stopRequested=false,stopSent=false;
            int clients=0,frames=0;ulong nextAuto=5,lastManual=0;ClientSession<CounterState>? session=null;var observed=new List<Observation>();
            var commands=new ConcurrentQueue<string>();
            if(o.Interactive)
            {
                // 阻塞式 ReadLine 独占后台任务；只有主线程能调用 node/session。输入队列也设置上限。
                _=Task.Run(()=>{string? line;while((line=Console.ReadLine())!=null){if(commands.Count<128)commands.Enqueue(line);}});
                Console.WriteLine("输入整数（例如 5 或 -2）提交操作；q 请求服务器结束。服务器须使用 --manual。");
            }
            try
            {
                while(clock.Elapsed.TotalSeconds<o.Timeout)
                {
                    node.Poll(clock.Elapsed);transport.Pump();
                    while(node.TryGetEvent(out var e))
                    {
                        if(e!.Kind==ConnectionEventKind.Connected)connected=true;
                        else {Require(finished,"Disconnected before verified completion: "+e.Reason);return 0;}
                    }
                    if(connected && !joined)joined=node.TrySend(peer,2,HostMessages.JoinMessage((ushort)o.Slot),out _)==SendResult.Accepted;
                    while(transport.Controls.Count>0)
                    {
                        var m=transport.Controls.Dequeue();ReadOnlySpan<byte> data=m.Payload;
                        Require(m.ConnectionId==peer && data.Length>0,"Unexpected host sender");
                        if(data[0]==HostMessages.Welcome && data.Length==27 && session==null)
                        {
                            Guid id=new Guid(data.Slice(1,16));clients=data[17];frames=BinaryPrimitives.ReadInt32LittleEndian(data.Slice(18));manual=data[26]==1;
                            Require(clients>=1 && clients<=4 && o.Slot<clients && frames>=1 && frames<=100000,"Invalid welcome limits");
                            Require(!o.Interactive || manual,"Interactive input requires server --manual");
                            session=new ClientSession<CounterState>(CounterGame.CreateModule(),id,1,transport,peer,options:new SessionOptions(maxFramesPerPoll:1));
                            Console.WriteLine($"client={(char)('A'+o.Slot)} pid={Environment.ProcessId} session={id}");
                        }
                        else if(data[0]==HostMessages.FinishAck && data.Length==1 && resultSent)
                        {if(!finished){Report(o,"client-"+o.Slot,session!.CaptureCheckpoint(),session.InspectState().Total,observed);finished=true;}}
                        else throw new InvalidDataException("Unexpected server host control");
                    }
                    if(session!=null)
                    {
                        if(!welcomeAck)welcomeAck=node.TrySend(peer,2,new[]{HostMessages.WelcomeAck},out _)==SendResult.Accepted;
                        var executed=session.Poll();if(executed.Count>0)Observe("client="+(char)('A'+o.Slot),session.LastFrame!,session.InspectState().Total,observed);
                        Require(session.Status!=SessionStatus.Failed,"Client session failed: "+session.Failure);
                        if(session.Status==SessionStatus.Running)
                        {
                            if(!manual)
                            {
                                // 演示自动输入提前最多 32 帧提交；这只是宿主调度，不是预测或修改已确认帧。
                                while(nextAuto<=(ulong)frames && nextAuto<=session.ConfirmedTick+32)
                                {
                                    int index=(int)(nextAuto/5),actor=(index-1)%clients;
                                    if(actor==o.Slot && !session.TrySubmit(nextAuto,CounterGame.Input(0,Amount(index))))break;
                                    nextAuto+=5;
                                }
                            }
                            else
                            {
                                while(commands.TryPeek(out string? command))
                                {
                                    if(command.Trim().Equals("q",StringComparison.OrdinalIgnoreCase)){commands.TryDequeue(out _);stopRequested=true;break;}
                                    if(!int.TryParse(command,NumberStyles.Integer,CultureInfo.InvariantCulture,out int amount) || amount< -1000 || amount>1000)
                                    {commands.TryDequeue(out _);Console.WriteLine("请输入 -1000..1000 的整数或 q。");continue;}
                                    ulong tick=Math.Max(session.ConfirmedTick+4,lastManual+1);
                                    if(tick>session.ConfirmedTick+128 || !session.TrySubmit(tick,CounterGame.Input(0,amount)))break;
                                    commands.TryDequeue(out _);lastManual=tick;Console.WriteLine($"submitted tick={tick} input={amount}");
                                }
                            }
                        }
                        if(stopRequested && !stopSent)stopSent=node.TrySend(peer,2,new[]{HostMessages.Stop},out _)==SendResult.Accepted;
                        if(session.Status==SessionStatus.Closed && !resultSent)
                            resultSent=node.TrySend(peer,2,HostMessages.ResultMessage(session.ConfirmedTick,session.InspectState().Total,CheckpointCodec.Hash(session.CaptureCheckpoint())),out _)==SendResult.Accepted;
                    }
                    Thread.Sleep(1);
                }
                throw new TimeoutException("Client deadline elapsed before verified shutdown");
            }
            finally{node.Shutdown();}
        }
        private static void Observe(string role,InputFrame frame,long total,List<Observation> observations)
        {
            foreach(var input in frame.Inputs)
                if(input.Origin==InputOrigin.Submitted)
                {
                    int amount=BinaryPrimitives.ReadInt32LittleEndian(input.Payload.Span.Slice(1));
                    observations.Add(new Observation(frame.Tick,input.Slot,amount));
                    if(observations.Count<=4 || observations.Count%100==0)
                        Console.WriteLine($"{role} received={(char)('A'+input.Slot)} tick={frame.Tick} input={amount} total={total}");
                }
        }
        private static int Amount(int index)=>index==1?5:index==2?3:index%7;
        private static long Expected(int frames){long total=0;for(int i=1;i<=frames/5;i++)total+=Amount(i);return total;}
        private static void Report(Options o,string role,Checkpoint checkpoint,long total,List<Observation> observed)
        {
            string hash=CheckpointCodec.Hash(checkpoint);
            if(o.Report!=null)WriteJson(o.Report,new{Role=role,ProcessId=Environment.ProcessId,Tick=checkpoint.Tick,Total=total,Hash=hash,Observed=observed});
            Console.WriteLine($"PASS {role} tick={checkpoint.Tick} total={total} hash={hash}");
        }
        private static void WriteJson(string path,object value)
        {
            string full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            string temporary=full+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,full,true);
        }
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        private sealed record Observation(ulong Tick,ushort Slot,int Amount);

        private sealed class Options
        {
            internal bool Server,Help,Manual,Interactive;
            internal string Address="127.0.0.1";
            internal int Port=7777,Clients=2,Frames=40,Rate=20,Slot;
            internal double Timeout=60;
            internal string? Ready,Report;
            internal static Options Parse(string[] args)
            {
                var o=new Options();if(args.Length==1 && args[0]=="--help"){o.Help=true;return o;}
                if(args.Length==0 || args[0]!="server" && args[0]!="client")throw new ArgumentException("First argument must be server or client; use --help");
                o.Server=args[0]=="server";var seen=new HashSet<string>();
                for(int i=1;i<args.Length;i++)
                {
                    string key=args[i];if(!seen.Add(key))throw new ArgumentException("Duplicate option: "+key);
                    if(key=="--manual"){Require(o.Server,"--manual requires server");o.Manual=true;continue;}
                    if(key=="--interactive"){Require(!o.Server,"--interactive requires client");o.Interactive=true;continue;}
                    if(++i==args.Length)throw new ArgumentException("Missing value: "+key);string value=args[i];
                    switch(key)
                    {
                        case "--bind":Require(o.Server,"--bind requires server");o.Address=value;break;
                        case "--host":Require(!o.Server,"--host requires client");o.Address=value;break;
                        case "--ready":Require(o.Server,"--ready requires server");o.Ready=value;break;
                        case "--report":o.Report=value;break;
                        case "--port":o.Port=int.Parse(value,CultureInfo.InvariantCulture);break;
                        case "--clients":Require(o.Server,"--clients requires server");o.Clients=int.Parse(value,CultureInfo.InvariantCulture);break;
                        case "--frames":Require(o.Server,"--frames requires server");o.Frames=int.Parse(value,CultureInfo.InvariantCulture);break;
                        case "--rate":Require(o.Server,"--rate requires server");o.Rate=int.Parse(value,CultureInfo.InvariantCulture);break;
                        case "--slot":Require(!o.Server,"--slot requires client");o.Slot=int.Parse(value,CultureInfo.InvariantCulture);break;
                        case "--timeout":o.Timeout=double.Parse(value,CultureInfo.InvariantCulture);break;
                        default:throw new ArgumentException("Unknown option: "+key);
                    }
                }
                Require(IPAddress.TryParse(o.Address,out _) && o.Port>=(o.Server?0:1) && o.Port<=65535 && o.Clients>=1 && o.Clients<=4 &&
                    o.Frames>=1 && o.Frames<=100000 && o.Rate>=1 && o.Rate<=200 && o.Slot>=0 && o.Slot<4 && double.IsFinite(o.Timeout) && o.Timeout>0 && o.Timeout<=86400,"Argument outside supported range");
                return o;
            }
        }
    }
}
