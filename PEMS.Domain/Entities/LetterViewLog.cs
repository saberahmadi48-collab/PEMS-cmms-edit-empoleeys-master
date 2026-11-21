using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

/// <summary>چه کسی، کی و چند بار یک نامه را باز کرده است (یک ردیف به‌ازای هر نامه و کاربر؛ برای گزارش انطباق).</summary>
public class LetterViewLog
{
    [Key]
    public int LetterViewLogId { get; set; }

    public int LetterId { get; set; }
    public Letter? Letter { get; set; }

    public int UserId { get; set; }

    [MaxLength(200)]
    public string? UserName { get; set; }

    public DateTime FirstViewedDate { get; set; } = DateTime.Now;

    public DateTime LastViewedDate { get; set; } = DateTime.Now;

    public int ViewCount { get; set; } = 1;
}
