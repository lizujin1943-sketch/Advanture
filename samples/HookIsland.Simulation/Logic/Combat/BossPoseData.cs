using System;
using System.IO;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Advanture.HookIsland.Simulation;

/// <summary>来自已批准模型的 30 Hz 毫米骨链表。离线采样，每个客户端使用同一资源哈希。</summary>
internal static class BossPoseData
{
    internal const string Hash="196c6e6777b07c032069288d13d9abdc0829b3fcc80e10ca8ed216d1ba92279a";
    private readonly struct Clip
    {
        internal Clip(int limbs,int body,int frames){Limbs=limbs;Body=body;Frames=frames;}
        internal int Limbs {get;} internal int Body {get;} internal int Frames {get;}
    }
    private static readonly Clip[] Clips={
        new Clip(0,5822208,121),
        new Clip(294272,5827048,181),
        new Clip(734464,5834288,91),
        new Clip(955776,5837928,109),
        new Clip(1220864,5842288,91),
        new Clip(1442176,5845928,109),
        new Clip(1707264,5850288,91),
        new Clip(1928576,5853928,109),
        new Clip(2193664,5858288,91),
        new Clip(2414976,5861928,109),
        new Clip(2680064,5866288,97),
        new Clip(2915968,5870168,31),
        new Clip(2991360,5871408,91),
        new Clip(3212672,5875048,151),
        new Clip(3579904,5881088,79),
        new Clip(3772032,5884248,79),
        new Clip(3964160,5887408,79),
        new Clip(4156288,5890568,79),
        new Clip(4348416,5893728,79),
        new Clip(4540544,5896888,79),
        new Clip(4732672,5900048,79),
        new Clip(4924800,5903208,79),
        new Clip(5116928,5906368,181),
        new Clip(5557120,5913608,109),
    };
    private static readonly int[] Offsets={0,25,50,75,100,113,126,139};
    private static readonly byte[] Data=Load();
    private static byte[] Load()
    {
        using var source=typeof(BossPoseData).Assembly.GetManifestResourceStream("Advanture.HookIsland.Simulation.Data.BossCollisions.bin.gz")??throw new InvalidDataException("Missing boss collision data.");
        using var gzip=new GZipStream(source,CompressionMode.Decompress);using var result=new MemoryStream();gzip.CopyTo(result);
        if(result.Length!=5917968)throw new InvalidDataException("Invalid boss collision table.");
        var bytes=result.ToArray();using var sha=SHA256.Create();
        if(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()!=Hash)throw new InvalidDataException("Boss collision hash mismatch.");return bytes;
    }
    private static int Read(int offset)=>BinaryPrimitives.ReadInt32LittleEndian(Data.AsSpan(offset,4));
    internal static int MainClip(BossState b)=>b.Phase==BossPhase.Emerge?0:b.Phase==BossPhase.Enrage?12:b.Phase==BossPhase.Dying || b.Phase==BossPhase.Defeated?22:b.Enraged?13:1;
    internal static int Frame(int clip,int tick)=>clip==1 || clip==13?tick%(Clips[clip].Frames-1):Math.Min(tick,Clips[clip].Frames-1);
    internal static BossProxy Head(BossState b,bool eye)
    {
        int clip=b.InkWindupTicks>0?10:MainClip(b),tick=b.InkWindupTicks>0?b.InkWindupTicks:b.PhaseTicks;
        if(b.TownArena && b.InkWindupTicks>0)tick=tick<=60?tick*36/60:36+(tick-60);
        int start=Clips[clip].Body+Frame(clip,tick)*40;
        if(eye){int radius=Read(start+36);return new BossProxy(-1,b.CenterX+Read(start+24),Read(start+28),b.CenterZ+Read(start+32),radius,radius,radius,true);}
        int x=Read(start),y=Read(start+4),z=Read(start+8),xx=Read(start+12),yy=Read(start+16),zz=Read(start+20);
        return new BossProxy(-1,b.CenterX+(x+xx)/2,(y+yy)/2,b.CenterZ+(z+zz)/2,(xx-x)/2,(yy-y)/2,(zz-z)/2);
    }
    internal static int JointCount(int id)=>id<4?25:13;
    internal static BossProxy Joint(BossState b,BossLimbState limb,int joint)
    {
        int clip=MainClip(b),tick=b.PhaseTicks;
        if(limb.Attack!=BossAttack.None){clip=2+limb.Id*2+(limb.Attack==BossAttack.Sweep?1:0);tick=limb.AttackTicks;}
        int start=Clips[clip].Limbs+Frame(clip,tick)*2432+(Offsets[limb.Id]+joint)*16;
        int radius=Read(start+12),x=Read(start),z=Read(start+8);
        if(b.TownArena && limb.Attack!=BossAttack.None)
        {
            var from=BossGeometry.AttackDirection(limb.Id);var to=BossGeometry.AttackDirection(b,limb.Id);
            long cosine=(long)from.X*to.X+(long)from.Z*to.Z,sine=(long)from.X*to.Z-(long)from.Z*to.X;
            int rotatedX=(int)((x*cosine-z*sine)/1000000);z=(int)((x*sine+z*cosine)/1000000);x=rotatedX;
        }
        return new BossProxy(limb.Id,b.CenterX+x,Read(start+4),b.CenterZ+z,radius,radius,radius);
    }
}
