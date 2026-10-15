using System.Runtime.CompilerServices;

// 仅向框架测试开放协议构造器，用于发送非法/重复消息；游戏接入方不依赖内部格式类型。
[assembly: InternalsVisibleTo("FrameSync.Tests")]
