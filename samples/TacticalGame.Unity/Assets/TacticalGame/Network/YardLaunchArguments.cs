using System;
using UnityEngine;
namespace Advanture.TacticalGame.NetworkView
{
    /// <summary>可选命令行启动入口，便于启动两个独立客户端；普通双击启动仍显示菜单。</summary>
    [RequireComponent(typeof(NetworkYardController))]
    public sealed class YardLaunchArguments : MonoBehaviour
    {
        public static string Value(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == key) return args[i + 1];
            return null;
        }
        public static bool Has(string key) => Array.IndexOf(Environment.GetCommandLineArgs(), key) >= 0;
        private void Start()
        {
            string role = Value("-yard-role"); if (role == null) return;
            int port = int.TryParse(Value("-yard-port"), out int p) ? p : 7777;
            var room = GetComponent<NetworkYardController>();
            if (role == "host") room.HostRoom(port);
            else if (role == "join") room.JoinRoom(Value("-yard-address") ?? "127.0.0.1", port);
        }
    }
}
