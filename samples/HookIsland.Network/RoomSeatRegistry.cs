using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Advanture.HookIsland.Network;

/// <summary>Room-local identity ownership. A failed first bootstrap frees its candidate; entered seats survive disconnection.</summary>
internal sealed class RoomSeatRegistry
{
    private readonly Guid roomId;
    private readonly Seat[] seats;
    internal RoomSeatRegistry(Guid roomId,int capacity)
    {this.roomId=roomId;seats=Enumerable.Range(0,capacity).Select(i=>new Seat((ushort)i)).ToArray();}
    internal IEnumerable<Seat> Connected=>seats.Where(s=>s.Connection.HasValue);
    internal Seat? Find(ulong connection)=>seats.FirstOrDefault(s=>s.Connection==connection);
    internal bool TryClaim(ulong connection,ushort requested,Guid room,ReadOnlySpan<byte> token,bool allowNew,out Seat? seat)
    {
        seat=Find(connection);
        if(seat!=null)
            return requested==seat.Slot && room==roomId && seat.Matches(token) ||
                token.Length==0 && room==Guid.Empty && (requested==seat.Slot || requested==ushort.MaxValue && seat.Slot>0);
        bool resume=token.Length>0;
        if(resume)
        {
            if(room!=roomId || requested>=seats.Length)return false;
            seat=seats[requested];
            if(!seat.Entered && !allowNew || seat.Connection.HasValue || !seat.Matches(token)){seat=null;return false;}
        }
        else
        {
            if(!allowNew || room!=Guid.Empty || requested!=0 && requested!=ushort.MaxValue)return false;
            seat=requested==0?seats[0]:seats.Skip(1).FirstOrDefault(s=>!s.Entered && !s.Connection.HasValue);
            if(seat==null || seat.Entered || seat.Connection.HasValue){seat=null;return false;}
            var secret=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(secret);seat.Token=secret;
        }
        seat.Connection=connection;return true;
    }
    internal void Release(ulong connection)
    {
        var seat=Find(connection);if(seat==null)return;
        seat.Connection=null;seat.Welcomed=seat.Ready=seat.Bound=false;
        // A failed first bootstrap leaves the seat free, but its proof may retry until a
        // fresh claim replaces it. This also covers activation confirmed only by the host.
    }
    internal sealed class Seat
    {
        internal Seat(ushort slot){Slot=slot;}
        internal ushort Slot { get; }
        internal ulong? Connection;
        internal bool Entered,Welcomed,Ready,Bound;
        internal byte[] Token=Array.Empty<byte>();
        internal bool Matches(ReadOnlySpan<byte> token)=>token.Length==32 && Token.Length==32 && CryptographicOperations.FixedTimeEquals(token,Token);
    }
}
