using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdcReferralOutcomeTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Comment",
                table: "DocumentIdcReferrals",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RespondedById",
                table: "DocumentIdcReferrals",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RespondedDate",
                table: "DocumentIdcReferrals",
                type: "datetime2",
                nullable: true);

            // طبق اصل «عدم جعل تاریخچه»: ردیف‌های قدیمیِ ارجاع IDC که این فیلد را نداشتند، هیچ
            // نتیجه‌ی واقعی ثبت‌شده‌ای ندارند - "Pending" صادقانه‌ترین مقدار پیش‌فرض است (نه
            // یک تصمیم ساختگی مثل Approve/Reject)
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "DocumentIdcReferrals",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Comment",
                table: "DocumentIdcReferrals");

            migrationBuilder.DropColumn(
                name: "RespondedById",
                table: "DocumentIdcReferrals");

            migrationBuilder.DropColumn(
                name: "RespondedDate",
                table: "DocumentIdcReferrals");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "DocumentIdcReferrals");
        }
    }
}
