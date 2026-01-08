# AutoSrt

`AutoSrt` 是一个基于 **.NET MAUI (.NET 10)** 的跨平台字幕工具：

- 从视频文件中 **提取内嵌字幕轨道**（使用 `Xabe.FFmpeg`）
- 将字幕 **翻译为中文**（使用 `SrtAgent` + `IChatClient`，当前集成 `VllmQwen3NextChatClient`）
- 在视频所在目录输出 `*.zh.srt`
- 支持将视频内的 **所有字幕轨道列表提交给 LLM**，由 LLM 选择“最可能是完整对白”的轨道；若已存在中文轨道则直接导出并跳过翻译
- 翻译过程支持 **进度日志**（约每 5% 更新一次）

## 项目结构

- `AutoSrt/`：MAUI UI 应用
- `SrtAgent/`：字幕导出与翻译核心库
  - `SrtExportor`：枚举字幕轨道 / 导出指定轨道为 SRT 文本 / 基于 LLM 选择轨道
  - `SrtTranslator`：将 SRT 翻译为目标语言（默认中文），支持进度回调
- `SrtAgent.Tests/`：xUnit 测试

## 运行方式（MAUI）

1. 启动 `AutoSrt` 应用。
2. 在页面中填写 VLLM ChatClient 参数：
   - `Endpoint`：例如 `http://localhost:8000/v1/{1}`（或你的服务地址）
   - `API Key`：你的 key
   - `Model`：例如 `qwen3-next-80b-a3b-instruct`
3. 点击“选择视频”，选择一个包含内嵌字幕的视频文件（如 `mkv/mp4`）。
4. 点击“提取并翻译字幕（中文）”。

应用将：

1. 枚举所有字幕轨道并交给 LLM 判断：
   - 是否已包含中文轨道（若有则直接导出）
   - 哪条轨道最可能是“完整对白字幕”
2. 导出选择的字幕轨道为 SRT 文本
3. 如需翻译则调用 LLM 翻译为中文，并在日志中输出进度
4. 输出到视频同目录：

- `原文件名.zh.srt`

## 依赖

- `Xabe.FFmpeg`：提取内嵌字幕
- `Microsoft.Extensions.AI`：统一的 ChatClient 接口
- `Ivilson.AI.VllmChatClient`：VLLM ChatClient 实现（`VllmQwen3NextChatClient`）

> 注意：首次使用时请确保运行环境可用 `ffmpeg`/`ffprobe`。`Xabe.FFmpeg` 可能会自动下载或需要你自行配置 ffmpeg 可执行文件。

## 说明与限制

- 如果字幕轨道是图形字幕（如 PGS/VobSub），直接导出为 `.srt` 可能内容很少或不可用，通常需要 OCR 才能得到文本字幕。
- “是否为完整对白字幕”的选择由 LLM 依据 `language/title/codec` 等元数据做推断，必要时可加入人工选择流程。

## 测试

在解决方案目录执行：

```bash
dotnet test
```
