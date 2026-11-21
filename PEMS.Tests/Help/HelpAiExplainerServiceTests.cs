using Microsoft.Extensions.Configuration;
using PEMS.AI.Services;
using PEMS.Domain.Entities;
using PEMS.Persistence.Services;
using PEMS.Tests.TestSupport;

namespace PEMS.Tests.Help;

/// <summary>
/// طبق «PEMS Help System — Phase 2» بخش ۱۰: تست واقعی (نه Mock شده) رفتار
/// HelpAiExplainerService روی همین محیط توسعه، جایی که Ollama واقعاً در دسترس نیست (تایید‌شده
/// با curl مستقیم به http://localhost:11434 قبل از نوشتن این تست - HTTP 000، اتصال رد شد).
/// این دقیقاً همان سناریویی است که سند می‌خواهد بررسی شود: «Whether the feature works when
/// Ollama is unavailable» - این‌جا یک ادعای بدون تست نیست، یک وضعیت واقعی و تکرارپذیر است.
/// </summary>
public class HelpAiExplainerServiceTests
{
    private static (PEMS.Persistence.Context.PEMSDbContext ctx, HelpAiExplainerService svc) CreateService(string dbName)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"HelpAi_{dbName}");

        // همان HttpClient/Config واقعی OllamaService - بدون Mock. آدرس پیش‌فرض
        // (localhost:11434) در این محیط توسعه واقعاً بدون پاسخ است (تایید‌شده با curl)
        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:11434/") };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ollama:Model"] = "test-model",
            ["Ollama:EmbeddingModel"] = "test-embedding-model"
        }).Build();
        var ollama = new OllamaService(httpClient, config);

        var svc = new HelpAiExplainerService(ctx, ollama);
        return (ctx, svc);
    }

    private static async Task<HelpArticle> SeedArticleAsync(PEMS.Persistence.Context.PEMSDbContext ctx, string code)
    {
        var cat = new HelpCategory { Code = "engineering", NameEn = "Engineering", NameFa = "مهندسی", DisplayOrder = 0, IsActive = true };
        ctx.HelpCategories.Add(cat);
        await ctx.SaveChangesAsync();

        var article = new HelpArticle
        {
            Code = code,
            HelpCategoryId = cat.HelpCategoryId,
            ArticleType = "Workflow",
            IsPublished = true,
            CreatedDate = DateTime.Now
        };
        article.Translations.Add(new HelpArticleTranslation
        {
            LanguageCode = "en", Title = "IDC", Summary = "Inter-Discipline Check", Content = "<p>IDC is advisory.</p>"
        });
        article.Translations.Add(new HelpArticleTranslation
        {
            LanguageCode = "fa", Title = "IDC", Summary = "بازبینی بین‌دیسیپلینی", Content = "<p>IDC مشورتی است.</p>"
        });
        ctx.HelpArticles.Add(article);
        await ctx.SaveChangesAsync();
        return article;
    }

    [Fact]
    public async Task ExplainAsync_OllamaUnavailable_ReturnsGracefulEnglishMessage_DoesNotThrow()
    {
        var (ctx, svc) = CreateService(nameof(ExplainAsync_OllamaUnavailable_ReturnsGracefulEnglishMessage_DoesNotThrow));
        var article = await SeedArticleAsync(ctx, "ai-test-1");

        var answer = await svc.ExplainAsync(article.Code, "What is this page used for?", "en", userId: null);

        Assert.False(string.IsNullOrWhiteSpace(answer));
        Assert.Contains("Ollama", answer);
        Assert.DoesNotContain("Exception", answer); // پیام باید برای کاربر خوانا باشد، نه Stack Trace خام
    }

    [Fact]
    public async Task ExplainAsync_OllamaUnavailable_ReturnsGracefulPersianMessage_DoesNotThrow()
    {
        var (ctx, svc) = CreateService(nameof(ExplainAsync_OllamaUnavailable_ReturnsGracefulPersianMessage_DoesNotThrow));
        var article = await SeedArticleAsync(ctx, "ai-test-2");

        var answer = await svc.ExplainAsync(article.Code, "این صفحه چه کاری انجام می‌دهد؟", "fa", userId: null);

        Assert.False(string.IsNullOrWhiteSpace(answer));
        Assert.Contains("هوش مصنوعی", answer);
    }

    [Fact]
    public async Task ExplainAsync_UnknownArticleCode_StillDegradesGracefully_DoesNotThrow()
    {
        var (ctx, svc) = CreateService(nameof(ExplainAsync_UnknownArticleCode_StillDegradesGracefully_DoesNotThrow));

        var answer = await svc.ExplainAsync("no-such-article-code", "Why can't I close this work order?", "en", userId: null);

        Assert.False(string.IsNullOrWhiteSpace(answer));
    }

    [Fact]
    public async Task ExplainAsync_NoArticleContext_StillDegradesGracefully_DoesNotThrow()
    {
        // طبق بخش ۱۱ سند فاز ۱: وقتی هیچ مقاله‌ای باز نیست، AI نباید خطا بدهد - باید یا از
        // دانش عمومیِ محدود پاسخ دهد یا بگوید مستند نشده؛ این‌جا چون Ollama در دسترس نیست،
        // مسیر Catch باید همچنان پیام خوانا برگرداند
        var (ctx, svc) = CreateService(nameof(ExplainAsync_NoArticleContext_StillDegradesGracefully_DoesNotThrow));

        var answer = await svc.ExplainAsync(null, "What is the difference between a functional location and equipment?", "en", userId: null);

        Assert.False(string.IsNullOrWhiteSpace(answer));
    }

    [Fact]
    public async Task ExplainAsync_EmptyQuestion_ReturnsPromptForQuestion_WithoutCallingOllama()
    {
        var (ctx, svc) = CreateService(nameof(ExplainAsync_EmptyQuestion_ReturnsPromptForQuestion_WithoutCallingOllama));

        var answerEn = await svc.ExplainAsync(null, "", "en", userId: null);
        var answerFa = await svc.ExplainAsync(null, "   ", "fa", userId: null);

        Assert.Equal("Please enter a question.", answerEn);
        Assert.Equal("لطفاً سوال خود را بنویسید.", answerFa);
    }
}
