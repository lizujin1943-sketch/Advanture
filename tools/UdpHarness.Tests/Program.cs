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
                ("Processes.TwoClients", ()=>Processes(2,40,20)), ("Processes.FourClients", ()=>Processes(4,100,50)), ("Processes.InteractiveInput", ()=>Processes(2,40,20,true)) };
            int passed=0,failed=0;
            foreach(var test in cases.Where(t=>args.Length==0 || t.Name.StartsWith(args[0],StringComparison.Ordinal)))
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
        private static void Processes(int clients,int frames,int rate,bool manual=false)
        {
            string directory=Path.GetFullPath($"artifacts/stage-e/processes-{clients}"+(manual?"-manual":""));Directory.CreateDirectory(directory);
            string ready=Path.Combine(directory,"ready.json");File.Delete(ready);
            var children=new List<Child>();
            try
            {
                var serverArgs=new List<string>{"server","--port","0","--clients",clients.ToString(),"--frames",frames.ToString(),"--rate",rate.ToString(),"--ready",ready};
                if(manual)serverArgs.Add("--manual");children.Add(Start(directory,"server",serverArgs.ToArray()));
                var wait=Stopwatch.StartNew();
                while(!File.Exists(ready) && !children[0].Process.HasExited && wait.ElapsedMilliseconds<10000)Thread.Sleep(10);
                Require(File.Exists(ready),"Server did not publish a bound port; inspect server.log");
                using var info=JsonDocument.Parse(File.ReadAllText(ready));string port=info.RootElement.GetProperty("Port").GetInt32().ToString();
                for(int i=0;i<clients;i++)
                {var clientArgs=new List<string>{"client","--port",port,"--slot",i.ToString()};if(manual && i==0)clientArgs.Add("--interactive");children.Add(Start(directory,"client-"+i,clientArgs.ToArray()));}
                if(manual){children[1].Process.StandardInput.WriteLine("5");children[1].Process.StandardInput.Flush();}
                foreach(var child in children)
                {
                    Require(child.Process.WaitForExit(45000),child.Name+" process timed out");
                    child.WriteLog();Require(child.Process.ExitCode==0,child.Name+" failed; inspect "+child.Name+".log");
                }
                var results=children.Select(c=>JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,c.Name+".json")))).ToArray();
                try
                {
                    string hash=results[0].RootElement.GetProperty("Hash").GetString()!;
                    long expected=0;for(int i=1;i<=frames/5;i++)expected+=i==1 ? 5 : i==2 ? 3 : i%7;if(manual)expected=5;
                    foreach(var r in results)
                    {
                        var item=r.RootElement;
                        Require(item.GetProperty("Tick").GetInt32()==frames,"Final ticks differ");
                        Require(item.GetProperty("Total").GetInt64()==expected,"Hand-calculated total differs");
                        Require(item.GetProperty("Hash").GetString()==hash,"Complete checkpoint hashes differ");
                        Require(item.GetProperty("Observed").GetArrayLength()==(manual?1:frames/5),"Some submitted input was not observed");
                        var first=item.GetProperty("Observed")[0];Require(first.GetProperty("Slot").GetInt32()==0 && first.GetProperty("Amount").GetInt32()==5,"A's input not received");
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
