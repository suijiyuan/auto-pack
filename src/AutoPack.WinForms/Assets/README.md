# 应用图标

- `AppIcon.png`：通过内置 imagegen 工具生成的透明源图，表示文件打包。
- `AppIcon.ico`：包含 16、20、24、32、40、48、64、128、256 像素的 PNG 图层，适用于 Windows 10/11。
- 项目通过 `ApplicationIcon` 将 ICO 编入 EXE 的原生资源，并将同一 ICO 嵌入程序集，供主窗口和设置窗口的标题栏及任务栏使用。发布时不需要额外图标文件。

ICO 由源 PNG 使用 macOS `sips` 等比缩小，再按 Windows ICO 目录格式封装；转换不改变图案。

## 生成提示词

工具：内置 `image_gen`，透明背景。

```text
Use case: logo-brand. Asset type: Windows desktop application icon for a Chinese file packaging and encrypted archive utility called AutoPack. Create one finished app icon, centered on a transparent square canvas. Subject: a compact blue archive/package box with a crisp white folded file sheet and a short zipper motif, a single cohesive easy-to-recognize symbol. Style: polished modern Windows desktop utility icon, simple bold geometry, subtle restrained dimensional shading, clean silhouette, strong contrast, readable at 16x16 and 32x32, no tiny details. Palette: medium royal blue matching #235CD2, light blue highlights and white file. Frame: icon occupies about 85 percent of canvas, generous symmetric transparent margin, no container background tile. No text, no letters, no numbers, no watermark, no ground plane, no decorative objects. Deliver an isolated icon with real alpha transparency.
```
