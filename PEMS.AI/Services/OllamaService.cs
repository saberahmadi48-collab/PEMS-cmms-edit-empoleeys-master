using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using PEMS.AI.Interfaces;
using PEMS.AI.Models;

namespace PEMS.AI.Services;

public class OllamaService : IOllamaService
{
    private readonly HttpClient _httpClient;
    private readonly string _defaultModel;
    private readonly string _embeddingModel;

    public OllamaService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _defaultModel = configuration["Ollama:Model"] ?? throw new InvalidOperationException("Missing required setting 'Ollama:Model' (Ollama__Model in env/<name>.env).");
        _embeddingModel = configuration["Ollama:EmbeddingModel"] ?? throw new InvalidOperationException("Missing required setting 'Ollama:EmbeddingModel' (Ollama__EmbeddingModel in env/<name>.env).");
    }

    public async Task<string> GenerateAsync(string prompt)
    {
        return await GenerateAsync(prompt, new OllamaGenerateOptions());
    }

    public async Task<string> GenerateAsync(string prompt, OllamaGenerateOptions options)
    {
        var request = new OllamaRequest
        {
            Prompt = prompt,
            Model = string.IsNullOrWhiteSpace(options.Model) ? _defaultModel : options.Model,
            Format = options.JsonMode ? "json" : null,
            Options = new OllamaRequestOptions { Temperature = options.Temperature, NumPredict = options.MaxResponseTokens }
        };

        var response = await _httpClient.PostAsJsonAsync("api/generate", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaResponse>();
        return result?.Response ?? "";
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        // متن‌های خیلی طولانی را کوتاه می‌کنیم چون هدف Semantic Search است، نه بازتولید کامل متن
        var trimmed = text.Length > 4000 ? text[..4000] : text;

        var request = new OllamaEmbeddingRequest
        {
            Model = _embeddingModel,
            Prompt = trimmed
        };

        var response = await _httpClient.PostAsJsonAsync("api/embeddings", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>();
        return result?.Embedding ?? Array.Empty<float>();
    }
}
