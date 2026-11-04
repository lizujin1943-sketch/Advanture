namespace Advanture.HookIsland
{
    /// <summary>投掷的本地按键闩锁。取消或失焦后，必须松开再按；不会把迟到的松手变成投掷。</summary>
    public sealed class GrenadeAimInput
    {
        private bool requireRelease=true;
        public bool Aiming {get;private set;}
        public bool Begin(bool allowed,bool pressed,bool held)
        {
            if(!held)requireRelease=false;
            if(!allowed){Interrupt(held);return false;}
            if(!requireRelease && pressed && held){Aiming=true;return true;}
            return false;
        }
        public bool Release(bool released)
        {
            if(!Aiming || !released)return false;
            Aiming=false;return true;
        }
        public void Interrupt(bool held=true){Aiming=false;requireRelease=held;}
    }
}
