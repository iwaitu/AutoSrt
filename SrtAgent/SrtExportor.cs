using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Xabe.FFmpeg;

namespace SrtAgent;

public sealed class SrtExportor
{
    public sealed record ExportOptions(
        string? FfmpegPath = null,
        int? SubtitleStreamIndex = null);

    public sealed record SubtitleStreamInfo(
        int Index,
        string? Codec,
        string? Language,
        string? Title);

    public sealed record SubtitleTrackChoiceResult(
        bool TargetLanguageAlreadyExists,
        string Action,
        int SelectedIndex,
        string? Reason);

    public async Task<IReadOnlyList<SubtitleStreamInfo>> GetSubtitleStreamsAsync(
        string videoPath,
        string? ffmpegPath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            throw new ArgumentException("Video path is required.", nameof(videoPath));

        if (!string.IsNullOrWhiteSpace(ffmpegPath))
        {
            FFmpeg.SetExecutablesPath(ffmpegPath);
        }

        var mediaInfo = await FFmpeg.GetMediaInfo(videoPath, cancellationToken).ConfigureAwait(false);
        var subtitles = mediaInfo.SubtitleStreams?.ToList() ?? new List<ISubtitleStream>();

        return subtitles
            .Select(s => new SubtitleStreamInfo(
                Index: s.Index,
                Codec: s.Codec,
                Language: s.Language,
                Title: s.Title))
            .ToList();
    }

    public async Task<SubtitleTrackChoiceResult> ChooseSubtitleTrackAsync(
        IChatClient chatClient,
        IReadOnlyList<SubtitleStreamInfo> streams,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(streams);

        if (streams.Count == 0)
        {
            throw new InvalidOperationException("No subtitle streams provided.");
        }

        var payload = JsonSerializer.Serialize(streams, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        var sb = new StringBuilder();
        sb.AppendLine("你是视频字幕轨道分析助手。下面是该视频的所有字幕轨道列表（JSON数组）。");
        sb.AppendLine("你的任务：");
        sb.AppendLine($"1) 判断是否已存在目标语言字幕（{targetLanguage}）。目标语言可根据 language 字段（如 zh/chi/zho/zh-Hans/zh-CN）或 title/codec 等信息推断。");
        sb.AppendLine("2) 如果存在目标语言字幕：返回 action=\"use_existing_target\"，并选择 index 为最合适的目标语言字幕（优先 title 包含中文/简体/繁体/Chinese，或 language 明确为 zh）。");
        sb.AppendLine("3) 如果不存在目标语言字幕：返回 action=\"translate\"，并选择 index 为‘完整对白字幕’（通常 title 不包含 forced/signs，且更像完整语言字幕而非“仅标牌/强制字幕/Commentary”）。");
        sb.AppendLine("4) 只输出 JSON 对象，不要任何解释文字。\n");
        sb.AppendLine("输出 JSON 格式：");
        sb.AppendLine("{\n  \"targetLanguageAlreadyExists\": true,\n  \"action\": \"use_existing_target\",\n  \"selectedIndex\": 3,\n  \"reason\": \"...\"\n}");
        sb.AppendLine();
        sb.AppendLine("字幕轨道列表：");
        sb.AppendLine(payload);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "你是严谨的媒体信息分析助手，只输出有效JSON。"),
            new(ChatRole.User, sb.ToString())
        };

        var response = await chatClient.GetResponseAsync(messages, new ChatOptions
        {
            Temperature = 0.0f,
            MaxOutputTokens = 400
        }, cancellationToken).ConfigureAwait(false);

        var json = ExtractJsonObject(response.Text);
        if (json is null)
        {
            var fallback = streams[0];
            return new SubtitleTrackChoiceResult(false, "translate", fallback.Index, "LLM response not JSON; fallback to first stream.");
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var exists = root.TryGetProperty("targetLanguageAlreadyExists", out var e) && e.ValueKind == JsonValueKind.True;
            var action = root.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String
                ? a.GetString() ?? "translate"
                : "translate";
            var selected = root.TryGetProperty("selectedIndex", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetInt32()
                : streams[0].Index;
            var reason = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString()
                : null;

            if (!streams.Any(x => x.Index == selected))
            {
                selected = streams[0].Index;
                reason = string.IsNullOrWhiteSpace(reason) ? "LLM did not return a valid index; fallback to first stream." : reason;
            }

            action = string.Equals(action, "use_existing_target", StringComparison.OrdinalIgnoreCase)
                ? "use_existing_target"
                : "translate";

            return new SubtitleTrackChoiceResult(exists, action, selected, reason);
        }
        catch
        {
            var fallback = streams[0];
            return new SubtitleTrackChoiceResult(false, "translate", fallback.Index, "LLM JSON parse failed; fallback to first stream.");
        }
    }

    private static string? ExtractJsonObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        var end = text.LastIndexOf('}');
        if (end <= start)
        {
            return null;
        }

        return text[start..(end + 1)].Trim();
    }

    public async Task<string> ExportEmbeddedSubtitlesAsSrtTextAsync(
        string videoPath,
        ExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            throw new ArgumentException("Video path is required.", nameof(videoPath));

        options ??= new ExportOptions();

        if (!string.IsNullOrWhiteSpace(options.FfmpegPath))
        {
            FFmpeg.SetExecutablesPath(options.FfmpegPath);
        }

        var mediaInfo = await FFmpeg.GetMediaInfo(videoPath, cancellationToken).ConfigureAwait(false);
        var subtitles = mediaInfo.SubtitleStreams?.ToList() ?? new List<ISubtitleStream>();

        if (subtitles.Count == 0)
            throw new InvalidOperationException("No embedded subtitle streams found.");

        ISubtitleStream selected;
        if (options.SubtitleStreamIndex.HasValue)
        {
            selected = subtitles.FirstOrDefault(s => s.Index == options.SubtitleStreamIndex.Value)
                ?? throw new ArgumentOutOfRangeException(nameof(options.SubtitleStreamIndex), "Subtitle stream index not found.");
        }
        else
        {
            selected = subtitles[0];
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.srt");

        try
        {
            var conversion = FFmpeg.Conversions.New()
                .AddStream(selected)
                .SetOutput(tempPath);

            await conversion.Start(cancellationToken).ConfigureAwait(false);

            return await File.ReadAllTextAsync(tempPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }
}
