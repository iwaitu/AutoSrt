# AutoSrt

`AutoSrt` 是一个基于 **.NET MAUI (.NET 10)** 的跨平台字幕工具：

- 从视频文件中 **提取内嵌字幕轨道**（使用 `Xabe.FFmpeg`）
- 将字幕 **翻译为中文**（使用 `SrtAgent` + `IChatClient`，根据模型选择不同的 vLLM ChatClient）；
- 目前测试通过 `qwen3-next-80b-a3b-instruct` 和 `gpt-oss-120b` 两个模型，均可完成字幕翻译任务。主要依赖 `Ivilson.AI.VllmChatClient` 实现 vLLM ChatClient。
- 强烈建议使用 qwen3-next-80B-A3B ，获得速度和性能的最佳平衡，gpt-oss-120b 可能会消耗更多时间。90分钟电影的字幕 qwen3-next-80B-A3B 模型耗时7-9分钟，而gpt-oss-120b 则会消耗5倍的时间。
- 考虑到价格问题，主要使用 openroute 的本地部署模型（如 Qwen3-Next、GPT-OSS-120B 等）完成字幕翻译任务。
- 选国内模型时建议使用 `qwen3-next-80b-a3b-instruct`，效果较好且响应速度快。但是不要使用阿里云百炼平台的接口，因为该模型在百炼平台上有RPM限制，频繁调用时会报错。
- 另外不建议使用思维链模型，因为字幕翻译任务对上下文长度要求较高，思维链模型的上下文长度通常较短，容易导致截断。
- 在视频所在目录输出 `*.zh.srt`
- 支持将视频内的 **所有字幕轨道列表提交给 LLM**，由 LLM 选择“最可能是完整对白”的轨道；若已存在中文轨道则直接导出并跳过翻译
- 翻译过程支持 **进度日志**（约每 5% 更新一次）

## OPENRoute 简单比较

（同样以“百万 token”单位计费）

- 输入成本：gpt-oss-120b 约 $0.039 vs Qwen3-Next-80B-A3B-Instruct 约 $0.09
  - ? gpt-oss 约 2.3× 更便宜 的输入费用。
- 输出成本：gpt-oss-120b 约 $0.19 vs Qwen3-Next-80B-A3B-Instruct 约 $1.10
  - ? gpt-oss 约 5.8× 更便宜 的输出费用。



## 项目结构

- `AutoSrt/`：MAUI UI 应用
- `SrtAgent/`：字幕导出与翻译核心库
  - `SrtExportor`：枚举字幕轨道 / 导出指定轨道为 SRT 文本 / 基于 LLM 选择轨道
  - `SrtTranslator`：将 SRT 翻译为目标语言（默认中文），支持进度回调
- `SrtAgent.Tests/`：xUnit 测试

## 运行方式（MAUI）

1. 启动 `AutoSrt` 应用。
2. 在页面中填写/选择 LLM 参数：
   - `Endpoint`：例如 `http://localhost:8000/v1/{1}`（或你的服务地址）
   - `API Key`：你的 key
   - `Model Name`：从下拉框选择（见下方“模型配置说明”）
3. 点击“选择视频”，选择一个包含内嵌字幕的视频文件（如 `mkv/mp4`）。
4. 点击“开始处理”。

应用将：

1. 枚举所有字幕轨道并交给 LLM 判断：
   - 是否已包含中文轨道（若有则直接导出）
   - 哪条轨道最可能是“完整对白字幕”
2. 导出选择的字幕轨道为 SRT 文本
3. 如需翻译则调用 LLM 翻译为中文，并在日志中输出进度
4. 输出到视频同目录：

- `原文件名.zh.srt`

## 模型配置说明（Model Name 下拉框）

当前 UI 的 `Model Name` 为下拉选择，内置选项为：

- `qwen/qwen3-next-80b-a3b-instruct`（默认）
- `openai/gpt-oss-120b`

### 模型与 ChatClient 的对应关系

应用会根据你选择的 `model` 自动选择不同的 `IChatClient` 实现（逻辑位于 `AutoSrt/Services/VllmChatClientFactory.cs`）：

- `qwen/...`：使用 `VllmQwen3NextChatClient`
- `openai/gpt-oss-120b`：使用 `VllmGptOssChatClient`

> 注意：如果你选择 `openai/gpt-oss-120b` 后运行时报错提示找不到 `VllmGptOssChatClient`，说明当前依赖库未提供该类型；需要升级/替换 vLLM ChatClient 依赖或自行实现该类。

## 依赖

- `Xabe.FFmpeg`：提取内嵌字幕
- `Microsoft.Extensions.AI`：统一的 ChatClient 接口
- `Ivilson.AI.VllmChatClient`：vLLM ChatClient 实现

> 注意：首次使用时请确保运行环境可用 `ffmpeg`/`ffprobe`。`Xabe.FFmpeg` 可能会自动下载或需要你自行配置 ffmpeg 可执行文件。

## 说明与限制

- 如果字幕轨道是图形字幕（如 PGS/VobSub），直接导出为 `.srt` 可能内容很少或不可用，通常需要 OCR 才能得到文本字幕。
- “是否为完整对白字幕”的选择由 LLM 依据 `language/title/codec` 等元数据做推断，必要时可加入人工选择流程。

## 测试

在解决方案目录执行：

```bash
dotnet test
