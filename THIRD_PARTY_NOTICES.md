# 第三方项目

OWExtractor 是独立开发的客户端，通过命令行调用用户自行准备的 DataTool。当前仓库及客户端包没有嵌入 OWLib、TACTLib、TankLib 或 Blender 插件的实现或二进制文件。

## OWLib / DataTool

- 项目：https://github.com/overtools/OWLib
- 许可证：https://github.com/overtools/OWLib/blob/develop/LICENSE
- 上游版权：Copyright (c) 2016-2024 overtools
- 许可证：MIT

用户选择的 DataTool 及其依赖遵循各自许可证。如果以后随安装包分发第三方引擎，需要同时包含其版权、许可证及全部依赖说明。

## io_scene_owm

- 项目：https://github.com/overtools/io_scene_owm
- 用途：由用户单独安装的 Blender 导入插件。
- 许可证以插件仓库及实际下载版本为准；本项目不包含插件代码。

## 游戏内容

游戏资源不属于本项目软件许可证的授权范围。仓库及便携包不包含提取的游戏模型、贴图或音频。文档界面截图展示的是客户端与本地资源条目信息。

## unslop 设计方法参考

本项目 v0.1.1 的界面审视与改进过程参考 [unslop](https://github.com/heydamir/unslop)，作者为 [Damir Sokolovsky / @heydamir](https://x.com/heydamir)。其技能及参考文档采用 [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) 许可证。

本项目根据桌面资源工具的用途适配了间距、视觉层级、文案、状态反馈与选择标记。WPF 界面代码为独立实现，没有复制其示例前端代码、参考图片或技能文件。
