using System.Net.Http;
using System.Text;
using System.Text.Json;
using CodexCapture.Models;

namespace CodexCapture.Services;

public sealed class AiRequestFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public PreparedAiRequest CreateTranslationRequest(
        ProviderProfile provider,
        string apiKey,
        string imagePath,
        string targetLanguage)
    {
        var endpoint = provider.ProtocolMode == AiProtocolMode.Responses
            ? CombineUrl(provider.BaseUrl, "responses")
            : CombineUrl(provider.BaseUrl, "chat/completions");

        var prompt =
            $"请识别截图里的可读文字，判断原文语言，并翻译成{targetLanguage}。请只返回原文语言和译文。";

        var imageBytes = File.ReadAllBytes(imagePath);
        var base64 = Convert.ToBase64String(imageBytes);
        var imageValue = provider.ImagePayloadMode == ImagePayloadMode.RawBase64
            ? base64
            : "data:image/png;base64," + base64;
        var payload = provider.ProtocolMode == AiProtocolMode.Responses
            ? CreateResponsesPayload(provider.Model, prompt, imageValue)
            : CreateChatCompletionsPayload(provider.Model, prompt, imageValue);

        var json = JsonSerializer.Serialize(payload, JsonOptions);

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        foreach (var header in provider.CustomHeaders)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return new PreparedAiRequest(
            request,
            endpoint,
            provider.ProtocolMode,
            provider.ImagePayloadMode,
            imageBytes.Length,
            json.Length);
    }

    private static object CreateResponsesPayload(string model, string prompt, string imageValue) => new
    {
        model,
        input = new object[]
        {
            new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "input_text", text = prompt },
                    new { type = "input_image", image_url = imageValue }
                }
            }
        }
    };

    private static object CreateChatCompletionsPayload(string model, string prompt, string imageValue) => new
    {
        model,
        messages = new object[]
        {
            new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "text", text = prompt },
                    new { type = "image_url", image_url = new { url = imageValue } }
                }
            }
        }
    };

    private static string CombineUrl(string baseUrl, string endpoint)
    {
        var trimmed = baseUrl.TrimEnd('/');
        if (trimmed.EndsWith("/responses", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"{trimmed}/{endpoint}";
    }

    public static string? ExtractText(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("output_text", out var outputText))
        {
            return outputText.GetString();
        }

        if (document.RootElement.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content))
            {
                return ExtractContentText(content);
            }
        }

        if (document.RootElement.TryGetProperty("output", out var output))
        {
            return ExtractContentText(output);
        }

        return null;
    }

    private static string? ExtractContentText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                var text = ExtractContentText(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text);
                }
            }

            return parts.Count > 0 ? string.Join(Environment.NewLine, parts) : null;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("text", out var text))
            {
                return ExtractContentText(text);
            }

            if (element.TryGetProperty("content", out var content))
            {
                return ExtractContentText(content);
            }
        }

        return null;
    }
}

public sealed record PreparedAiRequest(
    HttpRequestMessage Request,
    string Endpoint,
    AiProtocolMode ProtocolMode,
    ImagePayloadMode ImagePayloadMode,
    long ImageBytes,
    int JsonBytes) : IDisposable
{
    public void Dispose() => Request.Dispose();
}
