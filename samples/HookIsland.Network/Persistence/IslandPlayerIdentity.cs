using System;
using System.IO;
using System.Security.Cryptography;
using FrameSync.Storage.FileSystem;

namespace Advanture.HookIsland.Network;

/// <summary>Local ownership proof reused across application restarts; never sent in snapshots or logs.</summary>
public static class IslandPlayerIdentity
{
    public static byte[] LoadOrCreate(string path)
    {
        var store=new FileSaveStore(path);
        // Serialize the check-and-create operation as well as the final atomic write.
        using var initialization=new FileStream(path+".identity-lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if(File.Exists(path))return Validate(store.Read(32));
        var secret=Create();store.WriteAtomic(secret,32);return secret;
    }
    private static byte[] Validate(byte[] value)
    {if(value.Length!=32)throw new InvalidDataException("The local player identity is damaged.");return value;}
    internal static byte[] Create(){var value=new byte[32];using var rng=RandomNumberGenerator.Create();rng.GetBytes(value);return value;}
}
