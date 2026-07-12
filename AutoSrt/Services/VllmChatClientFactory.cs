using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.Gemma;
using Microsoft.Extensions.AI.VllmChatClient.GptOss;
using Microsoft.Extensions.AI.VllmChatClient.Mimo;

namespace AutoSrt.Services;

internal static class VllmChatClientFactory
{
    public static IChatClient Create(string endpoint, string apiKey, string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required.", nameof(model));
        }

        endpoint = NormalizeEndpoint(endpoint);
        model = model.Trim();
        var modelName = GetModelName(model);

        return modelName switch
        {
            _ when modelName.StartsWith("gpt-oss-", StringComparison.Ordinal) =>
                new VllmGptOssChatClient(endpoint, apiKey, model),
            _ when modelName.StartsWith("gpt-", StringComparison.Ordinal) =>
                new VllmOpenAiGptClient(endpoint, apiKey, model),
            _ when modelName.StartsWith("claude-", StringComparison.Ordinal) =>
                new VllmClaudeChatClient(endpoint, apiKey, model),
            _ when modelName.Contains("nemotron-3", StringComparison.Ordinal) =>
                new VllmNemotronChatClient(endpoint, apiKey, model),

            _ when modelName.StartsWith("qwen3-next-", StringComparison.Ordinal)
                || modelName.StartsWith("qwen3.5", StringComparison.Ordinal)
                || modelName.StartsWith("qwen3.6", StringComparison.Ordinal)
                || modelName.StartsWith("qwen3-vl-", StringComparison.Ordinal) =>
                new VllmQwen3NextChatClient(endpoint, apiKey, model),
            _ when modelName.StartsWith("qwen3", StringComparison.Ordinal) =>
                new VllmQwen3ChatClient(endpoint, apiKey, model),
            _ when modelName.StartsWith("qwq", StringComparison.Ordinal) =>
                new VllmQwqChatClient(endpoint, apiKey, model),

            _ when modelName.StartsWith("gemma-4", StringComparison.Ordinal)
                || modelName.StartsWith("gemma4", StringComparison.Ordinal) =>
                new VllmGemma4ChatClient(endpoint, apiKey, model),
            _ when modelName.StartsWith("gemma-3", StringComparison.Ordinal)
                || modelName.StartsWith("gemma3", StringComparison.Ordinal) =>
                new VllmGemmaChatClient(endpoint, apiKey, model),
            _ when modelName.StartsWith("gemini-3", StringComparison.Ordinal) =>
                new VllmGemini3ChatClient(endpoint, apiKey, model),

            _ when modelName.StartsWith("deepseek-r1", StringComparison.Ordinal) =>
                new VllmDeepseekR1ChatClient(endpoint, apiKey, model),
            _ when modelName.StartsWith("deepseek-v3", StringComparison.Ordinal)
                || modelName.StartsWith("deepseek-v4", StringComparison.Ordinal) =>
                new VllmDeepseekV3ChatClient(endpoint, apiKey, model),
            _ when modelName.StartsWith("kimi-k2", StringComparison.Ordinal) =>
                new VllmKimiK2ChatClient(endpoint, apiKey, model),
            _ when modelName.StartsWith("glm-", StringComparison.Ordinal) =>
                new VllmGlmChatClient(endpoint, apiKey, model),
            _ when modelName.StartsWith("minimax-m2", StringComparison.Ordinal) =>
                new VllmMiniMaxChatClient(endpoint, apiKey, model),
            _ when modelName.StartsWith("mimo-v2-", StringComparison.Ordinal) =>
                new VllmMimoChatClient(endpoint, apiKey, model),

            _ => throw new NotSupportedException($"Unsupported model: {model}")
        };
    }

    private static string GetModelName(string model)
    {
        var separatorIndex = model.LastIndexOf('/');
        return model[(separatorIndex + 1)..].ToLowerInvariant();
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        endpoint = (endpoint ?? string.Empty).Trim();

        // Keep placeholder-style endpoints such as:
        // https://dashscope.aliyuncs.com/compatible-mode/v1/{1}
        // https://openrouter.ai/api/v1/{1}
        // Some clients use the placeholder to compose the final chat path.
        if (endpoint.Contains("{1}", StringComparison.Ordinal)
            || endpoint.Contains("{0}", StringComparison.Ordinal))
        {
            return endpoint;
        }

        endpoint = endpoint.TrimEnd('/');
        return endpoint;
    }
}
