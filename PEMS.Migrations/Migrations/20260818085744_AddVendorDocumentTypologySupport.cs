using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorDocumentTypologySupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TypologyCode",
                table: "VDRItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VendorFormatDocumentNumber",
                table: "VDRItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JobNumber",
                table: "Projects",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VendorDocumentTypologyCodes",
                columns: table => new
                {
                    VendorDocumentTypologyCodeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorDocumentTypologyCodes", x => x.VendorDocumentTypologyCodeId);
                });

            migrationBuilder.UpdateData(
                table: "Projects",
                keyColumn: "ProjectId",
                keyValue: 1,
                column: "JobNumber",
                value: null);

            migrationBuilder.InsertData(
                table: "VendorDocumentTypologyCodes",
                columns: new[] { "VendorDocumentTypologyCodeId", "Category", "Code", "CreatedDate", "Description", "IsActive" },
                values: new object[,]
                {
                    { 1, "Project Management / Planning / Procurement / Quality Assurance / HSE Assurance", "PL", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Document List; Document Issuing Plan", true },
                    { 2, "Project Management / Planning / Procurement / Quality Assurance / HSE Assurance", "PP", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Manufacturing schedule; Fabrication Plan; Progress Reports", true },
                    { 3, "Project Management / Planning / Procurement / Quality Assurance / HSE Assurance", "PS", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "List of Subvendors/Subcontractors; List of Suborders", true },
                    { 4, "Project Management / Planning / Procurement / Quality Assurance / HSE Assurance", "PQ", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Quality Assurance Documents (Quality Plan, Site Quality Plan, Organization Chart, Audit plans)", true },
                    { 5, "Project Management / Planning / Procurement / Quality Assurance / HSE Assurance", "PH", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "HSE Assurance Documents (HSE Plan, Site HSE Plan)", true },
                    { 6, "Quality Control / Inspection & Testing", "QC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Inspection & Test Plan (Quality Control Plan)", true },
                    { 7, "Quality Control / Inspection & Testing", "QS", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Site (Construction) Inspection & Test Plan (relevant to Site assembled/tested equipment)", true },
                    { 8, "Quality Control / Inspection & Testing", "QP", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Inspection and Testing procedures (functional, performance, material, NDE, pressure (hydraulic) tests)", true },
                    { 9, "Quality Control / Inspection & Testing", "QT", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Factory Acceptance Test Procedures, Site Acceptance Test Procedures", true },
                    { 10, "Fabrication & Welding", "FW", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Welding Books: Welding Process Specifications (WPS), Welding Process Qualifications (PQR), Welding maps; Welding Repair Procedures; Heat Treatment Procedures", true },
                    { 11, "Fabrication & Welding", "FO", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Description of the fabrication cycle, other Manufacturing Procedures, Cladding procedure", true },
                    { 12, "Fabrication & Welding", "FP", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Internal Lining, Coating and Painting Procedures", true },
                    { 13, "Basic and Process Data / Performance Design Data", "BC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Heat/Material Balance; Thermal Calculations; Utilities calculation/consumption; Effluent/Waste/Emission List; Functional sizing calculations; Process Calculations, Performance Curves; Reliability & Availability Analysis", true },
                    { 14, "Basic and Process Data / Performance Design Data", "BO", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Description of working: Interlock, Control strategies and Sequences narratives; Cause/effect diagrams", true },
                    { 15, "Basic and Process Data / Performance Design Data", "BD", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Process Flow Diagrams; Piping & Instrument Diagrams; Machinery Circuits diagrams (lubrication / hydraulic / pneumatic)", true },
                    { 16, "General Configuration - Equipment List/Data Sheets", "GE", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Equipment List; Auxiliary and other Components lists", true },
                    { 17, "General Configuration - Equipment List/Data Sheets", "GS", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Equipment Data Sheets/Specifications", true },
                    { 18, "General Configuration - Equipment List/Data Sheets", "GH", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Fire Fighting Equipment Data Sheets and Drawings", true },
                    { 19, "General Configuration - Equipment List/Data Sheets", "GP", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Spare Parts Lists, SPIR forms, Spare Parts Drawings (precomm / comm / capital / operation spare parts)", true },
                    { 20, "General Configuration - Equipment List/Data Sheets", "GL", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Lubricants Lists and Quantities (Schedules), Lubricant Specifications", true },
                    { 21, "General Configuration - Equipment List/Data Sheets", "GT", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "List of special tools (erection, testing, operation)", true },
                    { 22, "Layouts / General Arrangements / Foundation Data", "LG", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Equipment layout, General Arrangements (with data for foundations); Plot plans, Layouts and requirements for UG services (sewers, piping / cable routing)", true },
                    { 23, "Layouts / General Arrangements / Foundation Data", "LF", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Foundations (or Process concrete structures) / Supports Calculations & Design", true },
                    { 24, "HSE Design", "HG", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Material Safety Data Sheets", true },
                    { 25, "HSE Design", "HN", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Noise Source Data Sheets", true },
                    { 26, "HSE Design", "HF", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Sources or Areas of Release Data Sheets, Hazardous Areas Classification Drawings", true },
                    { 27, "Mechanical Design (Static Components)", "SC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Mechanical Calculations (Design/Code)", true },
                    { 28, "Mechanical Design (Static Components)", "SD", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Vessels / Tanks / other Pressure Equipment Drawings - Nozzle Orientation", true },
                    { 29, "Mechanical Design (Static Components)", "SS", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Auxiliary Steel Structures Specifications & Drawings", true },
                    { 30, "Mechanical Design (Rotating Components)", "RC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Mechanical Calculations (rotating elements, vibrations, pulsation, etc)", true },
                    { 31, "Mechanical Design (Rotating Components)", "RD", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Machinery / Rotating Equipment Drawings", true },
                    { 32, "Mechanical Design (Rotating Components)", "RM", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Other Mechanical Equipment Drawings", true },
                    { 33, "Piping Design", "PC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Piping Calculations (design, stress)", true },
                    { 34, "Piping Design", "PD", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Piping Arrangement/Layouts, Piping Isometrics, Tie-in drawings", true },
                    { 35, "Piping Design", "PG", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Piping Components General Specifications / Classes; Supports Specifications & Drawings", true },
                    { 36, "Instrumentation / Automation & Process Control Design", "IB", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Block Diagrams, Architectural Diagrams", true },
                    { 37, "Instrumentation / Automation & Process Control Design", "IL", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Instrument list, I/O lists, Alarm and trip lists, Memory Maps for Serial Communications, Point Configuration Database", true },
                    { 38, "Instrumentation / Automation & Process Control Design", "IS", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Instrument Data Sheets & Specifications", true },
                    { 39, "Instrumentation / Automation & Process Control Design", "IV", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Instrumental component drawings (instrumentation, valves, etc)", true },
                    { 40, "Instrumentation / Automation & Process Control Design", "IC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Instrumental Calculations (instrument sizing, SIL, etc)", true },
                    { 41, "Instrumentation / Automation & Process Control Design", "IT", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Typical installation drawings", true },
                    { 42, "Instrumentation / Automation & Process Control Design", "IW", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Instrumental wiring diagrams: Interconnection diagrams, DCS/PLC interfacing diagrams", true },
                    { 43, "Instrumentation / Automation & Process Control Design", "IG", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Instrument layouts, Cable run layouts", true },
                    { 44, "Instrumentation / Automation & Process Control Design", "IK", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "List of cables (Instrumental)", true },
                    { 45, "Instrumentation / Automation & Process Control Design", "ID", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Logic Diagrams, Loop Diagrams", true },
                    { 46, "Instrumentation / Automation & Process Control Design", "IP", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Instrumentation / Instrumental Panels Construction Drawings", true },
                    { 47, "Instrumentation / Automation & Process Control Design", "IM", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Man Machine Interface, Graphic Pages, Synoptic and Operating Panels layouts", true },
                    { 48, "Electrical Design", "EB", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Block Diagrams, Architectural Diagrams", true },
                    { 49, "Electrical Design", "EU", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "List of Electric Users / Electrical Consumptions", true },
                    { 50, "Electrical Design", "EC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Electrical Calculations; Electrical Curves", true },
                    { 51, "Electrical Design", "ED", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Electric operating diagrams, one-line and interconnection diagrams, typical wiring diagrams, electric tracing diagrams, grounding diagrams", true },
                    { 52, "Electrical Design", "ES", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Motors, Switchgears, Variable Speed Drivers, Distribution Boards, other Electrical Equipment Data Sheets / Specifications", true },
                    { 53, "Electrical Design", "EE", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Motors, Switchgears, Variable Speed Drivers, Distribution Boards, other Electrical Equipment Drawings", true },
                    { 54, "Electrical Design", "EG", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Electrical cable run layouts, Electrical equipment layout", true },
                    { 55, "Electrical Design", "EL", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "List of cables (Electrical)", true },
                    { 56, "HVAC Design", "VH", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Heat Dissipation Calculations", true },
                    { 57, "HVAC Design", "VC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "HVAC Design Calculation Reports / Utility Consumption Analyses", true },
                    { 58, "HVAC Design", "VG", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "HVAC General Drawings", true },
                    { 59, "HVAC Design", "VL", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "HVAC System Layouts/Sections (Mechanical Part)", true },
                    { 60, "HVAC Design", "VE", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "HVAC System Layouts/Sections (Electrical/Automatic Control Parts)", true },
                    { 61, "HVAC Design", "VT", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "HVAC System Material Take-Offs", true },
                    { 62, "Civil Design / Steel Structures Design", "AD", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Building / Architectural Design (specifications and drawings)", true },
                    { 63, "Civil Design / Steel Structures Design", "AC", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Steel Structures Calculation reports", true },
                    { 64, "Civil Design / Steel Structures Design", "AE", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Steel Structures Assembly Drawings", true },
                    { 65, "Civil Design / Steel Structures Design", "AS", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Steel Structures Shop Drawings", true },
                    { 66, "Civil Design / Steel Structures Design", "AJ", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Steel Structures Joint Detail Drawings", true },
                    { 67, "Civil Design / Steel Structures Design", "AM", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Steel Structures Mark Lists", true },
                    { 68, "Civil Design / Steel Structures Design", "AA", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Steel Structures Accounting", true },
                    { 69, "Transportation, Storage, Preservation & Site Installation", "DT", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Shipping and Handling Instructions / Transportation drawings", true },
                    { 70, "Transportation, Storage, Preservation & Site Installation", "DS", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Storage and Preservation Instructions / Drawings", true },
                    { 71, "Transportation, Storage, Preservation & Site Installation", "DI", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Installation, Field Assembling/Fabrication, Precommissioning Instructions/Drawings/List of Materials", true },
                    { 72, "Design Final Documentation", "DB", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Manufacturer Data Book - or any subtype of", true },
                    { 73, "Design Final Documentation", "DL", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Manufacturer Data Book Index of Contents", true },
                    { 74, "Operation and Maintenance", "OI", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Operating Instructions (Start-up and Operation)", true },
                    { 75, "Operation and Maintenance", "OP", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Maintenance Instructions / Procedures / Detailed Drawings", true },
                    { 76, "Operation and Maintenance", "OM", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Installation, Operation and Maintenance Manual - or any subtype of", true },
                    { 77, "Operation and Maintenance", "OL", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Installation, Operation and Maintenance Manual Index of Contents", true },
                    { 78, "Certification of Manufacturing/Erection", "MB", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Certification Dossier", true },
                    { 79, "Certification of Manufacturing/Erection", "ML", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Certification Dossier Index of Contents", true }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VendorDocumentTypologyCodes");

            migrationBuilder.DropColumn(
                name: "TypologyCode",
                table: "VDRItems");

            migrationBuilder.DropColumn(
                name: "VendorFormatDocumentNumber",
                table: "VDRItems");

            migrationBuilder.DropColumn(
                name: "JobNumber",
                table: "Projects");
        }
    }
}
