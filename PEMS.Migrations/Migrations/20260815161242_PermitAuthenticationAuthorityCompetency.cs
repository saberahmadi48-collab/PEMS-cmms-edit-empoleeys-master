using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PermitAuthenticationAuthorityCompetency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeAreaScopes",
                columns: table => new
                {
                    EmployeeAreaScopeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    Plant = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Area = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeAreaScopes", x => x.EmployeeAreaScopeId);
                    table.ForeignKey(
                        name: "FK_EmployeeAreaScopes_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PermitApprovalCompetencyRequirements",
                columns: table => new
                {
                    PermitApprovalCompetencyRequirementId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermitType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequiredCertificationId = table.Column<int>(type: "int", nullable: true),
                    RequiredQualificationId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitApprovalCompetencyRequirements", x => x.PermitApprovalCompetencyRequirementId);
                    table.ForeignKey(
                        name: "FK_PermitApprovalCompetencyRequirements_Certifications_RequiredCertificationId",
                        column: x => x.RequiredCertificationId,
                        principalTable: "Certifications",
                        principalColumn: "CertificationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitApprovalCompetencyRequirements_Qualifications_RequiredQualificationId",
                        column: x => x.RequiredQualificationId,
                        principalTable: "Qualifications",
                        principalColumn: "QualificationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WebAuthnCredentials",
                columns: table => new
                {
                    WebAuthnCredentialId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CredentialId = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    SignCount = table.Column<long>(type: "bigint", nullable: false),
                    AAGUID = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Nickname = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastUsedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebAuthnCredentials", x => x.WebAuthnCredentialId);
                    table.ForeignKey(
                        name: "FK_WebAuthnCredentials_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PermitApprovalAuditRecords",
                columns: table => new
                {
                    PermitApprovalAuditRecordId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PermitId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApprovalStage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AuthenticationMethod = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CredentialReferenceId = table.Column<int>(type: "int", nullable: true),
                    AuthorizationDecision = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CompetencyReferenceId = table.Column<int>(type: "int", nullable: true),
                    CertificationReferenceId = table.Column<int>(type: "int", nullable: true),
                    AreaScope = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SessionReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PermitSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BlockingReasonsSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AuditCorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitApprovalAuditRecords", x => x.PermitApprovalAuditRecordId);
                    table.ForeignKey(
                        name: "FK_PermitApprovalAuditRecords_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitApprovalAuditRecords_Permits_PermitId",
                        column: x => x.PermitId,
                        principalTable: "Permits",
                        principalColumn: "PermitId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PermitApprovalAuditRecords_WebAuthnCredentials_CredentialReferenceId",
                        column: x => x.CredentialReferenceId,
                        principalTable: "WebAuthnCredentials",
                        principalColumn: "WebAuthnCredentialId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAreaScopes_EmployeeId",
                table: "EmployeeAreaScopes",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitApprovalAuditRecords_CredentialReferenceId",
                table: "PermitApprovalAuditRecords",
                column: "CredentialReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitApprovalAuditRecords_PermitId",
                table: "PermitApprovalAuditRecords",
                column: "PermitId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitApprovalAuditRecords_UserId",
                table: "PermitApprovalAuditRecords",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitApprovalCompetencyRequirements_RequiredCertificationId",
                table: "PermitApprovalCompetencyRequirements",
                column: "RequiredCertificationId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitApprovalCompetencyRequirements_RequiredQualificationId",
                table: "PermitApprovalCompetencyRequirements",
                column: "RequiredQualificationId");

            migrationBuilder.CreateIndex(
                name: "IX_WebAuthnCredentials_UserId",
                table: "WebAuthnCredentials",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeAreaScopes");

            migrationBuilder.DropTable(
                name: "PermitApprovalAuditRecords");

            migrationBuilder.DropTable(
                name: "PermitApprovalCompetencyRequirements");

            migrationBuilder.DropTable(
                name: "WebAuthnCredentials");
        }
    }
}
