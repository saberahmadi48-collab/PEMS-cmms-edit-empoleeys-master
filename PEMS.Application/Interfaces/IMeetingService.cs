using PEMS.Application.DTOs;

namespace PEMS.Application.Interfaces;

/// <summary>
/// مدیریت جلسات (طبق نمونه‌ی کارتابل مرجع) - اقدامات پیگیری هر جلسه از همان ITaskService
/// موجود استفاده می‌کنند (RelatedType="Meeting")، نه یک سیستم Task جدا.
/// </summary>
public interface IMeetingService
{
    Task<List<MeetingDto>> GetAllAsync();

    // طبق «PEMS — Global Grid Hardening» P1: GetAllAsync بالا کل جدول فعال Meetings را
    // بارگذاری می‌کند و سپس یک GroupBy جداگانه روی کل TaskItems برای ActionItemCount اجرا
    // می‌کند. این متد همان دو شمارنده (AttendeeCount/ActionItemCount) را با Subquery همبسته
    // داخل خودِ Select محاسبه می‌کند - نه با GroupBy/ToDictionary روی کل جدول.
    Task<PagedResult<MeetingDto>> GetPagedAsync(int page, int pageSize);

    Task<MeetingDetailDto?> GetDetailAsync(int meetingId);

    [PEMS.Application.Common.PemsOperation("Meeting", "Create")]
    Task<int> CreateAsync(string title, DateTime meetingDate, string? location, string? organizerName,
        int? projectId, string? agenda);

    [PEMS.Application.Common.PemsOperation("Meeting", "UpdateMinutes")]
    Task UpdateMinutesAsync(int meetingId, string minutesNotes, string status);

    [PEMS.Application.Common.PemsOperation("Meeting", "AddAttendee")]
    Task AddAttendeeAsync(int meetingId, int? employeeId, string? externalName, bool isRequired);

    [PEMS.Application.Common.PemsOperation("Meeting", "MarkAttended")]
    Task MarkAttendedAsync(int meetingAttendeeId, bool attended);
}
