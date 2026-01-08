using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.GptOss;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SrtAgent;

public sealed class SrtTranslator
{
    private readonly IChatClient _chatClient;
    private readonly ILogger _logger;

    public SrtTranslator(IChatClient chatClient, ILogger logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public sealed record TranslationOptions(
        string TargetLanguage,
        int BatchSize = 10,
        int ContextSize = 3,
        float Temperature = 0.3f,
        int MaxOutputTokens = 2000,
        int MaxRetries = 3,
        int RetryBaseDelayMs = 1000,
        int BatchDelayMs = 500);

    public async Task<string> TranslateSrtAsync(
        string srtContent,
        TranslationOptions? options = null,
        Action<int, string?>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(srtContent))
        {
            throw new ArgumentException("SRT content is empty.", nameof(srtContent));
        }

        options ??= new TranslationOptions(TargetLanguage: "中文");

        var subtitles = ParseSrtFile(srtContent);
        if (subtitles.Count == 0)
        {
            throw new FormatException("Invalid or empty SRT content.");
        }

        var translated = await TranslateSubtitlesAsync(subtitles, options, progress, cancellationToken).ConfigureAwait(false);
        return GenerateSrtContent(translated);
    }

    public async Task<IReadOnlyList<SrtSubtitle>> TranslateSubtitlesAsync(
        IReadOnlyList<SrtSubtitle> subtitles,
        TranslationOptions? options = null,
        Action<int, string?>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subtitles);

        options ??= new TranslationOptions(TargetLanguage: "中文");

        var translatedSubtitles = new List<SrtSubtitle>(subtitles.Count);

        var batchSize = Math.Max(1, options.BatchSize);
        var totalBatches = (int)Math.Ceiling((double)subtitles.Count / batchSize);
        long allInputTokens = 0;
        long allOutputTokens = 0;
        progress?.Invoke(0, "start");
        var lastReported = 0;

