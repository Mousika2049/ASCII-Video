# 🌊 AsciiFlow - 高性能全彩 ASCII 媒体转换器

<p align="center">
  <b>基于 .NET 10、FFmpeg 与 SkiaSharp 的全彩字符视频处理工具</b>
</p>

---

## ✨ 核心特性

- 🎨 **原视频全彩渲染（Color ASCII）**
  支持字符级 RGB 色彩采样与 Alpha 遮罩混合，在保留 ASCII 风格的同时还原主要色彩。
- 👁️ **人眼感知最佳灰度映射（BT.709 + S-Curve）**
  遵循 ITU-R BT.709 亮度标准，结合 S 曲线 (S-Curve) 伽马对比度增强，明暗细节清晰，高对比度不失真。
- 🎞️ **多媒体格式支持**
  输入由 FFmpeg 按内容自动探测，可读取 MP4、MKV、MOV、AVI、WebM、FLV、WMV、M4V、MPEG-TS 等包含视频流的媒体文件。
- 📦 **多容器输出**
  支持 MP4、M4V、MOV、MKV、AVI、MPEG-TS 和 WebM；WebM 自动使用 VP9，其他容器使用 H.264。
- 🎵 **原声音频无损透传（Audio Stream Copy）**
  音频编码与目标容器兼容时复制原始音频包；不兼容时安全忽略音轨并在详细日志中说明。
- ⚡ **三槽有界处理流水线**
  解码、映射/渲染与编码三阶段重叠执行；三个循环复用的帧槽控制内存上界。渲染器直接写入 YUV420P，不再生成整帧 RGB24 中间图像；单帧内融合灰度计算、字符映射和颜色采样。
- 🎞️ **经过样本验证的编码模式**
  默认 `speed` 使用 `libx264 ultrafast / CRF 20 / 0 B 帧`；也可选择较小文件的 `balanced` 或兼容旧参数的 `quality`。
- 🐧 **全平台字体兼容（Cross-Platform Font Fallback）**
  内置 Windows / Linux / macOS 自动字体选择降级机制（Consolas ➔ Cascadia Mono ➔ DejaVu Sans Mono ➔ Liberation Mono ➔ Monospace），防止跨平台全黑画面。
- ⏱️ **智能帧率匹配（Auto Frame Rate）**
  使用有理数保留 `30000/1001`、`24000/1001` 等小数帧率，避免整数截断造成的累计漂移。
- 📈 **可信的进度语义**
  优先使用媒体提供的总帧数；缺少该元数据时，以时长和平均帧率显示明确标注为“约”的估算进度，完成摘要始终报告实际处理帧数。
- 🛡️ **安全输出**
  先写入同目录临时文件，成功完成容器尾部后再替换目标；失败或取消不会破坏旧输出。

---

## 🛠️ 系统流水线

```mermaid
flowchart LR
    A[📹 输入视频] --> B[1. FFmpeg 解码 RGB24]
    B -->|3 槽有界缓冲| C[2. BT.709 / 颜色采样 / S-Curve ASCII 融合映射]
    C --> D[3. 字符多线程直接渲染 YUV420P]
    D -->|3 槽有界缓冲| E[4. H.264 / VP9 直接编码 + 兼容音轨透传]
    E --> F[🎬 最终 ASCII 视频]
```

---

## 🚀 快速开始

