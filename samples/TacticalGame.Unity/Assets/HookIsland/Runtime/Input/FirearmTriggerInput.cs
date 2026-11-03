namespace Advanture.HookIsland
{
    /// <summary>Local trigger latch: automatic fire survives multiple simulation ticks per rendered frame.</summary>
    public sealed class FirearmTriggerInput
    {
        private int weaponId;
        private bool pending,held,releaseRequired;
        public void Capture(int weapon,bool automatic,bool active,bool pressed,bool down)
        {
            if(weaponId!=weapon)
            {pending=held=false;releaseRequired=down && !pressed;weaponId=weapon;}
            if(!active){Interrupt();return;}
            if(!down)releaseRequired=false;
            if(releaseRequired){pending=held=false;return;}
            pending|=pressed;held=automatic && down;
        }
        public bool Consume(){bool fire=pending || held;pending=false;return fire;}
        public void Interrupt(){pending=held=false;releaseRequired=true;}
    }
}
