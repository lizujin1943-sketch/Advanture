using System;
using FrameSync.Persistence;
using FrameSync.Storage.FileSystem;

namespace Advanture.HookIsland.Network;

/// <summary>Atomic local persistence, separate from the UDP room lifecycle.</summary>
public sealed class IslandSaveStore
{
    private readonly ISaveStore store;
    public IslandSaveStore(string path):this(new FileSaveStore(path)){}
    internal IslandSaveStore(ISaveStore store){this.store=store??throw new ArgumentNullException(nameof(store));}
    public void Save(IslandSave save)
    {
        if(save==null)throw new ArgumentNullException(nameof(save));
        var bytes=IslandSaveCodec.Encode(save);_=IslandSaveCodec.Decode(bytes);
        store.WriteAtomic(bytes,IslandSaveCodec.MaximumBytes);
    }
    public IslandSave Load()=>IslandSaveCodec.Decode(store.Read(IslandSaveCodec.MaximumBytes));
}
