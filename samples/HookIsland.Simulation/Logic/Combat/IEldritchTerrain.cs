using System.Collections.Generic;

namespace Advanture.HookIsland.Simulation;

/// <summary>The level supplies walkable space, sight lines and entrances to the shared encounter simulation.</summary>
public interface IEldritchTerrain
{
    bool CanStand(EldritchHabitat habitat, int x, int z);
    bool Segment(EldritchHabitat habitat, int ax, int az, int bx, int bz);
    bool Visible(int ax, int az, int bx, int bz);
    int Target(EldritchCreature creature, IReadOnlyList<Fisher> players, out Point goal);
    bool TrySpawn(EldritchCreature creature, IReadOnlyList<Fisher> players, IReadOnlyList<EldritchCreature> roster, out Point point);
}
