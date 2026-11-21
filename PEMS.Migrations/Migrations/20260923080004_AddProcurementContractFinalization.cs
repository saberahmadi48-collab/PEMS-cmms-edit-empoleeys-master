using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProcurementContractFinalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QualificationCategory",
                table: "Vendors",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QualificationEffectiveDate",
                table: "Vendors",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualificationEvidenceReference",
                table: "Vendors",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QualificationExpiryDate",
                table: "Vendors",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualificationNotes",
                table: "Vendors",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QualificationReviewedByEmployeeId",
                table: "Vendors",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QualificationReviewedById",
                table: "Vendors",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualificationStatus",
                table: "Vendors",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CriticalityClass",
                table: "ProcurementPackages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InspectionNotes",
                table: "ProcurementPackages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresInspection",
                table: "ProcurementPackages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RiskLevel",
                table: "ProcurementPackages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContractId",
                table: "Meetings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeetingType",
                table: "Meetings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ProcurementPackageId",
                table: "Meetings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedById",
                table: "Contracts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedDate",
                table: "Contracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentValue",
                table: "Contracts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveDate",
                table: "Contracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SignedById",
                table: "Contracts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SignedDate",
                table: "Contracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractDisciplineReviews",
                columns: table => new
                {
                    ContractDisciplineReviewId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    DisciplineRole = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    ReviewerId = table.Column<int>(type: "int", nullable: true),
                    Decision = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractDisciplineReviews", x => x.ContractDisciplineReviewId);
                    table.ForeignKey(
                        name: "FK_ContractDisciplineReviews_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "ContractId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContractDisciplineReviews_Employees_ReviewerId",
                        column: x => x.ReviewerId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractNTPs",
                columns: table => new
                {
                    ContractNTPId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    NTPNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IssuedById = table.Column<int>(type: "int", nullable: true),
                    IssuedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveStartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AttachmentId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractNTPs", x => x.ContractNTPId);
                    table.ForeignKey(
                        name: "FK_ContractNTPs_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "ContractId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractNTPs_DocumentAttachments_AttachmentId",
                        column: x => x.AttachmentId,
                        principalTable: "DocumentAttachments",
                        principalColumn: "AttachmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractNTPs_Employees_IssuedById",
                        column: x => x.IssuedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContractRevisions",
                columns: table => new
                {
                    ContractRevisionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ContractId = table.Column<int>(type: "int", nullable: false),
                    RevisionNo = table.Column<int>(type: "int", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreviousValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NewValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangeDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequestedById = table.Column<int>(type: "int", nullable: true),
                    RequestedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedById = table.Column<int>(type: "int", nullable: true),
                    ApprovedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractRevisions", x => x.ContractRevisionId);
                    table.ForeignKey(
                        name: "FK_ContractRevisions_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "ContractId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContractRevisions_Employees_ApprovedById",
                        column: x => x.ApprovedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractRevisions_Employees_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VendorQualificationHistories",
                columns: table => new
                {
                    VendorQualificationHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VendorId = table.Column<int>(type: "int", nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EvidenceReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ChangedById = table.Column<int>(type: "int", nullable: true),
                    ChangeDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorQualificationHistories", x => x.VendorQualificationHistoryId);
                    table.ForeignKey(
                        name: "FK_VendorQualificationHistories_Employees_ChangedById",
                        column: x => x.ChangedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VendorQualificationHistories_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "VendorId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BidEvaluations",
                columns: table => new
                {
                    BidEvaluationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BidId = table.Column<int>(type: "int", nullable: false),
                    EvaluationType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvaluatorId = table.Column<int>(type: "int", nullable: true),
                    DisciplineRole = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Decision = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EvaluationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BidEvaluations", x => x.BidEvaluationId);
                    table.ForeignKey(
                        name: "FK_BidEvaluations_Employees_EvaluatorId",
                        column: x => x.EvaluatorId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BidNegotiations",
                columns: table => new
                {
                    BidNegotiationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BidId = table.Column<int>(type: "int", nullable: false),
                    RoundNo = table.Column<int>(type: "int", nullable: false),
                    NegotiationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Participants = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PreviousValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NegotiatedValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CommercialChanges = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TechnicalChanges = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContractualChanges = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Justification = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SupportingDocumentReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApprovedById = table.Column<int>(type: "int", nullable: true),
                    ApprovedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BidNegotiations", x => x.BidNegotiationId);
                    table.ForeignKey(
                        name: "FK_BidNegotiations_Employees_ApprovedById",
                        column: x => x.ApprovedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Bids",
                columns: table => new
                {
                    BidId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BidNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    VendorId = table.Column<int>(type: "int", nullable: false),
                    SubmittedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OriginalValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NegotiatedValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FinalValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TechnicalSubmissionReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CommercialSubmissionReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bids", x => x.BidId);
                    table.ForeignKey(
                        name: "FK_Bids_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "VendorId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tenders",
                columns: table => new
                {
                    TenderId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProcurementPackageId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResponseDeadline = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequiresVendorQualification = table.Column<bool>(type: "bit", nullable: false),
                    TechnicalRequirements = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CommercialRequirements = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InspectionRequirements = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContractualRequirements = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AwardedBidId = table.Column<int>(type: "int", nullable: true),
                    AwardedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AwardedById = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenders", x => x.TenderId);
                    table.ForeignKey(
                        name: "FK_Tenders_Bids_AwardedBidId",
                        column: x => x.AwardedBidId,
                        principalTable: "Bids",
                        principalColumn: "BidId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tenders_Employees_AwardedById",
                        column: x => x.AwardedById,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tenders_ProcurementPackages_ProcurementPackageId",
                        column: x => x.ProcurementPackageId,
                        principalTable: "ProcurementPackages",
                        principalColumn: "ProcurementPackageId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenderInvitedVendors",
                columns: table => new
                {
                    TenderInvitedVendorId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenderId = table.Column<int>(type: "int", nullable: false),
                    VendorId = table.Column<int>(type: "int", nullable: false),
                    InvitedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    QualificationConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    TenantId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenderInvitedVendors", x => x.TenderInvitedVendorId);
                    table.ForeignKey(
                        name: "FK_TenderInvitedVendors_Tenders_TenderId",
                        column: x => x.TenderId,
                        principalTable: "Tenders",
                        principalColumn: "TenderId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TenderInvitedVendors_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalTable: "Vendors",
                        principalColumn: "VendorId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_QualificationReviewedByEmployeeId",
                table: "Vendors",
                column: "QualificationReviewedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Meetings_ContractId",
                table: "Meetings",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_Meetings_ProcurementPackageId",
                table: "Meetings",
                column: "ProcurementPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_ApprovedById",
                table: "Contracts",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_SignedById",
                table: "Contracts",
                column: "SignedById");

            migrationBuilder.CreateIndex(
                name: "IX_BidEvaluations_BidId",
                table: "BidEvaluations",
                column: "BidId");

            migrationBuilder.CreateIndex(
                name: "IX_BidEvaluations_EvaluatorId",
                table: "BidEvaluations",
                column: "EvaluatorId");

            migrationBuilder.CreateIndex(
                name: "IX_BidNegotiations_ApprovedById",
                table: "BidNegotiations",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_BidNegotiations_BidId_RoundNo",
                table: "BidNegotiations",
                columns: new[] { "BidId", "RoundNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bids_BidNumber",
                table: "Bids",
                column: "BidNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bids_TenderId_VendorId",
                table: "Bids",
                columns: new[] { "TenderId", "VendorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bids_VendorId",
                table: "Bids",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractDisciplineReviews_ContractId_DisciplineRole",
                table: "ContractDisciplineReviews",
                columns: new[] { "ContractId", "DisciplineRole" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractDisciplineReviews_ReviewerId",
                table: "ContractDisciplineReviews",
                column: "ReviewerId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractNTPs_AttachmentId",
                table: "ContractNTPs",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractNTPs_ContractId",
                table: "ContractNTPs",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractNTPs_IssuedById",
                table: "ContractNTPs",
                column: "IssuedById");

            migrationBuilder.CreateIndex(
                name: "IX_ContractNTPs_NTPNumber",
                table: "ContractNTPs",
                column: "NTPNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractRevisions_ApprovedById",
                table: "ContractRevisions",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_ContractRevisions_ContractId_RevisionNo",
                table: "ContractRevisions",
                columns: new[] { "ContractId", "RevisionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractRevisions_RequestedById",
                table: "ContractRevisions",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_TenderInvitedVendors_TenderId_VendorId",
                table: "TenderInvitedVendors",
                columns: new[] { "TenderId", "VendorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenderInvitedVendors_VendorId",
                table: "TenderInvitedVendors",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_AwardedBidId",
                table: "Tenders",
                column: "AwardedBidId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_AwardedById",
                table: "Tenders",
                column: "AwardedById");

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_ProcurementPackageId",
                table: "Tenders",
                column: "ProcurementPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenders_TenderNumber",
                table: "Tenders",
                column: "TenderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendorQualificationHistories_ChangedById",
                table: "VendorQualificationHistories",
                column: "ChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_VendorQualificationHistories_VendorId",
                table: "VendorQualificationHistories",
                column: "VendorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Contracts_Employees_ApprovedById",
                table: "Contracts",
                column: "ApprovedById",
                principalTable: "Employees",
                principalColumn: "EmployeeId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Contracts_Employees_SignedById",
                table: "Contracts",
                column: "SignedById",
                principalTable: "Employees",
                principalColumn: "EmployeeId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Meetings_Contracts_ContractId",
                table: "Meetings",
                column: "ContractId",
                principalTable: "Contracts",
                principalColumn: "ContractId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Meetings_ProcurementPackages_ProcurementPackageId",
                table: "Meetings",
                column: "ProcurementPackageId",
                principalTable: "ProcurementPackages",
                principalColumn: "ProcurementPackageId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vendors_Employees_QualificationReviewedByEmployeeId",
                table: "Vendors",
                column: "QualificationReviewedByEmployeeId",
                principalTable: "Employees",
                principalColumn: "EmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_BidEvaluations_Bids_BidId",
                table: "BidEvaluations",
                column: "BidId",
                principalTable: "Bids",
                principalColumn: "BidId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_BidNegotiations_Bids_BidId",
                table: "BidNegotiations",
                column: "BidId",
                principalTable: "Bids",
                principalColumn: "BidId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Bids_Tenders_TenderId",
                table: "Bids",
                column: "TenderId",
                principalTable: "Tenders",
                principalColumn: "TenderId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contracts_Employees_ApprovedById",
                table: "Contracts");

            migrationBuilder.DropForeignKey(
                name: "FK_Contracts_Employees_SignedById",
                table: "Contracts");

            migrationBuilder.DropForeignKey(
                name: "FK_Meetings_Contracts_ContractId",
                table: "Meetings");

            migrationBuilder.DropForeignKey(
                name: "FK_Meetings_ProcurementPackages_ProcurementPackageId",
                table: "Meetings");

            migrationBuilder.DropForeignKey(
                name: "FK_Vendors_Employees_QualificationReviewedByEmployeeId",
                table: "Vendors");

            migrationBuilder.DropForeignKey(
                name: "FK_Tenders_Bids_AwardedBidId",
                table: "Tenders");

            migrationBuilder.DropTable(
                name: "BidEvaluations");

            migrationBuilder.DropTable(
                name: "BidNegotiations");

            migrationBuilder.DropTable(
                name: "ContractDisciplineReviews");

            migrationBuilder.DropTable(
                name: "ContractNTPs");

            migrationBuilder.DropTable(
                name: "ContractRevisions");

            migrationBuilder.DropTable(
                name: "TenderInvitedVendors");

            migrationBuilder.DropTable(
                name: "VendorQualificationHistories");

            migrationBuilder.DropTable(
                name: "Bids");

            migrationBuilder.DropTable(
                name: "Tenders");

            migrationBuilder.DropIndex(
                name: "IX_Vendors_QualificationReviewedByEmployeeId",
                table: "Vendors");

            migrationBuilder.DropIndex(
                name: "IX_Meetings_ContractId",
                table: "Meetings");

            migrationBuilder.DropIndex(
                name: "IX_Meetings_ProcurementPackageId",
                table: "Meetings");

            migrationBuilder.DropIndex(
                name: "IX_Contracts_ApprovedById",
                table: "Contracts");

            migrationBuilder.DropIndex(
                name: "IX_Contracts_SignedById",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "QualificationCategory",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "QualificationEffectiveDate",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "QualificationEvidenceReference",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "QualificationExpiryDate",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "QualificationNotes",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "QualificationReviewedByEmployeeId",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "QualificationReviewedById",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "QualificationStatus",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "CriticalityClass",
                table: "ProcurementPackages");

            migrationBuilder.DropColumn(
                name: "InspectionNotes",
                table: "ProcurementPackages");

            migrationBuilder.DropColumn(
                name: "RequiresInspection",
                table: "ProcurementPackages");

            migrationBuilder.DropColumn(
                name: "RiskLevel",
                table: "ProcurementPackages");

            migrationBuilder.DropColumn(
                name: "ContractId",
                table: "Meetings");

            migrationBuilder.DropColumn(
                name: "MeetingType",
                table: "Meetings");

            migrationBuilder.DropColumn(
                name: "ProcurementPackageId",
                table: "Meetings");

            migrationBuilder.DropColumn(
                name: "ApprovedById",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "ApprovedDate",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "CurrentValue",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "EffectiveDate",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "SignedById",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "SignedDate",
                table: "Contracts");
        }
    }
}
