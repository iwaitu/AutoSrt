using Microsoft.Extensions.AI;
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

        // Keep mapping strict to the options exposed in UI.
        if (model.StartsWith("qwen/", StringComparison.OrdinalIgnoreCase))
        {
            return new VllmQwen3NextChatClient(endpoint, apiKey, model);
        }

        if (model.Equals("openai/gpt-oss-120b", StringComparison.OrdinalIgnoreCase)
            || model.Equals("openai/gpt-oss-20b", StringComparison.OrdinalIgnoreCase))
        {
            return new VllmGptOssChatClient(endpoint, apiKey, model);
        }

        throw new NotSupportedException($"Unsupported model: {model}");
    }

    private static string NormalizeEndpoint(string endpoint)
    {
        endpoint = (endpoint ?? string.Empty).Trim();

        // UI uses templates like https://openrouter.ai/api/v1/{1}
        // The vLLM/OpenAI-compatible clients expect a concrete base URL (typically .../v1).
        if (endpoint.Contains("{1}", StringComparison.Ordinal))
        {
            endpoint = endpoint.Replace("{1}", string.Empty, StringComparison.Ordinal);
        }

        endpoint = endpoint.TrimEnd('/');
        return endpoint;
    }
}
