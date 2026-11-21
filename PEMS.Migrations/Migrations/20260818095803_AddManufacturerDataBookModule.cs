using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddManufacturerDataBookModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ManufacturerDataBooks",
                columns: table => new
                {
                    MDBId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EquipmentId = table.Column<int>(type: "int", nullable: false),
                    VendorId = table.Column<int>(type: "int", nullable: false),
                    PoId = table.Column<int>(type: "int", nullable: true),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    PlannedFinalIssueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IndexOfContentsSubmittedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HardCopyCount = table.Column<int>(type: "int", nullable: true),
                    HardCopyShippedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SoftCopyDeliveredDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturerDataBooks", x => x.MDBId);
                    table.ForeignKey(
                        name: "FK_ManufacturerDataBooks_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManufacturerDataBooks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManufacturerDataBooks_PurchaseOrders_PoId",
                        column: x => x.PoId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "PoId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManufacturerDataBooks_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "VendorId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MDBDocuments",
                columns: table => new
                {
                    MDBDocumentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MDBId = table.Column<int>(type: "int", nullable: false),
                    Part = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DocumentId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DvdVolumeNo = table.Column<int>(type: "int", nullable: true),
                    IsNotApplicable = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MDBDocuments", x => x.MDBDocumentId);
                    table.ForeignKey(
                        name: "FK_MDBDocuments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "DocumentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MDBDocuments_ManufacturerDataBooks_MDBId",
                        column: x => x.MDBId,
                        principalTable: "ManufacturerDataBooks",
                        principalColumn: "MDBId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturerDataBooks_EquipmentId",
                table: "ManufacturerDataBooks",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturerDataBooks_PoId",
                table: "ManufacturerDataBooks",
                column: "PoId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturerDataBooks_ProjectId",
                table: "ManufacturerDataBooks",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturerDataBooks_VendorId",
                table: "ManufacturerDataBooks",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_MDBDocuments_DocumentId",
                table: "MDBDocuments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_MDBDocuments_MDBId",
                table: "MDBDocuments",
                column: "MDBId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MDBDocuments");

            migrationBuilder.DropTable(
                name: "ManufacturerDataBooks");
        }
    }
}
