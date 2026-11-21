using System.ComponentModel.DataAnnotations;

namespace PEMS.Domain.Entities;

public class Project
{
    public int ProjectId { get; set; }


    // کد یکتا پروژه (SCRUM-539: required; an empty form was saved as a project without code and name)
    [Required]
    [Display(Name = "Project Code")]   // SCRUM-540: the message names the field in the page language
    public string ProjectCode { get; set; } = string.Empty;


    // نام پروژه
    [Required]
    [Display(Name = "Project Name")]
    public string ProjectName { get; set; } = string.Empty;


    // کارفرما
    public string? ClientName { get; set; }


    // محل اجرای پروژه
    public string? Location { get; set; }


    // تاریخ شروع
    public DateTime? StartDate { get; set; }


    // تاریخ پایان
    public DateTime? EndDate { get; set; }


    // وضعیت پروژه
    // Planning, Active, Completed, Cancelled
    public string Status { get; set; } = "Planning";


    public bool IsActive { get; set; } = true;


    // طبق «VENDOR DOCUMENTATION MANAGEMENT PROCEDURE» بخش ۳: کد واحد/پلنت (مثل ۸۰/۸۵/۸۸) که
    // در شماره‌ی ترانسمیتال (TV-X-UU-AAAA-NNNN) به‌عنوان UU استفاده می‌شود.
    public string? UnitCode { get; set; }


    // طبق بخش ۴.۳.۱ همان سند: کد تک‌حرفی پلنت (نمونه سند: "J") که در شماره‌ی رسمی مدرک
    // (J-85-XXX-ME-DSH-...) به‌عنوان بخش اول استفاده می‌شود - مستقل از UnitCode.
    public string? PlantCode { get; set; }


    // طبق بخش ۴.۳.۲ همان سند: شماره‌ی کار SUB-CONTRACTOR (نمونه سند: "3863") که در شماره‌ی
    // فرمت Vendor (3863-XX-VD-TT-...) به‌عنوان بخش اول استفاده می‌شود.
    public string? JobNumber { get; set; }


    public DateTime CreatedDate { get; set; }
}