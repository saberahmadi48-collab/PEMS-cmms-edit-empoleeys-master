using PEMS.Application.DTOs;
using PEMS.Application.Interfaces;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;
using PEMS.Persistence.Identity;
using PEMS.Persistence.Services;
using Microsoft.AspNetCore.Identity;

namespace PEMS.Tests.TestSupport;

/// <summary>کش بی‌اثر: هر بار Factory را اجرا می‌کند (تا تست‌ها به داده‌ی کهنه‌ی کش وابسته نشوند).</summary>
public sealed class NoCache : ICacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null) => factory();
    public Task RemoveAsync(string key) => Task.CompletedTask;
}

/// <summary>INotificationService ثبت‌کننده برای تست سرویس‌های نامه.</summary>
public sealed class LetterTestNotifications : INotificationService
{
    public bool Throw { get; set; }
    public List<(int UserId, string Title, string Message, string? Link, int? RelatedId)> Sent { get; } = new();

    public Task CreateAsync(int userId, string title, string message, string? linkUrl = null, string? relatedType = null, int? relatedId = null)
    {
        if (Throw) throw new InvalidOperationException("notification store down");
        Sent.Add((userId, title, message, linkUrl, relatedId));
        return Task.CompletedTask;
    }

    public Task<List<NotificationDto>> GetForUserAsync(int userId, bool unreadOnly = false) => throw new NotImplementedException();
    public Task<PagedResult<NotificationDto>> GetInboxAsync(int userId, string? filter, int page, int pageSize) => throw new NotImplementedException();
    public Task<int> GetUnreadCountAsync(int userId) => throw new NotImplementedException();
    public Task<bool> MarkAsReadAsync(int userId, int notificationId) => throw new NotImplementedException();
    public Task MarkAllAsReadAsync(int userId) => throw new NotImplementedException();
    public Task<string?> OpenAsync(int userId, int notificationId) => throw new NotImplementedException();
    public Task<bool> DismissAsync(int userId, int notificationId) => throw new NotImplementedException();
    public Task DismissAllAsync(int userId) => throw new NotImplementedException();
    public Task MarkRelatedReadAsync(int userId, string relatedType, int relatedId) => throw new NotImplementedException();
    public Task ResolveRelatedAsync(int? userId, string relatedType, int relatedId) => throw new NotImplementedException();
}

/// <summary>
/// دادهٔ پایه‌ی تست‌های نوشتن/تایید نامه: دپارتمان ENG، هفت کارمند فعال (E1..E7) با کاربر هم‌شماره (101..107)،
/// نقش «Reviewers» که Approve ماژول Correspondence دارد و کاربران ۱۰۶/۱۰۷/۱۰۵ عضو آن‌اند (۱۰۱ نویسنده عضو نیست).
/// </summary>
public static class LetterTestWorld
{
    public const int Author = 101, To1 = 102, Cc1 = 103, Bcc1 = 104, Other = 105, Approver1 = 106, Approver2 = 107;

    public static PEMSDbContext NewContext(string name)
    {
        var ctx = PemsDbContextFactory.CreateInMemory($"LetterWorld_{name}_{Guid.NewGuid():N}");

        ctx.Departments.Add(new Department { DepartmentId = 1, Code = "eng-01", Name = "Engineering" });
        ctx.Positions.Add(new Position { PositionId = 1, Title = "Lead Engineer" });

        for (var i = 1; i <= 7; i++)
        {
            ctx.Employees.Add(new Employee
            {
                EmployeeId = i, FirstName = "E" + i, LastName = "L" + i, IsActive = true, DepartmentId = 1, PositionId = i == 1 ? 1 : null,
                PersonnelCode = "P" + i, Email = $"e{i}@pems.test", Phone = "0" + i
            });
            ctx.Users.Add(new ApplicationUser { Id = 100 + i, UserName = "u" + i, NormalizedUserName = "U" + i, EmployeeId = i, IsActive = true, FullName = "User " + i, Email = $"u{i}@pems.test" });
        }

        ctx.Roles.Add(new ApplicationRole("Reviewers") { Id = 1, NormalizedName = "REVIEWERS" });
        ctx.RolePermissions.Add(new RolePermission { RoleName = "Reviewers", ModuleName = "Correspondence", CanApprove = true });
        foreach (var u in new[] { Other, Approver1, Approver2 })
            ctx.UserRoles.Add(new IdentityUserRole<int> { UserId = u, RoleId = 1 });

        ctx.SaveChanges();
        return ctx;
    }

    public static LetterComposeActor AuthorActor(bool manualNumber = false) => new(Author, "u1", 1, manualNumber, false);

    public static LetterComposeInput ValidInput(string type = "Internal") => new()
    {
        LetterType = type,
        Category = "Request",
        Subject = "Quarterly maintenance schedule",
        Body = "<p>Please review the attached schedule.</p>",
        ToEmployeeIds = type == "Internal" ? new List<int> { 2 } : new List<int>(),
        ExternalReceiver = type == "Outgoing" ? "ACME Ltd" : null
    };

    public static (LetterComposeService Compose, LetterService Letters, PEMSDbContext Ctx, LetterTestNotifications Notes) Services(string name)
    {
        var ctx = NewContext(name);
        var notes = new LetterTestNotifications();
        var sanitizer = new RichTextSanitizerService();
        var numbering = new LetterNumberingService(ctx);
        return (new LetterComposeService(ctx, sanitizer, numbering, new NoCache()),
            new LetterService(ctx, sanitizer, knowledgeBaseService: null!, notes), ctx, notes);
    }
}
