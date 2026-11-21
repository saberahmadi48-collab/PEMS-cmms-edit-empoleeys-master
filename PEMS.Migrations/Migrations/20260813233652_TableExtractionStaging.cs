using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TableExtractionStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TableExtractionBatches",
                columns: table => new
                {
                    TableExtractionBatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceAttachmentId = table.Column<int>(type: "int", nullable: false),
                    SourceFileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExtractionMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableExtractionBatches", x => x.TableExtractionBatchId);
                    table.ForeignKey(
                        name: "FK_TableExtractionBatches_DocumentAttachments_SourceAttachmentId",
                        column: x => x.SourceAttachmentId,
                        principalTable: "DocumentAttachments",
                        principalColumn: "AttachmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TableExtractionBatches_Employees_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TableExtractionRows",
                columns: table => new
                {
                    TableExtractionRowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TableExtractionBatchId = table.Column<int>(type: "int", nullable: false),
                    TagNumber = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JunctionBox = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Terminal = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CableNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MarshallingCabinet = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MarshallingTerminal = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SystemCabinet = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IoCard = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IoChannel = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IoAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DcsTagNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LoopNo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    System = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SignalClass = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SpareOrHotSpare = table.Column<bool>(type: "bit", nullable: true),
                    Remarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawExtraJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MatchStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MatchedEquipmentId = table.Column<int>(type: "int", nullable: true),
                    ResultInstrumentId = table.Column<int>(type: "int", nullable: true),
                    ReviewedById = table.Column<int>(type: "int", nullable: true),
                    ReviewedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableExtractionRows", x => x.TableExtractionRowId);
                    table.ForeignKey(
                        name: "FK_TableExtractionRows_Employees_ReviewedById",
                        column: x => x.ReviewedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TableExtractionRows_Equipments_MatchedEquipmentId",
                        column: x => x.MatchedEquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "EquipmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TableExtractionRows_Instruments_ResultInstrumentId",
                        column: x => x.ResultInstrumentId,
                        principalTable: "Instruments",
                        principalColumn: "InstrumentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TableExtractionRows_TableExtractionBatches_TableExtractionBatchId",
                        column: x => x.TableExtractionBatchId,
                        principalTable: "TableExtractionBatches",
                        principalColumn: "TableExtractionBatchId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TableExtractionBatches_CreatedById",
                table: "TableExtractionBatches",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TableExtractionBatches_SourceAttachmentId",
                table: "TableExtractionBatches",
                column: "SourceAttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TableExtractionRows_MatchedEquipmentId",
                table: "TableExtractionRows",
                column: "MatchedEquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TableExtractionRows_ResultInstrumentId",
                table: "TableExtractionRows",
                column: "ResultInstrumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TableExtractionRows_ReviewedById",
                table: "TableExtractionRows",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_TableExtractionRows_TableExtractionBatchId",
                table: "TableExtractionRows",
                column: "TableExtractionBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_TableExtractionRows_TagNumber",
                table: "TableExtractionRows",
                column: "TagNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TableExtractionRows");

            migrationBuilder.DropTable(
                name: "TableExtractionBatches");
        }
    }
}
