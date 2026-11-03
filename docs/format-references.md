# 离线预览的格式与来源说明

OWExtractor 的离线预览只读取用户已有的 OWMDL / OWMAT / PNG 导出结果。它不依赖 DataTool 内部对象，也不调用 Blender。

## 文件格式

格式的主要文档来自采用 MIT 许可证的 OWLib，以下链接固定到核对时的提交 `546efa19516d7df7ba138802d221c73ad886df28`：

- [OWMDL 2.0 字段布局](https://github.com/overtools/OWLib/blob/546efa19516d7df7ba138802d221c73ad886df28/owm-formats/owmdl.txt)
- [OWMAT 3.0 字段布局](https://github.com/overtools/OWLib/blob/546efa19516d7df7ba138802d221c73ad886df28/owm-formats/owmat.txt)
- [OWLib 许可证](https://github.com/overtools/OWLib/blob/546efa19516d7df7ba138802d221c73ad886df28/LICENSE)

`OfflineModelReader` 使用 .NET BinaryReader 逐字段读取，独立进行文本、规模、非有限数值、截断、面索引及取消检查。格式文档并非全部实现细节的完整规范；读取器还通过合成文件和历史导出样本校验，当前只支持文档所述的版本。

## 材质与 UV 语义

开发时核对了 io_scene_owm 的 [模型读取器](https://github.com/overtools/io_scene_owm/blob/a13b4483ec1c0d42556b22a9ee61ffe68eae979f/readers/OWModelReader.py)、[材质导入器](https://github.com/overtools/io_scene_owm/blob/a13b4483ec1c0d42556b22a9ee61ffe68eae979f/importer/ImportMaterial.py)及材质定义，参考范围包括字段含义、纹理 / 着色器标识、颜色与透明度的区别，以及 OW2 头发透明遮罩使用第二套 UV 的约定。该插件采用 [GPL-3.0](https://github.com/overtools/io_scene_owm/blob/a13b4483ec1c0d42556b22a9ee61ffe68eae979f/LICENSE)。

本项目使用自有的 WPF DiffuseMaterial / SpecularMaterial 近似，不包含插件的 Python 模块、Blender 节点组、骨骼或动画实现。`PreviewTextureDecoder` 保留独立 RGB / Alpha 通道；`PreviewCutout` 在加载时裁切透明边缘；`PreviewMeshIndexing` 生成仅用于软件交互的轻量网格。这些实现针对 WPF 软件预览的限制，不提供完整的游戏材质复现。

将来如引入第三方实现代码或分发插件，需要重新核对其许可和署名要求；这里的格式参考说明不授权复制上游代码。
