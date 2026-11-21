using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RevalidateStoreRequisitionConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Revalidation شرطی: فقط اگر هیچ ردیف ناقض وجود نداشته باشد، قید «Trusted» می‌شود (WITH CHECK). هیچ داده‌ای تغییر/حذف نمی‌شود؛
            // اگر ناقض وجود داشته باشد، قید NOCHECK می‌ماند و باید طبق docs/audits/STORE_REQUISITION_PRODUCTION_SAFETY_AUDIT.md اصلاح شود.
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM MaterialStocks WHERE NOT (OnHandQuantity >= 0 AND ReservedQuantity >= 0 AND ReservedQuantity <= OnHandQuantity))
    ALTER TABLE MaterialStocks WITH CHECK CHECK CONSTRAINT CK_MaterialStocks_Quantities;");
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM StoreRequisitionLines WHERE NOT (RequestedQuantity >= 0 AND IssuedQuantity >= 0
       AND (ApprovedQuantity IS NULL OR (ApprovedQuantity >= 0 AND ApprovedQuantity <= RequestedQuantity))
       AND IssuedQuantity <= COALESCE(ApprovedQuantity, RequestedQuantity)))
    ALTER TABLE StoreRequisitionLines WITH CHECK CHECK CONSTRAINT CK_StoreRequisitionLines_Quantities;");
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM MaterialReservations WHERE NOT (Quantity >= 0 AND IssuedQuantity >= 0 AND IssuedQuantity <= Quantity))
    ALTER TABLE MaterialReservations WITH CHECK CHECK CONSTRAINT CK_MaterialReservations_Quantities;");
        

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
