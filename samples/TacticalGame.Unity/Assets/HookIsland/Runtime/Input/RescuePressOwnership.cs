namespace Advanture.HookIsland
{
    /// <summary>One R press belongs to its initial action until release, even after its rescue target disappears.</summary>
    public sealed class RescuePressOwnership
    {
        public int Target {get;private set;}=-1;
        public bool OwnedByRescue {get;private set;}
        public void Capture(bool pressed,bool held,int candidate,bool targetStillValid)
        {
            if(!held){Reset();return;}
            if(pressed){Target=candidate;OwnedByRescue=candidate>=0;}
            else if(!targetStillValid)Target=-1;
        }
        public void Interrupt(bool held){Target=-1;if(!held)Reset();}
        public void Reset(){Target=-1;OwnedByRescue=false;}
    }
}
