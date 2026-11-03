# 第三方项目

OWExtractor 是独立开发的客户端，通过命令行调用用户自行准备的 DataTool。当前仓库及客户端包没有嵌入 OWLib、TACTLib、TankLib 或 Blender 插件的实现或二进制文件。

## 随便携包分发的 .NET / Windows Desktop 运行时

Windows x64 自包含包包含 Microsoft.NETCore.App 和 Microsoft.WindowsDesktop.App，版权归 .NET Foundation and Contributors 及其各自第三方作者所有。

- 项目：[dotnet/runtime](https://github.com/dotnet/runtime)、[dotnet/wpf](https://github.com/dotnet/wpf)、[dotnet/winforms](https://github.com/dotnet/winforms)。
- 运行时主体采用 MIT 许可证，部分第三方组件另有许可说明。
- 每个发布包的 `licenses/` 目录包含对应 NuGet 运行时包的许可证、.NET 第三方说明，以及对应版本的 WPF 和 Windows Forms 第三方说明。
- `licenses/runtime-manifest.json` 记录实际运行时版本、说明文件来源与 SHA-256；不包含构建机器上的路径。

这些文件由发布脚本按 `OWExtractor.runtimeconfig.json` 中的实际版本收集。说明缺失时打包失败，不以本项目的 MIT 声明替代运行时声明。

## OWLib / DataTool

- 项目：https://github.com/overtools/OWLib
- 许可证：https://github.com/overtools/OWLib/blob/develop/LICENSE
- 上游版权：Copyright (c) 2016-2024 overtools
- 许可证：MIT

用户选择的 DataTool 及其依赖遵循各自许可证。如果以后随安装包分发第三方引擎，需要同时包含其版权、许可证及全部依赖说明。

## io_scene_owm

- 项目：https://github.com/overtools/io_scene_owm
- 许可证：[GNU GPL v3.0](https://github.com/overtools/io_scene_owm/blob/a13b4483ec1c0d42556b22a9ee61ffe68eae979f/LICENSE)。
- 用途：由用户单独安装的 Blender 导入插件；开发离线预览时也参考了其对导出格式、材质标识和 UV 语义的解释。
- 本项目的 C# 二进制读取、WPF 材质近似、透明边缘裁切和轻量网格在本仓库实现，不打包或调用插件，不移植其 Blender 节点、骨骼或动画实现。
- 格式依据、参考文件及实现范围见 [离线预览的格式与来源说明](docs/format-references.md)。本项目的 MIT 许可证不改变上游插件的 GPL 许可证。

## 游戏内容

游戏资源不属于本项目软件许可证的授权范围。仓库及便携包不包含提取的游戏模型、贴图或音频。随仓库和发布包提供的界面截图使用合成测试模型与条目。

## unslop 设计方法参考

本项目 v0.1.1 的界面审视与改进过程参考 [unslop](https://github.com/heydamir/unslop)，作者为 [Damir Sokolovsky / @heydamir](https://x.com/heydamir)。其技能及参考文档采用 [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) 许可证。

本项目根据桌面资源工具的用途适配了间距、视觉层级、文案、状态反馈与选择标记。WPF 界面代码为独立实现，没有复制其示例前端代码、参考图片或技能文件。
