using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using Xunit;

namespace SrtAgent.Tests;

public sealed class SrtAgentTests
{
    private readonly ITestOutputHelper _output;
    private IChatClient? _chatClient;

    public SrtAgentTests(ITestOutputHelper output)
    {
        _output = output;

        var apiKey = Environment.GetEnvironmentVariable("VLLM_API_KEY");
        var endpoint = Environment.GetEnvironmentVariable("VLLM_ENDPOINT") ?? "http://localhost:8000/v1/{1}";
        var model = Environment.GetEnvironmentVariable("VLLM_MODEL") ?? "qwen3-next-80b-a3b-instruct";

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _chatClient = new VllmQwen3NextChatClient(endpoint, apiKey, model);
        }
    }

    [Fact]
    public async Task TranslateSrtAsync_WithStubClient_TranslatesAndReturnsValidSrt()
    {
        var input =
            "1\n" +
            "00:00:01,000 --> 00:00:02,000\n" +
            "Hello\n\n" +
            "2\n" +
            "00:00:03,000 --> 00:00:04,000\n" +
            "World\n";

        var client = new StubChatClient(
            // JSON response expected by ParseTranslationResult
            "[{\"index\":1,\"text\":\"\u4f60\u597d\"},{\"index\":2,\"text\":\"\u4e16\u754c\"}]"
        );

        var agent = new SrtTranslator(client, NullLogger.Instance);
        var progressUpdates = new List<int>();

        var output = await agent.TranslateSrtAsync(
            input,
            new SrtTranslator.TranslationOptions(TargetLanguage: "中文", BatchSize: 1, BatchDelayMs: 0),
            (percent, _) => progressUpdates.Add(percent));

        Assert.Contains("1", output);
        Assert.Contains("00:00:01,000 --> 00:00:02,000", output);
        Assert.Contains("你好", output);
        Assert.Contains("世界", output);
        var requestOptions = Assert.IsType<VllmChatOptions>(client.LastOptions);
        Assert.False(requestOptions.ThinkingEnabled);
        Assert.False(requestOptions.EnableSkills);
        Assert.Equal(2000, requestOptions.MaxOutputTokens);
        Assert.Equal([0, 50, 100, 100], progressUpdates);
    }

    [Fact]
    public async Task TranslateSrtAsync_WhenRequestTimesOut_RetriesAndDoesNotHang()
    {
        const string input = "1\n00:00:01,000 --> 00:00:02,000\nHello\n";
        var client = new HangingChatClient();
        var agent = new SrtTranslator(client, NullLogger.Instance);
        var stopwatch = Stopwatch.StartNew();

        var output = await agent.TranslateSrtAsync(
            input,
            new SrtTranslator.TranslationOptions(
                TargetLanguage: "中文",
                MaxRetries: 2,
                RetryBaseDelayMs: 0,
                BatchDelayMs: 0,
                RequestTimeoutMs: 20));

        stopwatch.Stop();
        Assert.Equal(2, client.CallCount);
        Assert.Contains("Hello", output);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void ParseSrtFile_WithMultilineText_ProducesSingleSubtitleWithNewline()
    {
        var input =
            "1\n" +
            "00:00:01,000 --> 00:00:02,000\n" +
            "Line1\n" +
            "Line2\n\n";

        var subs = SrtTranslator.ParseSrtFile(input);

        Assert.Single(subs);
        Assert.Equal(1, subs[0].Index);
        Assert.Equal("Line1\nLine2", subs[0].Text);
    }

    [Fact]
    public async Task TranslateSrtAsync_WithEmptyInput_Throws()
    {
        var agent = new SrtTranslator(new StubChatClient("[]"), NullLogger.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => agent.TranslateSrtAsync("   "));
    }

    [Fact]
    public async Task TranslateSrtAsync_WithVllmClient_TranslatesToChinese()
    {
        if (_chatClient is null)
        {
            _output.WriteLine("Skipping integration test: env var VLLM_API_KEY is not set.");
            return;
        }

        var input =
            "1\n" +
            "00:00:01,000 --> 00:00:02,000\n" +
            "Hello world!\n\n" +
            "2\n" +
            "00:00:03,000 --> 00:00:04,000\n" +
            "Good morning.\n";

        var agent = new SrtTranslator(_chatClient, NullLogger.Instance);

        // Keep it small/fast and avoid delays.
        var options = new SrtTranslator.TranslationOptions(
            TargetLanguage: "中文",
            BatchSize: 2,
            ContextSize: 0,
            Temperature: 0.0f,
            MaxOutputTokens: 600,
            MaxRetries: 2,
            RetryBaseDelayMs: 500,
            BatchDelayMs: 0);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var output = await agent.TranslateSrtAsync(
            input,
            options,
            progress: null,
            cancellationToken: cts.Token);

        _output.WriteLine(output);

        Assert.Contains("-->", output);
        Assert.Contains("1", output);
        Assert.Contains("2", output);

        // Heuristic: ensure at least one CJK char appears, indicating likely Chinese translation.
        Assert.Matches("[\u4e00-\u9fff]", output);
    }

    private sealed class StubChatClient : IChatClient
    {
        private readonly string _responseText;

        public StubChatClient(string responseText)
        {
            _responseText = responseText;
        }

        public object? Metadata => null;

        public ChatOptions? LastOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, _responseText));
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Not used by SrtAgent; keep a minimal implementation for interface compliance.
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class HangingChatClient : IChatClient
    {
        public int CallCount { get; private set; }

        public object? Metadata => null;

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