### 依赖环境
- **[.NET 10.0 SDK](https://dotnet.microsoft.com/)** 或更高版本
- **FFmpeg 8.x** 动态链接库（已内置 Linux 64位库）

### 构建项目

```bash
# 克隆或进入项目目录
cd AsciiFlow

# 编译项目
dotnet build AsciiFlow.slnx -c Release

# 运行单元测试
dotnet test AsciiFlow.slnx -c Release
```

### 基础使用

```bash
# 最简转换（自动匹配帧率与原视频颜色）
dotnet run --project src/AsciiFlow.App -- -i input.mp4 -o output.mp4

# MKV 输入并输出 MOV
dotnet run --project src/AsciiFlow.App -- -i input.mkv -o output.mov

# WebM / VP9 输出
dotnet run --project src/AsciiFlow.App -- -i input.avi -o output.webm

# 指定字符网格分辨率 (如 160×90 字符)
dotnet run --project src/AsciiFlow.App -- -i input.mp4 -o output.mp4 -w 160 -h 90

# 生成经典黑白 ASCII 视频
dotnet run --project src/AsciiFlow.App -- -i input.mp4 -o output.mp4 --color false

# 测试预览模式（仅转换前 100 帧）
dotnet run --project src/AsciiFlow.App -- -i input.mp4 -o output.mp4 --max-frames 100
```

---

## 📖 命令行参数说明

| 短参数 | 长参数 | 默认值 | 说明 |
| :--- | :--- | :--- | :--- |
| `-i` | `--input` | **必填** | 输入媒体文件，格式由 FFmpeg 自动探测 |
| `-o` | `--output` | `output/output_ascii.mp4` | 输出文件；支持 `mp4/m4v/mov/mkv/avi/ts/m2ts/webm` |
| `-w` | `--width` | `240` | ASCII 字符画宽度（默认 `240` 字符超高清） |
| `-h` | `--height` | `0` (自动) | ASCII 字符画高度（`0` 表示根据原视频比例自动计算，16:9 对应 `135`） |
| `-f` | `--framerate` | `0.0` (自动) | 输出视频帧率（`0` 表示自动与原视频一致） |
| `-C` | `--color` | `true` | 是否启用彩色模式 (`true` / `false`) |
| `-c` | `--charset` | `standard` | 字符集选用：`standard`（70 字符）或 `detailed`（16 字符） |
| | `--font-family` | `Consolas` | 渲染字体族名称（跨平台自动回退） |
| | `--font-size` | `12` | 渲染字体大小 (px) |
| | `--max-frames` | `0` | 最大转换帧数（`0` 表示转换全片） |
| | `--encoder-mode` | `speed` | 编码模式：`speed`、`balanced` 或 `quality` |
| | `--no-progress` | `false` | 禁用动态进度条（输出重定向时会自动禁用） |
| `-v` | `--verbose` | `false` | 显示编码、渲染、性能明细和完整错误诊断 |

---

## 🧭 运行行为说明

- 输入与输出不能是同一路径。输出先写入目标目录中的临时文件，只有编码和容器收尾全部成功后才替换目标文件。
- 音频仅在源编码与目标容器确认兼容时原样透传；例如 WebM 不接受 AAC 时会保留视频转换结果并忽略不兼容音轨，`--verbose` 会显示判断结果。
- 源媒体没有 `nb_frames` 时，总帧数保持“未知”。界面只使用 `时长 × 平均帧率` 生成标有“约”的进度参考，并将其最高限制为 `99.9%`；完成摘要使用实际解码并编码的帧数。
- `--max-frames` 是处理上限，适合快速预览。使用 `--no-progress` 可关闭动态进度；标准输出被重定向时也会自动关闭。

---

## 📊 性能测量

默认终端只显示输入、输出、核心配置、源视频信息、进度和完成摘要。使用 `--verbose` 时，额外显示容器、编码器、音轨状态以及各处理阶段的实际耗时。

编码模式参数：

| 模式 | H.264 / libx264 | WebM / VP9 | 适用场景 |
| :--- | :--- | :--- | :--- |
| `speed`（默认） | `ultrafast / CRF 20 / 0 B 帧` | `realtime / cpu-used 8 / CRF 20` | 最高吞吐与低编码延迟，可接受更大文件 |
| `balanced` | `superfast / CRF 20` | `good / cpu-used 6 / CRF 20` | 平衡速度与文件体积 |
| `quality` | `fast / CRF 23 / tune fastdecode` | `good / cpu-used 4 / CRF 23` | 更注重压缩效率 |

WebM/VP9 会根据输出分辨率和可用 CPU 自动设置编码线程与 tile 分块；`speed` 模式同时关闭 lookahead，减少短视频和预览转换的收尾等待。

在 120 帧、1920×1080 彩色 ASCII 样本上，默认模式的流水线由旧参数的 72.6 FPS 提升至 92.4 FPS；H.264 子阶段由约 1.99 ms/帧降至 0.96 ms/帧。相同样本的隔离编码测试中，默认模式相对旧参数的综合 SSIM 为 0.9911（旧参数对照为 0.9919），PSNR 为 41.76 dB（旧参数对照为 40.20 dB）。默认模式样本文件为 10.62 MB，`balanced` 为 7.99 MB，`quality` 为 4.66 MB。测试结果取决于源编码、分辨率、ASCII 网格、字体、CPU 和 FFmpeg 构建，请在目标机器上使用同一输入进行比较，不把单台机器的结果视为通用保证。

---

## 📁 项目结构

```text
AsciiFlow/
├── src/
│   ├── AsciiFlow.App/             # 命令行应用层 (CLI & Pipeline Orchestration)
│   └── AsciiFlow.Core/            # 核心领域逻辑库
│       ├── Video/                 # FFmpeg 解码器与音轨处理
│       ├── Processing/            # 可独立使用的灰度转换组件
│       ├── AsciiMapping/          # 灰度到 ASCII 字符与颜色映射器
│       ├── Rendering/             # SkiaSharp 字符位图渲染引擎
│       └── Encoding/              # FFmpeg 容器选择与 H.264/VP9 编码
├── tests/
│   └── AsciiFlow.Core.Tests/      # 核心逻辑、CLI、终端输出与并发流水线测试
├── ffmpeg/                        # FFmpeg 原生动态库
├── output/                        # 默认输出目录
└── README.md                      # 项目说明文档
```

提交改动前可运行与 CI 一致的检查：

```bash
dotnet format AsciiFlow.slnx --verify-no-changes --no-restore
dotnet build AsciiFlow.slnx -c Release --no-restore
dotnet test AsciiFlow.slnx -c Release --no-build --no-restore
```

---

## 📄 开源协议

本项目基于 [MIT License](LICENSE) 协议开源。
