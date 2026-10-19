using System.Runtime.CompilerServices;

// 房间边界测试需要构造旧版和畸形消息，不向游戏接入 API 暴露协议细节。
[assembly: InternalsVisibleTo("TacticalGame.Tests")]