        for (var batchIndex = 0; batchIndex < totalBatches; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = subtitles.Skip(batchIndex * batchSize).Take(batchSize).ToList();
            var progressPercent = (int)((double)(batchIndex + 1) / totalBatches * 100);

            _logger.LogInformation($"Translating batch {batchIndex + 1}/{totalBatches} ({progressPercent}%)");

            var contextBatch = GetContextualBatch(subtitles, batchIndex, batchSize, Math.Max(0, options.ContextSize));
            var prompt = BuildTranslationPrompt(contextBatch, batch, options.TargetLanguage);

            List<SrtSubtitle>? translatedBatch = null;

            for (var retry = 0; retry < Math.Max(1, options.MaxRetries); retry++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var messages = new List<ChatMessage>
                    {
                        new(ChatRole.System, $"你是一个专业的字幕翻译专家。将字幕翻译成{options.TargetLanguage}。严格遵守输出格式要求，不要输出任何解释文字。"),
                        new(ChatRole.User, prompt)
                    };

                    var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
                    var translatedText = response.Text;
                    allOutputTokens += response.Usage?.OutputTokenCount ?? 0;
                    allInputTokens += response.Usage?.InputTokenCount ?? 0;

                    translatedBatch = ParseTranslationResult(translatedText, batch);

                    if (translatedBatch.Count >= batch.Count * 0.8)
                    {
                        break;
                    }

                    if (retry == options.MaxRetries - 1)
                    {
                        var rate = batch.Count == 0 ? 0d : (double)translatedBatch.Count / batch.Count;
                        _logger.LogWarning($"Batch {batchIndex + 1} translation parse rate low: {rate:P}");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error translating batch {batchIndex + 1} (retry {retry + 1}/{options.MaxRetries})");

                    if (retry == options.MaxRetries - 1)
                    {
                        translatedBatch = batch;
                        _logger.LogWarning($"Batch {batchIndex + 1} failed; keeping original text.");
                    }
                    else
                    {
                        var delay = options.RetryBaseDelayMs * (retry + 1);
                        if (delay > 0)
                        {
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }

            translatedSubtitles.AddRange(translatedBatch ?? batch);

            if (options.BatchDelayMs > 0)
            {
                await Task.Delay(options.BatchDelayMs, cancellationToken).ConfigureAwait(false);
            }

            if (progress is not null)
            {
                var bucket = (progressPercent / 5) * 5;
                if (bucket >= lastReported + 5 || progressPercent == 100)
                {
                    lastReported = bucket;
                    progress(bucket, $"batch {batchIndex + 1}/{totalBatches}");
                }
            }
        }

        progress?.Invoke(100, "done");

        _logger.LogInformation($"Token usage total: input={allInputTokens}, output={allOutputTokens}");

        return translatedSubtitles;
    }

    private Task<ChatResponse> GetResponseAsync(IReadOnlyList<ChatMessage> messages, TranslationOptions options, CancellationToken cancellationToken)
    {
        // Default: use Microsoft.Extensions.AI ChatOptions.
        // For `VllmGptOssChatClient`, use `GptOssChatOptions` (known working pattern from integration test).
        var clientTypeName = _chatClient.GetType().Name;
        if (string.Equals(clientTypeName, "VllmGptOssChatClient", StringComparison.Ordinal))
        {
            var gptOssOptions = new GptOssChatOptions
            {
                ReasoningLevel = GptOssReasoningLevel.Low,
                Temperature = 0.5f
            };

            return _chatClient.GetResponseAsync(messages, gptOssOptions, cancellationToken);
        }

        var chatOptions = new ChatOptions
        {
            Temperature = options.Temperature,
            MaxOutputTokens = options.MaxOutputTokens
        };

        return _chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);
    }

    private sealed record SrtTranslationItem(int Index, string Text);

    public sealed class SrtSubtitle
    {
        public int Index { get; set; }
        public string StartTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    public static List<SrtSubtitle> ParseSrtFile(string content)
    {
        var subtitles = new List<SrtSubtitle>();
        using var sr = new StringReader(content);

        SrtSubtitle? currentSubtitle = null;
        var isReadingText = false;

        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrEmpty(trimmedLine))
            {
                if (currentSubtitle != null && !string.IsNullOrEmpty(currentSubtitle.Text))
                {
                    subtitles.Add(currentSubtitle);
                    currentSubtitle = null;
                    isReadingText = false;
                }
                continue;
            }

            if (int.TryParse(trimmedLine, out var index) && !isReadingText)
            {
                if (currentSubtitle != null && !string.IsNullOrEmpty(currentSubtitle.Text))
                {
                    subtitles.Add(currentSubtitle);
                }

                currentSubtitle = new SrtSubtitle { Index = index };
                isReadingText = false;
                continue;
            }

            if (currentSubtitle != null && string.IsNullOrEmpty(currentSubtitle.StartTime) && TryParseTimestampLine(trimmedLine, out var start, out var end))
            {
                currentSubtitle.StartTime = start;
                currentSubtitle.EndTime = end;
                isReadingText = true;
                continue;
            }

            if (isReadingText && currentSubtitle != null)
            {
                currentSubtitle.Text = string.IsNullOrEmpty(currentSubtitle.Text)
                    ? trimmedLine
                    : currentSubtitle.Text + "\n" + trimmedLine;
            }
        }

        if (currentSubtitle != null && !string.IsNullOrEmpty(currentSubtitle.Text))
        {
            subtitles.Add(currentSubtitle);
        }

        return subtitles;
    }

    private static bool TryParseTimestampLine(string line, out string start, out string end)
    {
        start = string.Empty;
        end = string.Empty;

        var arrowIndex = line.IndexOf("-->", StringComparison.Ordinal);
        if (arrowIndex < 0)
        {
            return false;
        }

        var left = line[..arrowIndex].Trim();
        var right = line[(arrowIndex + 3)..].Trim();

        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return false;
        }

