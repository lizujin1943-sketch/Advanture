# Protobuf 协议目录

所有手写 `.proto` 文件统一在这里维护，字段使用简短的行末中文注释。

| 文件 | 职责 | 生成代码的项目 |
| --- | --- | --- |
| [session.proto](session.proto) | 通用帧同步会话、输入、最终帧和快照分块 | `FrameSync.Protocol.Protobuf` |
| [room.proto](room.proto) | 庭院试玩的加入、欢迎、确认与拒绝 | `TacticalGame.Network` |

项目通过 `.csproj` 的 `Protobuf Include` 引用对应文件，`ProtoRoot` 指向本目录。每个模块只编译自己的协议，通用框架不依赖试玩房间规则。

修改定义后运行普通 `dotnet build` 即可重新生成 C#。生成文件仍在各项目的 `obj/` 目录中，由构建管理，不手动编辑或提交。

新增协议时放入本目录，并在所属项目中明确添加引用。若单独提取框架源码，需要同时携带 `proto/session.proto`，无需携带试玩的 `room.proto`。

消息流程、校验和兼容规则见 [Protobuf 实现说明](../docs/protobuf-protocol.md)。
