using System;
using System.Collections.Generic;
using System.Linq;
namespace Advanture.Tactical.Tests;
internal static class Program
{
    static int Main(string[] args)
    {
        var tests = SimulationTests.Cases().Concat(WeaponTests.Cases()).Concat(NetworkTests.Cases()).Concat(RoomProtocolTests.Cases()).Where(t => args.Length == 0 || t.Name.StartsWith(args[0], StringComparison.Ordinal)).ToList();
        int failed=0;
        foreach(var test in tests)
        {
            if(args.Length>0 && !test.Name.StartsWith(args[0],StringComparison.Ordinal)) continue;
            try { test.Run(); Console.WriteLine("PASS "+test.Name); }
            catch(Exception e) { failed++; Console.WriteLine("FAIL "+test.Name+": "+e); }
        }
        Console.WriteLine($"Tactical tests: {tests.Count-failed} passed, {failed} failed.");
        return failed==0?0:1;
    }
}
internal static class Check
{
    internal static void True(bool value,string message="Expected true") { if(!value)throw new Exception(message); }
    internal static void Equal<T>(T expected,T actual) { if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"Expected {expected}; actual {actual}"); }
    internal static void Throws(Action action) { try{action();}catch(ArgumentException){return;}catch(System.IO.InvalidDataException){return;}throw new Exception("Expected invalid input to be rejected"); }
}