        var rightParts = right.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        start = left;
        end = rightParts.Length > 0 ? rightParts[0].Trim() : right;
        return true;
    }

    private static List<SrtSubtitle> GetContextualBatch(IReadOnlyList<SrtSubtitle> allSubtitles, int batchIndex, int batchSize, int contextSize)
    {
        var startIndex = Math.Max(0, batchIndex * batchSize - contextSize);
        var endIndex = Math.Min(allSubtitles.Count - 1, (batchIndex + 1) * batchSize + contextSize - 1);

        return allSubtitles.Skip(startIndex).Take(endIndex - startIndex + 1).ToList();
    }

    private static string BuildTranslationPrompt(IReadOnlyList<SrtSubtitle> contextBatch, IReadOnlyList<SrtSubtitle> targetBatch, string targetLanguage)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine($"请将【目标字幕】翻译成{targetLanguage}。上下文用于理解语境，但不要翻译上下文。");
        prompt.AppendLine();
        prompt.AppendLine("输出要求（非常重要）：");
        prompt.AppendLine("1) 只输出JSON数组，不要任何解释文字");
        prompt.AppendLine("2) 每个元素格式：{\"index\":数字,\"text\":\"翻译\"}");
        prompt.AppendLine("3) index 必须与输入一致，不要新增/删除条目，不要合并/拆分条目");
        prompt.AppendLine("4) text 允许包含换行，换行请使用\\n表示");
        prompt.AppendLine();

        prompt.AppendLine("【上下文字幕（仅参考，不翻译）】：");
        foreach (var subtitle in contextBatch.Where(s => !targetBatch.Any(t => t.Index == s.Index)))
        {
            prompt.AppendLine($"{subtitle.Index}: {subtitle.Text}");
        }

        prompt.AppendLine();
        prompt.AppendLine("【目标字幕（需要翻译）】：");
        foreach (var subtitle in targetBatch)
        {
            prompt.AppendLine($"{subtitle.Index}: {subtitle.Text}");
        }

        return prompt.ToString();
    }

    private List<SrtSubtitle> ParseTranslationResult(string translatedText, List<SrtSubtitle> originalBatch)
    {
        var parsed = TryParseJsonTranslation(translatedText, originalBatch);
        if (parsed != null)
        {
            return parsed;
        }

        return ParseColonTranslation(translatedText, originalBatch);
    }

    private List<SrtSubtitle>? TryParseJsonTranslation(string translatedText, List<SrtSubtitle> originalBatch)
    {
        try
        {
            var json = ExtractJsonArray(translatedText);
            if (json == null)
            {
                return null;
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var items = JsonSerializer.Deserialize<List<SrtTranslationItem>>(json, options);
            if (items == null || items.Count == 0)
            {
                return null;
            }

            var result = new List<SrtSubtitle>();
            foreach (var item in items)
            {
                var originalSubtitle = originalBatch.FirstOrDefault(s => s.Index == item.Index);
                if (originalSubtitle == null)
                {
                    continue;
                }

                var normalizedText = (item.Text ?? string.Empty)
                    .Replace("\\r\\n", "\\n")
                    .Replace("\\r", "\\n");

                normalizedText = normalizedText.Replace("\\\\n", "\n");

                result.Add(new SrtSubtitle
                {
                    Index = originalSubtitle.Index,
                    StartTime = originalSubtitle.StartTime,
                    EndTime = originalSubtitle.EndTime,
                    Text = normalizedText.Trim()
                });
            }

            if (result.Count == 0)
            {
                return null;
            }

            foreach (var original in originalBatch)
            {
                if (!result.Any(r => r.Index == original.Index))
                {
                    result.Add(original);
                }
            }

            return result.OrderBy(s => s.Index).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to parse JSON translation; falling back. {ex.Message}");
            return null;
        }
    }

    private static string? ExtractJsonArray(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf('[', StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var end = text.LastIndexOf(']');
        if (end <= start)
        {
            return null;
        }

        return text[start..(end + 1)].Trim();
    }

    private List<SrtSubtitle> ParseColonTranslation(string translatedText, List<SrtSubtitle> originalBatch)
    {
        var result = new List<SrtSubtitle>();
        var lines = translatedText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine))
            {
                continue;
            }

            var match = Regex.Match(trimmedLine, @"^(\d+):\s*(.+)$");
            if (!match.Success)
            {
                continue;
            }

            var index = int.Parse(match.Groups[1].Value);
            var translatedContent = match.Groups[2].Value.Trim();

            var originalSubtitle = originalBatch.FirstOrDefault(s => s.Index == index);
            if (originalSubtitle == null)
            {
                continue;
            }

            result.Add(new SrtSubtitle
            {
                Index = originalSubtitle.Index,
                StartTime = originalSubtitle.StartTime,
                EndTime = originalSubtitle.EndTime,
                Text = translatedContent
            });
        }

        if (result.Count == 0)
        {
            _logger.LogWarning("Translation parse failed; returning original batch.");
            return originalBatch;
        }

        foreach (var original in originalBatch)
        {
            if (!result.Any(r => r.Index == original.Index))
            {
                result.Add(original);
            }
        }

        return result.OrderBy(s => s.Index).ToList();
    }

    public static string GenerateSrtContent(IReadOnlyList<SrtSubtitle> subtitles)
    {
        var sb = new StringBuilder();

        foreach (var subtitle in subtitles.OrderBy(s => s.Index))
        {
            sb.AppendLine(subtitle.Index.ToString());
            sb.AppendLine($"{subtitle.StartTime} --> {subtitle.EndTime}");
            sb.AppendLine(subtitle.Text);
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
