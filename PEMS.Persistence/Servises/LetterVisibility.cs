using Microsoft.EntityFrameworkCore;
using PEMS.Application.Common;
using PEMS.Domain.Entities;
using PEMS.Persistence.Context;

namespace PEMS.Persistence.Services;

/// <summary>
/// فیلتر دسترسی به نامه در خودِ Query (قاعده‌ی هم‌ارز <see cref="LetterAccessRules.CanView"/>): مرجع مشترک فهرست نامه‌ها و پنل رشته‌ی مکاتبه‌ی DCC
/// تا هر دو دقیقاً یک قانون محرمانگی را اعمال کنند. کاربر بدون مجوز بالا فقط نامه‌های خودش، نامه‌های زنجیره‌ی تاییدش و نامه‌ی منتشرشده‌ی
/// Public/Internal یا نامه‌ای که گیرنده/ارجاع‌گیرنده‌ی آن است را می‌بیند.
/// </summary>
internal static class LetterVisibility
{
    public static async Task<IQueryable<Letter>> ApplyAsync(PEMSDbContext _context, IQueryable<Letter> query,
        bool canViewConfidential, int? requestingApplicationUserId)
    {
        if (canViewConfidential) return query;

        var employeeId = requestingApplicationUserId.HasValue
            ? await _context.Users.Where(x => x.Id == requestingApplicationUserId.Value)
                .Select(x => x.EmployeeId).FirstOrDefaultAsync()
            : null;

        var referredLetterIds = employeeId.HasValue
            ? await _context.LetterReferrals.Where(x => x.ReferredToEmployeeId == employeeId.Value)
                .Select(x => x.LetterId).ToListAsync()
            : new List<int>();

        // گیرنده‌ی To/Cc/Bcc نامه (قبلاً گیرنده‌ی نامه‌ی محرمانه بدون ارجاع جدا آن را در فهرست نمی‌دید)
        var recipientLetterIds = employeeId.HasValue
            ? await _context.LetterRecipients.Where(x => x.EmployeeId == employeeId.Value)
                .Select(x => x.LetterId).Distinct().ToListAsync()
            : new List<int>();

        // عضو زنجیره‌ی تایید، نامه‌ی در انتظار تاییدش را می‌بیند
        var chainLetterIds = requestingApplicationUserId.HasValue
            ? await _context.LetterApprovalSteps.Where(x => x.ApproverUserId == requestingApplicationUserId.Value)
                .Select(x => x.LetterId).Distinct().ToListAsync()
            : new List<int>();

        var preSend = LetterAccessRules.PreSendStatuses;

        // قاعده‌ی هم‌ارز LetterAccessRules.CanView (پیش‌نویس/در انتظار تایید فقط نویسنده و زنجیره‌ی تایید؛ نامه‌ی منتشرشده بر اساس سطح
        // محرمانگی یا گیرنده/ارجاع‌گیرنده‌بودن)
        return query.Where(x =>
            (requestingApplicationUserId.HasValue && x.RegisteredById == requestingApplicationUserId.Value) ||
            chainLetterIds.Contains(x.LetterId) ||
            (!preSend.Contains(x.Status) &&
                (x.ConfidentialityLevel == "Public" || x.ConfidentialityLevel == "Internal" ||
                 referredLetterIds.Contains(x.LetterId) || recipientLetterIds.Contains(x.LetterId))));
    }
}
