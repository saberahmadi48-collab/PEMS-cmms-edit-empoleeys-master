using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoreRequisitionLineTraceability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "IssuedQuantity",
                table: "MaterialReservations",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "StoreRequisitionLineId",
                table: "MaterialReservations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialReservations_StoreRequisitionLineId",
                table: "MaterialReservations",
                column: "StoreRequisitionLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaterialReservations_StoreRequisitionLines_StoreRequisitionLineId",
                table: "MaterialReservations",
                column: "StoreRequisitionLineId",
                principalTable: "StoreRequisitionLines",
                principalColumn: "StoreRequisitionLineId",
                onDelete: ReferentialAction.Restrict);

            // ---- Backfill (قطعی و Idempotent؛ فقط داده‌ی بدون ابهام)
            // رزروهای کاملاً تحویل‌شده‌ی قدیمی: مقدار تحویل = مقدار رزرو
            migrationBuilder.Sql(@"UPDATE MaterialReservations SET IssuedQuantity = Quantity WHERE Status = 'FullyIssued' AND IssuedQuantity = 0;");

            // انتساب رزرو به خط فقط وقتی کالا در آن درخواست دقیقاً یک خط فعال دارد؛ موارد مبهم NULL می‌مانند (سرویس هنگام تحویل ابهام را گزارش می‌کند)
            migrationBuilder.Sql(@"
UPDATE r SET r.StoreRequisitionLineId =
    (SELECT MIN(l.StoreRequisitionLineId) FROM StoreRequisitionLines l
      WHERE l.StoreRequisitionId = r.StoreRequisitionId AND l.MaterialItemId = r.MaterialItemId AND l.IsActive = 1)
FROM MaterialReservations r
WHERE r.StoreRequisitionId IS NOT NULL AND r.StoreRequisitionLineId IS NULL
  AND (SELECT COUNT(*) FROM StoreRequisitionLines l
        WHERE l.StoreRequisitionId = r.StoreRequisitionId AND l.MaterialItemId = r.MaterialItemId AND l.IsActive = 1) = 1;");

            // ---- ناوردا‌های موجودی (WITH NOCHECK: ردیف‌های قدیمیِ احتمالاً نادرست مهاجرت را رد نمی‌کنند؛ نوشتن‌های جدید کنترل می‌شوند)
            migrationBuilder.Sql(@"ALTER TABLE MaterialStocks WITH NOCHECK ADD CONSTRAINT CK_MaterialStocks_Quantities
CHECK (OnHandQuantity >= 0 AND ReservedQuantity >= 0 AND ReservedQuantity <= OnHandQuantity);");
            migrationBuilder.Sql(@"ALTER TABLE StoreRequisitionLines WITH NOCHECK ADD CONSTRAINT CK_StoreRequisitionLines_Quantities
CHECK (RequestedQuantity >= 0 AND IssuedQuantity >= 0
       AND (ApprovedQuantity IS NULL OR (ApprovedQuantity >= 0 AND ApprovedQuantity <= RequestedQuantity))
       AND IssuedQuantity <= COALESCE(ApprovedQuantity, RequestedQuantity));");
            migrationBuilder.Sql(@"ALTER TABLE MaterialReservations WITH NOCHECK ADD CONSTRAINT CK_MaterialReservations_Quantities
CHECK (Quantity >= 0 AND IssuedQuantity >= 0 AND IssuedQuantity <= Quantity);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE MaterialReservations DROP CONSTRAINT CK_MaterialReservations_Quantities;");
            migrationBuilder.Sql("ALTER TABLE StoreRequisitionLines DROP CONSTRAINT CK_StoreRequisitionLines_Quantities;");
            migrationBuilder.Sql("ALTER TABLE MaterialStocks DROP CONSTRAINT CK_MaterialStocks_Quantities;");

            migrationBuilder.DropForeignKey(
                name: "FK_MaterialReservations_StoreRequisitionLines_StoreRequisitionLineId",
                table: "MaterialReservations");

            migrationBuilder.DropIndex(
                name: "IX_MaterialReservations_StoreRequisitionLineId",
                table: "MaterialReservations");

            migrationBuilder.DropColumn(
                name: "IssuedQuantity",
                table: "MaterialReservations");

            migrationBuilder.DropColumn(
                name: "StoreRequisitionLineId",
                table: "MaterialReservations");
        }
    }
}
