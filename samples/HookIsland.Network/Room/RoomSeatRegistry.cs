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
    internal bool TryClaim(ulong connection,ushort requested,Guid room,ReadOnlySpan<byte> token,bool allowNew,out Seat? seat,ReadOnlySpan<byte> profile=default)
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
            if(room!=Guid.Empty || requested!=0 && requested!=ushort.MaxValue)return false;
            var proof=profile.ToArray();
            seat=seats.FirstOrDefault(s=>s.OwnedBy(proof));
            if(seat!=null && (requested==0)!=(seat.Slot==0)){seat=null;return false;}
            if(seat==null && allowNew)seat=requested==0?seats[0]:seats.Skip(1).FirstOrDefault(s=>!s.Entered && !s.Connection.HasValue);
            if(seat==null || seat.Connection.HasValue || seat.Entered && !seat.OwnedBy(profile) || !seat.Entered && !allowNew){seat=null;return false;}
            seat.Token=IslandPlayerIdentity.Create();
            if(!seat.Entered)seat.Profile=profile.Length==32?profile.ToArray():IslandPlayerIdentity.Create();
        }
        seat.Connection=connection;return true;
    }
    internal byte[][] CaptureProfiles()=>seats.Select(s=>s.Entered?(byte[])s.Profile.Clone():Array.Empty<byte>()).ToArray();
    internal bool[] CaptureEntered()=>seats.Select(s=>s.Entered).ToArray();
    internal void Restore(IslandSave save,byte[]? ownerKey)
    {
        var profiles=save.Profiles;
        for(int i=0;i<seats.Length;i++)
        {seats[i].Entered=save.Entered[i];seats[i].Profile=profiles[i];seats[i].Token=IslandPlayerIdentity.Create();}
        if(ownerKey!=null)
        {if(ownerKey.Length!=32)throw new ArgumentException("Invalid owner identity.",nameof(ownerKey));seats[0].Profile=(byte[])ownerKey.Clone();}
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
        internal byte[] Profile=Array.Empty<byte>();
        internal bool OwnedBy(ReadOnlySpan<byte> proof)=>proof.Length==32 && Profile.Length==32 && CryptographicOperations.FixedTimeEquals(proof,Profile);
        internal bool Matches(ReadOnlySpan<byte> token)=>token.Length==32 && Token.Length==32 && CryptographicOperations.FixedTimeEquals(token,Token);
    }
}
