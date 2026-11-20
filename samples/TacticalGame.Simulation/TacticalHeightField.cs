using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Advanture.Tactical.Simulation;

/// <summary>从静态关卡烘焙，每格含脚下支撑面与遮挡顶面。存档和主客机共用同一份整数数据。</summary>
internal sealed class TacticalHeightField
{
    private const short Missing = short.MinValue;
    private const int Header = 24;
    private readonly int originX, originZ, cell, width, depth, stride;
    private readonly byte[] columns, encoded;
    internal bool HasCeiling => stride == 6;

    internal TacticalHeightField(ReadOnlySpan<byte> bytes, MapRect area, bool hasCeiling)
    {
        if (bytes.Length < Header) throw new InvalidDataException("高程地图头被截断。");
        originX = ReadInt(bytes, 0); originZ = ReadInt(bytes, 4); cell = ReadInt(bytes, 8);
        width = ReadInt(bytes, 12); depth = ReadInt(bytes, 16);
        int packedSize = ReadInt(bytes, 20);
        if (cell < 100 || cell > 1000 || width <= 0 || depth <= 0 || (long)width * depth > 500000 ||
            originX != area.MinX || originZ != area.MinZ || (long)width * cell + originX != area.MaxX ||
            (long)depth * cell + originZ != area.MaxZ || packedSize <= 0 || packedSize > 64000 || packedSize != bytes.Length - Header)
            throw new InvalidDataException("高程地图范围或长度不合法。");
        encoded = bytes.ToArray();
        stride = hasCeiling ? 6 : 4;
        columns = new byte[width * depth * stride];
        using var input = new MemoryStream(encoded, Header, packedSize, false);
        using var inflater = new DeflateStream(input, CompressionMode.Decompress);
        int offset = 0;
        while (offset < columns.Length)
        {
            int read = inflater.Read(columns, offset, columns.Length - offset);
            if (read == 0) throw new InvalidDataException("高程地图未完整解压。");
            offset += read;
        }
        if (inflater.ReadByte() != -1) throw new InvalidDataException("高程地图数据过长。");
    }

    internal ReadOnlySpan<byte> Encoded => encoded;
    private static int ReadInt(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset));
    private bool Inside(int col, int row) => (uint)col < (uint)width && (uint)row < (uint)depth;
    private short Value(int col, int row, int channel) =>
        BinaryPrimitives.ReadInt16LittleEndian(columns.AsSpan((row * width + col) * stride + channel));
    internal int CeilingAt(int x, int z)
    {
        if (!HasCeiling) return int.MaxValue;
        int col = (x - originX) / cell, row = (z - originZ) / cell;
        if (!Inside(col, row)) return int.MaxValue;
        short ceiling = Value(col, row, 4);
        return ceiling == short.MaxValue ? int.MaxValue : ceiling * 10;
    }
    internal bool TryGround(int x, int z, out int height)
    {
        height = 0;
        if (x < originX || z < originZ) return false;
        int col = (x - originX) / cell, row = (z - originZ) / cell;
        if (!Inside(col, row)) return false;
        short value = Value(col, row, 0);
        if (value == Missing) return false;
        height = value * 10;
        return true;
    }

    // Test every touched cell, including both shoulders at an exact diagonal corner.
    // Sampling a smoothed route can miss a sliver of rubble that blocks the next short AI step.
    internal bool Walk(int ax,int az,int bx,int bz)
    {
        int col=(ax-originX)/cell,row=(az-originZ)/cell;
        int endCol=(bx-originX)/cell,endRow=(bz-originZ)/cell;
        int dx=Math.Abs(bx-ax),dz=Math.Abs(bz-az),sx=Math.Sign(bx-ax),sz=Math.Sign(bz-az);
        int edgeX=Math.Abs(originX+(col+(sx>0?1:0))*cell-ax);
        int edgeZ=Math.Abs(originZ+(row+(sz>0?1:0))*cell-az);
        short floor=Value(col,row,0);
        while(col!=endCol || row!=endRow)
        {
            long crossX=col==endCol?long.MaxValue:(long)edgeX*dz;
            long crossZ=row==endRow?long.MaxValue:(long)edgeZ*dx;
            bool stepX=crossX<=crossZ,stepZ=crossZ<=crossX;
            int nextCol=stepX?col+sx:col,nextRow=stepZ?row+sz:row;
            short next=Value(nextCol,nextRow,0);
            if(!WalkableStep(floor,next))return false;
            if(stepX && stepZ)
            {
                short sideX=Value(nextCol,row,0),sideZ=Value(col,nextRow,0);
                if(!WalkableStep(floor,sideX) || !WalkableStep(sideX,next) ||
                   !WalkableStep(floor,sideZ) || !WalkableStep(sideZ,next))return false;
            }
            if(stepX)edgeX+=cell;
            if(stepZ)edgeZ+=cell;
            col=nextCol;row=nextRow;floor=next;
        }
        return true;
    }
    private static bool WalkableStep(short from,short to) =>
        from!=Missing && to!=Missing && Math.Abs(from-to)*10<=VerticalMotion.StepHeight;

    // Visit each crossed cell, even if one input spans several stair treads.
    internal int Sweep(int from, int other, int target, bool xAxis, int? airborneFeet)
    {
        if (from == target) return from;
        int origin = xAxis ? originX : originZ, direction = Math.Sign(target - from);
        int current = from;
        TryGround(xAxis ? from : other, xAxis ? other : from, out int previous);
        while (current != target)
        {
            int near = origin + ((current - origin) / cell) * cell;
            int next = direction > 0 ? Math.Min(target, near + cell) : Math.Max(target, near - 1);
            int x = xAxis ? next : other, z = xAxis ? other : next;
            if (!TryGround(x, z, out int height)) return direction > 0 ? near + cell - 1 : near;
            if (airborneFeet.HasValue
                ? height > airborneFeet.Value || airborneFeet.Value + VerticalMotion.BodyHeight > CeilingAt(x, z)
                : height > previous + VerticalMotion.StepHeight)
                return direction > 0 ? near + cell - 1 : near;
            if (!airborneFeet.HasValue && previous - height > VerticalMotion.StepHeight) airborneFeet = previous;
            current = next;
            previous = height;
        }
        return current;
    }

    // Traverse cell boundaries at the same integer distance precision as the existing hitscan.
    internal long Trace(int x, int y, int z, int dx, int dy, int dz, long limit)
    {
        const long unit = (long)FixedAim.Scale * 1000;
        int col = (x - originX) / cell, row = (z - originZ) / cell;
        long enter = 0;
        while (Inside(col, row) && enter < limit)
        {
            long tx = Crossing(x, dx, originX, col, unit), tz = Crossing(z, dz, originZ, row, unit);
            long exit = Math.Min(limit, Math.Min(tx, tz));
            short top = Value(col, row, 2);
            if (top != Missing)
            {
                long above = ((long)y - top * 10) * unit;
                if (above + enter * dy <= 0) return enter;
                if (dy < 0)
                {
                    long hit = (above + (-dy) - 1) / (-dy);
                    if (hit <= exit) return hit;
                }
            }
            if (exit == limit) break;
            if (tx <= tz) col += Math.Sign(dx);
            if (tz <= tx) row += Math.Sign(dz);
            enter = exit;
        }
        return limit;
    }

    private long Crossing(int position, int direction, int origin, int index, long unit)
    {
        if (direction == 0) return long.MaxValue;
        int edge = origin + (direction > 0 ? index + 1 : index) * cell;
        long numerator = Math.Abs((long)edge - position) * unit;
        return (numerator + Math.Abs(direction) - 1) / Math.Abs(direction);
    }
}
