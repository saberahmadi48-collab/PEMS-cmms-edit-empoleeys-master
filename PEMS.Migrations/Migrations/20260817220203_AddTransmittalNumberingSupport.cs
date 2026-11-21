using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransmittalNumberingSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UnitCode",
                table: "Projects",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransmittalCode",
                table: "Disciplines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 1,
                columns: new[] { "Code", "ImportAliases", "TransmittalCode" },
                values: new object[] { "PR", "PROC,PRO,PROCESS,PROCS", "J" });

            migrationBuilder.UpdateData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 2,
                columns: new[] { "Code", "Description", "ImportAliases", "Name", "TransmittalCode" },
                values: new object[] { "ME", "Mechanical Engineering - Static Equipment", "MECH,MEC,MECHANICAL,STATIC", "Mechanical (Static)", "C" });

            migrationBuilder.UpdateData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 3,
                columns: new[] { "Code", "ImportAliases", "TransmittalCode" },
                values: new object[] { "EL", "ELEC,ELE,ELECTRICAL,PWR,POWER", "N" });

            migrationBuilder.UpdateData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 4,
                columns: new[] { "Code", "ImportAliases", "Name", "TransmittalCode" },
                values: new object[] { "IN", "INST,INS,IC,I&C,INSTRUMENT,CONTROL,INSTRUMENTATION", "Instrumentation & Control", "K" });

            migrationBuilder.UpdateData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 5,
                columns: new[] { "Code", "ImportAliases", "TransmittalCode" },
                values: new object[] { "PI", "PIPE,PIP,PIPING,PIPELINE", "X" });

            migrationBuilder.UpdateData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 6,
                columns: new[] { "Code", "ImportAliases", "TransmittalCode" },
                values: new object[] { "CV", "CIVL,CI,CIV,CIVIL,CS", "A" });

            migrationBuilder.InsertData(
                table: "Disciplines",
                columns: new[] { "DisciplineId", "Code", "CreatedDate", "Description", "ImportAliases", "IsActive", "Name", "TransmittalCode" },
                values: new object[,]
                {
                    { 7, "PM", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Mechanical Engineering - Rotary/Process Machinery & Packages", "ROTATING,ROTARY,MACHINERY,PACKAGE", true, "Mechanical (Rotary)", "G" },
                    { 8, "TC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Telecommunication Engineering", "TELECOM,TELECOMMUNICATION", true, "Telecommunication", null },
                    { 9, "CP", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Cathodic Protection Engineering", "CATHODIC", true, "Cathodic Protection", null },
                    { 10, "AR", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Architectural Engineering", "ARCH,ARCHITECTURE", true, "Architectural", "A" },
                    { 11, "ST", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Structural Engineering", "STR,STRUCTURAL", true, "Structural", "A" },
                    { 12, "HS", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Health, Safety & Environment", "HSE,SAFETY,HEALTH", true, "HSE", "H" },
                    { 13, "HV", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Heating, Ventilation & Air Conditioning", "HVAC", true, "HVAC", null },
                    { 14, "CN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Construction Management", "CONSTRUCTION", true, "Construction", null },
                    { 15, "PQ", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Procurement", "PROCUREMENT,PURCHASING", true, "Procurement", null },
                    { 16, "CC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Cost Control", "COST", true, "Cost Control", "M" },
                    { 17, "PL", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Project Planning and Control", "PLANNING", true, "Project Planning & Control", "M" },
                    { 18, "PP", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Project Management", "PROJECTMGMT,PROJMGMT", true, "Project Management", "M" },
                    { 19, "IT", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Information Technology", "INFOTECH", true, "Information Technology", null },
                    { 20, "LB", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Laboratory", "LAB,LABORATORY", true, "Laboratory", "W" },
                    { 21, "FN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Financing", "FINANCE,FINANCING", true, "Financing", null },
                    { 22, "QA", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Quality Assurance", "QUALITY", true, "Quality Assurance", null },
                    { 23, "IP", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Insulation and Painting", "INSULATION,PAINTING", true, "Insulation/Painting", "X" }
                });

            migrationBuilder.UpdateData(
                table: "Projects",
                keyColumn: "ProjectId",
                keyValue: 1,
                column: "UnitCode",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 17);

            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 19);

            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 20);

            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 21);

            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 22);

            migrationBuilder.DeleteData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 23);

            migrationBuilder.DropColumn(
                name: "UnitCode",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "TransmittalCode",
                table: "Disciplines");

            migrationBuilder.UpdateData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 1,
                columns: new[] { "Code", "ImportAliases" },
                values: new object[] { "PROC", "PR,PRO,PROCESS,PROCS" });

            migrationBuilder.UpdateData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 2,
                columns: new[] { "Code", "Description", "ImportAliases", "Name" },
                values: new object[] { "MECH", "Mechanical Engineering", "ME,MEC,MECHANICAL,ROTATING,STATIC", "Mechanical" });

            migrationBuilder.UpdateData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 3,
                columns: new[] { "Code", "ImportAliases" },
                values: new object[] { "ELEC", "EL,ELE,ELECTRICAL,PWR,POWER" });

            migrationBuilder.UpdateData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 4,
                columns: new[] { "Code", "ImportAliases", "Name" },
                values: new object[] { "INST", "IN,INS,IC,I&C,INSTRUMENT,CONTROL,INSTRUMENTATION", "Instrumentation" });

            migrationBuilder.UpdateData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 5,
                columns: new[] { "Code", "ImportAliases" },
                values: new object[] { "PIPE", "PI,PIP,PL,PIPING,PIPELINE" });

            migrationBuilder.UpdateData(
                table: "Disciplines",
                keyColumn: "DisciplineId",
                keyValue: 6,
                columns: new[] { "Code", "ImportAliases" },
                values: new object[] { "CIVL", "CI,CIV,ST,STR,CIVIL,STRUCTURAL,CS" });
        }
    }
}
