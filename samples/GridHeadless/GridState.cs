using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FrameSync.Samples.Grid
{
    public readonly struct GridPoint
    {
        public GridPoint(Guid participantId, int x, int y) { ParticipantId = participantId; X = x; Y = y; }
        public Guid ParticipantId { get; }
        public int X { get; }
        public int Y { get; }
    }
    public sealed class GridState
    {
        public GridState(ulong steps, IReadOnlyList<GridPoint> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            var sorted = points.OrderBy(p => p.ParticipantId).ToArray();
            if (sorted.Length == 0 || sorted.Length > ushort.MaxValue || sorted.Any(p => p.ParticipantId == Guid.Empty) ||
                sorted.Select(p => p.ParticipantId).Distinct().Count() != sorted.Length)
                throw new ArgumentException("Grid state needs unique, non-empty participant identities.");
            Steps = steps; Points = new ReadOnlyCollection<GridPoint>(sorted);
        }
        public ulong Steps { get; }
        public IReadOnlyList<GridPoint> Points { get; }
    }
}
