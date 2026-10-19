# Unity 程序集入口

这个本地包仅分发框架的 .NET Standard 2.1 程序集，不包含试玩逻辑。

在仓库根目录运行 `./scripts/sync-unity-libraries.sh`，再打开 Unity 工程。脚本从 `framework/src` 构建八个通用程序集（含协议和可选的自动诊断模块），一起复制 `Google.Protobuf` 与 `System.Runtime.CompilerServices.Unsafe` 运行库，并把两个试玩程序集放到示例工程的 Plugins 目录。`Grpc.Tools` 仅供构建时生成 C#，不会进入游戏；Unity 自身提供的 `System.Memory`、`System.Buffers` 等基础程序集不会重复复制。生成的 DLL 不提交 Git。

Unity 2022 的脚本编译器不直接编译仓库中的 C# 10 源码；框架源码仍只有一份。修改框架或纯 C# 游戏代码后需要重新运行同步脚本，等待 Unity 导入完成。程序集的 SHA-256 记录保存在 `artifacts/tactical-network/unity-libraries.sha256`。

协议定义及再生成方法见 [Protobuf 说明](../../docs/protobuf-protocol.md)，运行依赖的许可证见 [第三方声明](ThirdPartyNotices.md)。
