using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// Semantic Search / RAG واقعی طبق AI Architecture (Volume VIII - Bible): بدون Vector Database
/// جداگانه، Embedding ها محلی (Ollama) تولید و در همان SQL Server نگه‌داری می‌شوند.
/// </summary>
public interface IKnowledgeBaseService
{
    Task IndexAsync(string sourceType, int sourceId, string content);

    [PEMS.Application.Common.PemsOperation("KnowledgeBase", "Search")]   // SCRUM-712: the semantic search page is a data source over this method
    Task<List<KnowledgeBaseSearchResultDto>> SearchAsync(string query, int topK = 5);

    Task<int> GetEntryCountAsync();
}
