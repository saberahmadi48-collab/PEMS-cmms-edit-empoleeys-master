using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PEMS.Persistence.Migrations
{
    /// <summary>
    /// Global Search: SQL Server Full-Text catalog and indexes (Equipments, EngineeringDocuments, VDRItems, Vendors).
    /// Replaces the hand-run EnableFullTextSearch.sql (kept under legacy-trash/). Full-text objects cannot be created
    /// inside a transaction, hence suppressTransaction. When Full-Text Search is not installed on the SQL Server the
    /// statements do nothing and the application falls back to LIKE queries; install the feature BEFORE the first
    /// run, because an applied migration is not re-run.
    /// </summary>
    public partial class FullTextSearch : Migration
    {
        private const string Catalog = "PEMSFullTextCatalog";

        private const string FullTextInstalled = "(SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')) = 1";

        private static readonly (string Table, string Columns, string PrimaryKey)[] Targets =
        {
            ("Equipments", "TagNumber, Name, Description, Manufacturer", "PK_Equipments"),
            ("EngineeringDocuments", "DocumentNo, Title, Description", "PK_EngineeringDocuments"),
            ("VDRItems", "DocumentNumber, DocumentDescription", "PK_VDRItems"),
            ("Vendors", "Name", "PK_Vendors"),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
IF {FullTextInstalled}
   AND NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = '{Catalog}')
    CREATE FULLTEXT CATALOG {Catalog} AS DEFAULT;", suppressTransaction: true);

            foreach (var (table, columns, primaryKey) in Targets)
            {
                migrationBuilder.Sql($@"
IF {FullTextInstalled}
   AND EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = '{Catalog}')
   AND EXISTS (SELECT 1 FROM sys.tables WHERE name = '{table}' AND SCHEMA_NAME(schema_id) = 'dbo')
   AND NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.{table}'))
   AND EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = '{primaryKey}')
    CREATE FULLTEXT INDEX ON dbo.{table}({columns})
        KEY INDEX {primaryKey} ON {Catalog}
        WITH CHANGE_TRACKING AUTO;", suppressTransaction: true);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, _, _) in Targets)
            {
                migrationBuilder.Sql($@"
IF {FullTextInstalled}
   AND EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.{table}'))
    DROP FULLTEXT INDEX ON dbo.{table};", suppressTransaction: true);
            }

            migrationBuilder.Sql($@"
IF {FullTextInstalled}
   AND EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = '{Catalog}')
    DROP FULLTEXT CATALOG {Catalog};", suppressTransaction: true);
        }
    }
}
