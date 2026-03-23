using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.VllmChatClient.Gemma;
using Microsoft.Extensions.AI.VllmChatClient.GptOss;

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


        switch (model.ToLower())
        {
            case "qwen3.5-plus" or "qwen3-next-80b-a3b-instruct" or "qwen/qwen3.5-397b-a17b" or "qwen3.5-397b-a17b" or "qwen3.5-122b-a10b":
                return  new VllmQwen3NextChatClient(endpoint, apiKey, model);
            case "openai/gpt-oss-120b":
                return new VllmGptOssChatClient(endpoint, apiKey, model);

            case "openai/gpt-5.3-codex":
                return new VllmOpenAiGptClient(endpoint, apiKey, model);

            case "google/gemini-3.1-pro-preview" or "google/gemini-3-flash-preview" or "gemini-3.1-flash-lite-preview" or "gemini-3.1-pro-preview" or "gemini-3-flash-preview":
                return new VllmGemini3ChatClient(endpoint, apiKey, model);

            case "anthropic/claude-opus-4.6":
                return new VllmClaudeChatClient(endpoint, apiKey, model);

            case "kimi-k2.5":
                return new VllmKimiK2ChatClient(endpoint, apiKey, model);

            case "minimax-m2.5" or "minimax-m2.7" or "minimax/minimax-m2.7":
                return new VllmMiniMaxChatClient(endpoint, apiKey, model);

            case "glm-5" or "glm-4.7":
                return new VllmGlmChatClient(endpoint, apiKey, model);

            case "openai/gpt-oss-120b" or "openai/gpt-oss-20b":
                return new VllmGptOssChatClient(endpoint, apiKey, model);

            default:
                return new VllmQwen3NextChatClient(endpoint, apiKey, model);
        }

        throw new NotSupportedException($"Unsupported model: {model}");
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
