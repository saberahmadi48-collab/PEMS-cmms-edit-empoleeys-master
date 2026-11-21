using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EquipmentAssetFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FunctionalLocationId",
                table: "Equipments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InstallationDate",
                table: "Equipments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentEquipmentId",
                table: "Equipments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FunctionalLocationId",
                table: "Assets",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_FunctionalLocationId",
                table: "Equipments",
                column: "FunctionalLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_ParentEquipmentId",
                table: "Equipments",
                column: "ParentEquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_FunctionalLocationId",
                table: "Assets",
                column: "FunctionalLocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_FunctionalLocations_FunctionalLocationId",
                table: "Assets",
                column: "FunctionalLocationId",
                principalTable: "FunctionalLocations",
                principalColumn: "FunctionalLocationId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Equipments_Equipments_ParentEquipmentId",
                table: "Equipments",
                column: "ParentEquipmentId",
                principalTable: "Equipments",
                principalColumn: "EquipmentId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Equipments_FunctionalLocations_FunctionalLocationId",
                table: "Equipments",
                column: "FunctionalLocationId",
                principalTable: "FunctionalLocations",
                principalColumn: "FunctionalLocationId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_FunctionalLocations_FunctionalLocationId",
                table: "Assets");

            migrationBuilder.DropForeignKey(
                name: "FK_Equipments_Equipments_ParentEquipmentId",
                table: "Equipments");

            migrationBuilder.DropForeignKey(
                name: "FK_Equipments_FunctionalLocations_FunctionalLocationId",
                table: "Equipments");

            migrationBuilder.DropIndex(
                name: "IX_Equipments_FunctionalLocationId",
                table: "Equipments");

            migrationBuilder.DropIndex(
                name: "IX_Equipments_ParentEquipmentId",
                table: "Equipments");

            migrationBuilder.DropIndex(
                name: "IX_Assets_FunctionalLocationId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "FunctionalLocationId",
                table: "Equipments");

            migrationBuilder.DropColumn(
                name: "InstallationDate",
                table: "Equipments");

            migrationBuilder.DropColumn(
                name: "ParentEquipmentId",
                table: "Equipments");

            migrationBuilder.DropColumn(
                name: "FunctionalLocationId",
                table: "Assets");
        }
    }
}
