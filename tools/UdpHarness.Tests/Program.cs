using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FrameSync.Tools.UdpHarness;
using FrameSync.Transport;
using FrameSync.Transport.Udp;
using ReliableUdp;
using System.Net;

namespace FrameSync.Tools.UdpHarnessTests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var cases=new (string Name,Action Run)[] {
                ("Socket.RealDatagrams", Socket), ("Adapter.CapabilitiesRejectWrongProfile", Profile),
                ("Cli.RejectsUnknownArgument", ()=>Require(UdpApp.Run(new[]{"--invalid"})==1,"Unknown argument accepted")),
                ("Processes.TwoClients", ()=>Processes(2,40,20)), ("Processes.FourClients", ()=>Processes(4,100,50)), ("Processes.InteractiveInput", ()=>Processes(2,40,20,true)), ("Processes.IdleNeutralInputs", ()=>Processes(2,20,20,true,true)), ("Processes.SaveReloadPrediction", SaveReload), ("Processes.DisconnectContinues", DisconnectContinues) };
            int passed=0,failed=0;
            foreach(var test in cases.Concat(InputBufferTests.Cases()).Where(t=>args.Length==0 || t.Name.StartsWith(args[0],StringComparison.Ordinal)))
            { try {test.Run();Console.WriteLine("PASS "+test.Name);passed++;} catch(Exception e){Console.WriteLine("FAIL "+test.Name+": "+e);failed++;} }
            Console.WriteLine($"UDP integration tests: {passed} passed, {failed} failed.");return failed==0 && passed>0 ? 0 : 1;
        }
        private static void Socket()
        {
            using var a=new SocketDatagramPort(new DatagramAddress(IPAddress.Loopback,0));
            using var b=new SocketDatagramPort(new DatagramAddress(IPAddress.Loopback,0));
            Require(!a.TryReceive(out _),"Receive must not block");
            Require(a.TrySend(b.LocalAddress,new byte[]{1,2,3}),"Send failed");
            Datagram? result=null; var time=Stopwatch.StartNew();
            while(!b.TryReceive(out result) && time.ElapsedMilliseconds<1000)Thread.Sleep(1);
            Require(result!=null && result.Source.Equals(a.LocalAddress) && result.Bytes.SequenceEqual(new byte[]{1,2,3}),"Datagram differs");
        }
        private static void Profile()
        {
            using var p=new SocketDatagramPort(new DatagramAddress(IPAddress.Loopback,0));
            var node=new UdpNode(p);var adapter=new UdpTransport(node);
            Require(adapter.MaxMessageBytes>=16429 && adapter.ChannelCount==4 && adapter.Supports(DeliveryMode.ReliableOrdered),"Stage B capabilities differ");
            try {new UdpTransport(new UdpNode(p,new UdpOptions(new[]{new ChannelOptions(UdpDelivery.UnreliableUnordered,900)})));}
            catch(ArgumentException){return;} throw new Exception("Wrong profile accepted");
        }
        private static void Processes(int clients,int frames,int rate,bool manual=false,bool idle=false)
        {
            string directory=Path.GetFullPath($"artifacts/stage-e/processes-{clients}"+(idle?"-idle":manual?"-manual":""));Directory.CreateDirectory(directory);
            string ready=Path.Combine(directory,"ready.json");File.Delete(ready);
            var children=new List<Child>();
            try
            {
                var serverArgs=new List<string>{"server","--port","0","--clients",clients.ToString(),"--frames",frames.ToString(),"--rate",rate.ToString(),"--ready",ready,"--save",Path.Combine(directory,"world.fssv")};
                if(manual)serverArgs.Add("--manual");children.Add(Start(directory,"server",serverArgs.ToArray()));
                var wait=Stopwatch.StartNew();
                while(!File.Exists(ready) && !children[0].Process.HasExited && wait.ElapsedMilliseconds<10000)Thread.Sleep(10);
                Require(File.Exists(ready),"Server did not publish a bound port; inspect server.log");
                using var info=JsonDocument.Parse(File.ReadAllText(ready));string port=info.RootElement.GetProperty("Port").GetInt32().ToString();
                for(int i=0;i<clients;i++)
                {var clientArgs=new List<string>{"client","--port",port,"--slot",i.ToString()};if(manual && i==0)clientArgs.Add("--interactive");children.Add(Start(directory,"client-"+i,clientArgs.ToArray()));}
                if(manual && !idle){children[1].Process.StandardInput.WriteLine("5");children[1].Process.StandardInput.Flush();}
                foreach(var child in children)
                {
                    Require(child.Process.WaitForExit(45000),child.Name+" process timed out");
                    child.WriteLog();Require(child.Process.ExitCode==0,child.Name+" failed; inspect "+child.Name+".log");
                }
                var results=children.Select(c=>JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,c.Name+".json")))).ToArray();
                try
                {
                    string hash=results[0].RootElement.GetProperty("Hash").GetString()!;
                    long expected=0;for(int i=1;i<=frames/5;i++)expected+=i==1 ? 5 : i==2 ? 3 : i%7;if(manual)expected=idle?0:5;
                    foreach(var r in results)
                    {
                        var item=r.RootElement;
                        Require(item.GetProperty("Tick").GetInt32()==frames,"Final ticks differ");
                        Require(item.GetProperty("Total").GetInt64()==expected,"Hand-calculated total differs");
                        Require(item.GetProperty("Hash").GetString()==hash,"Complete checkpoint hashes differ");
                        if(item.GetProperty("Role").GetString()!="server")
                        {
                            Require(item.GetProperty("FrozenInputs").GetUInt64()==(ulong)frames, "Client did not freeze all local frames");
                            if(idle)Require(item.GetProperty("FrozenNeutralInputs").GetUInt64()==(ulong)frames, "Idle client omitted neutral samples");
                        }
                        Require(item.GetProperty("Observed").GetArrayLength()==(manual?(idle?0:1):frames/5-frames/35),"Some submitted input was not observed");
                        if(!idle){var first=item.GetProperty("Observed")[0];Require(first.GetProperty("Slot").GetInt32()==0 && first.GetProperty("Amount").GetInt32()==5,"A's input not received");}
                    }
                    if(idle)
                    {
                        // 验证真实进程确实发送了无操作帧，不能仅因服务器补零也得到相同游戏总数就算成功。
                        var save = new FrameSync.Persistence.SaveService(new FrameSync.Storage.FileSystem.FileSaveStore(Path.Combine(directory,"world.fssv"))).Load();
                        Require(save.Checkpoint.MissingInputCounts.All(n=>n==0), "Idle clients did not explicitly submit neutral input");
                        Require(save.Checkpoint.InputHistory.TakeLast(10).All(f=>f.Inputs.Count==clients && f.Inputs.All(i=>i.Origin==FrameSync.Abstractions.InputOrigin.Submitted)), "Idle input was substituted instead of submitted");
                    }
                    Require(results.Select(r=>r.RootElement.GetProperty("ProcessId").GetInt32()).Distinct().Count()==clients+1,"Not separate OS processes");
                    Console.WriteLine($"PROOF server + {clients} client processes: tick={frames} total={expected} hash={hash}");
                }
                finally {foreach(var r in results)r.Dispose();}
            }
            finally
            {
                foreach(var child in children) {if(!child.Process.HasExited){child.Process.Kill(entireProcessTree:true);child.Process.WaitForExit();}child.WriteLog();child.Process.Dispose();}
            }
        }
        // 两轮分别创建三个新进程：第二轮只从第一轮正式存档恢复，不能借用旧进程内存。
        private static void SaveReload()
        {
            string root=Path.GetFullPath("artifacts/initial-core/save-reload"); Directory.CreateDirectory(root);
            string save=Path.Combine(root,"world.fssv"); if(File.Exists(save))File.Delete(save);
            string? oldSession=null; long expectedTotal=0;
            for(int round=0;round<2;round++)
            {
                string directory=Path.Combine(root,"round-"+round);Directory.CreateDirectory(directory);
                string ready=Path.Combine(directory,"ready.json");File.Delete(ready);var children=new List<Child>();
                try
                {
                    var args=new List<string>{"server","--port","0","--clients","2","--frames","20","--rate","20","--ready",ready,"--save",save};
                    if(round==1){args.Add("--load");args.Add(save);}
                    children.Add(Start(directory,"server",args.ToArray()));
                    var time=Stopwatch.StartNew();
                    while(!File.Exists(ready) && !children[0].Process.HasExited && time.ElapsedMilliseconds<10000)Thread.Sleep(10);
                    Require(File.Exists(ready),"Reload server did not start");
                    using var info=JsonDocument.Parse(File.ReadAllText(ready));string port=info.RootElement.GetProperty("Port").GetInt32().ToString();
                    for(int slot=0;slot<2;slot++)children.Add(Start(directory,"client-"+slot,"client","--port",port,"--slot",slot.ToString()));
                    foreach(var child in children){Require(child.Process.WaitForExit(30000),"Reload process timed out");child.WriteLog();Require(child.Process.ExitCode==0,"Reload process failed: "+child.Name);}
                    Require(File.Exists(save),"Normal shutdown did not create formal save");
                    expectedTotal+=15; // 第5/10/15/20帧分别 +5/+3/+3/+4。
                    using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"server.json")));
                    string session=report.RootElement.GetProperty("SessionId").GetString()!;
                    Require(session!=oldSession,"Reload reused old session identity");oldSession=session;
                    string hash=report.RootElement.GetProperty("Hash").GetString()!;
                    Require(report.RootElement.GetProperty("PersistentTick").GetUInt64()==(ulong)((round+1)*20),"Persistent time was reset");
                    Require(report.RootElement.GetProperty("Total").GetInt64()==expectedTotal,"Saved world progress was not continued");
                    for(int slot=0;slot<2;slot++)
                    {
                        using var client=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"client-"+slot+".json")));
                        Require(client.RootElement.GetProperty("Hash").GetString()==hash,"Reloaded client hash differs");
                        Require(client.RootElement.GetProperty("Rollbacks").GetUInt64()>0,"Prediction was not exercised");
                    }
                }
                finally
                {
                    foreach(var child in children){if(!child.Process.HasExited){child.Process.Kill(true);child.Process.WaitForExit();}child.WriteLog();child.Process.Dispose();}
                }
            }
        }
        private static void DisconnectContinues()
        {
            string directory=Path.GetFullPath("artifacts/initial-core/disconnect");Directory.CreateDirectory(directory);
            string ready=Path.Combine(directory,"ready.json"),save=Path.Combine(directory,"world.fssv");File.Delete(ready);
            var children=new List<Child>();
            try
            {
                children.Add(Start(directory,"server","server","--port","0","--clients","2","--frames","40","--rate","20","--ready",ready,"--save",save));
                var time=Stopwatch.StartNew();
                while(!File.Exists(ready) && !children[0].Process.HasExited && time.ElapsedMilliseconds<10000)Thread.Sleep(10);
                Require(File.Exists(ready),"Disconnect server did not start");
                using var info=JsonDocument.Parse(File.ReadAllText(ready));string port=info.RootElement.GetProperty("Port").GetInt32().ToString();
                children.Add(Start(directory,"client-0","client","--port",port,"--slot","0"));
                children.Add(Start(directory,"client-1","client","--port",port,"--slot","1","--leave-at","10"));
                foreach(var child in children){Require(child.Process.WaitForExit(30000),"Disconnect process timed out");child.WriteLog();Require(child.Process.ExitCode==0,"Disconnect process failed: "+child.Name);}
                using var server=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"server.json")));
                using var client=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"client-0.json")));
                Require(server.RootElement.GetProperty("Tick").GetInt32()==40,"Remaining participant did not continue");
                Require(server.RootElement.GetProperty("Hash").GetString()==client.RootElement.GetProperty("Hash").GetString(),"Remaining participant differs");
                var saved=new FrameSync.Persistence.SaveService(new FrameSync.Storage.FileSystem.FileSaveStore(save)).Load();
                Require(!saved.Checkpoint.Membership.Find(1).IsActive && saved.Checkpoint.Membership.Find(0).IsActive,"Disconnect was not saved as a member event");
            }
            finally
            {
                foreach(var child in children){if(!child.Process.HasExited){child.Process.Kill(true);child.Process.WaitForExit();}child.WriteLog();child.Process.Dispose();}
            }
        }
        private static Child Start(string directory,string name,params string[] args)
        {
            var info=new ProcessStartInfo("dotnet") {RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=args.Contains("--interactive"),UseShellExecute=false};
            info.ArgumentList.Add(typeof(UdpApp).Assembly.Location);foreach(string arg in args)info.ArgumentList.Add(arg);
            info.ArgumentList.Add("--report");info.ArgumentList.Add(Path.Combine(directory,name+".json"));
            var process=Process.Start(info) ?? throw new Exception("Cannot start child");return new Child(process,name,Path.Combine(directory,name+".log"));
        }
        private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        private sealed class Child
        {
            internal readonly Process Process;internal readonly string Name;private readonly string _path;private readonly Task<string> _out,_error;
            internal Child(Process process,string name,string path){Process=process;Name=name;_path=path;_out=process.StandardOutput.ReadToEndAsync();_error=process.StandardError.ReadToEndAsync();}
            internal void WriteLog(){if(Process.HasExited)File.WriteAllText(_path,_out.GetAwaiter().GetResult()+_error.GetAwaiter().GetResult());}
        }
    }
}
