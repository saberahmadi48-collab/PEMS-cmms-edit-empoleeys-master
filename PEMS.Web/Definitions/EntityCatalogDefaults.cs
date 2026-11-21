using Microsoft.AspNetCore.Identity;
using PEMS.Domain.Entities;
using PEMS.Persistence.Identity;

namespace PEMS.Web.Definitions;

/// <summary>
/// The entities definitions may use (the waves of the migration): one registration per entity with the permission and the roles that guard it today (the same as its controller), and the property names a
/// grid or an option list may read and a create or edit may write. Registering an entity is all the code a master-data page, form and action need; the rest is definition files
/// (<c>tools/definitions/make_entity_pages.py</c>). Entities with business rules express them as <c>deny</c> and <c>unique</c> rules in the action definition, never as a handler.
/// </summary>
internal static class EntityCatalogDefaults
{
    private static readonly string[] AdminRoles = [SystemRoles.SystemAdmin];
    private static readonly string[] OrganizationRoles = [SystemRoles.SystemAdmin, SystemRoles.EngineeringManager];

    public static EntityCatalog Build() => new EntityCatalog()
        // Administration (W1)
        .Add<Department>("DepartmentId", e => e.Roles(OrganizationRoles, OrganizationRoles)
            .Readable("Code", "Name", "Description", "IsActive", "CreatedDate").Writable("Code", "Name", "Description", "IsActive", "CreatedDate"))
        .Add<ApplicationRole>("Id", e => e.Roles(AdminRoles, AdminRoles)
            .Readable("Name", "DisplayName", "Description", "Domain", "Category", "IsSystemRole", "SortOrder").Writable("Name", "DisplayName", "Description")
            .BeforeCreate(r => { r.NormalizedName = r.Name?.ToUpperInvariant(); r.ConcurrencyStamp = Guid.NewGuid().ToString(); }))
        .Add<SecurityEvent>("SecurityEventId", e => e.Roles(AdminRoles, null)
            .Readable("EventType", "Method", "Result", "FailureReason", "ActorUserName", "TargetUserName", "IpAddress", "OccurredAtUtc"))
        .Add<AuditLog>("AuditLogId", e => e.Roles(AdminRoles, null)
            .Readable("EntityName", "EntityKeyValue", "Action", "ChangedFieldsSummary", "UserId", "UserName", "Timestamp", "IsAIAssisted"))
        .Add<AuthorizationAuditLog>("AuthorizationAuditLogId", e => e.Roles(AdminRoles, null)
            .Readable("UserId", "ResourceName", "ActionName", "ResourceId", "ScopeType", "ScopeValue", "Decision", "Reason", "Timestamp", "IpAddress"))
        .Add<DefinitionGrant>("DefinitionGrantId", e => e.Roles(AdminRoles, AdminRoles)   // SCRUM-591: an administrator opens or closes a definition for a role without a deploy
            .Readable("Kind", "DefinitionKey", "RoleName", "Effect", "Reason", "CreatedBy", "CreatedDate").Writable("Kind", "DefinitionKey", "RoleName", "Effect", "Reason"))
        .Add<Project>("ProjectId", e => e   // SCRUM-740: the Projects controller had no permission of its own; the menu rule and the signed-in user decide, as before
            .Readable("ProjectCode", "ProjectName", "ClientName", "Location", "StartDate", "EndDate", "Status", "IsActive", "CreatedDate")
            .Writable("ProjectCode", "ProjectName", "ClientName", "Location", "StartDate", "EndDate", "Status", "IsActive", "CreatedDate"))
        .Add<Discipline>("DisciplineId", e => e.Read("DisciplineMaster.View").Create("DisciplineMaster.Create").Edit("DisciplineMaster.Edit")   // SCRUM-682: reads and writes go through the DisciplineMaster operations; the catalog loads the row into the edit form
            .Readable("Code", "Name", "Description", "ImportAliases", "TransmittalCode", "IsActive", "CreatedDate").Writable("Code", "Name", "Description", "ImportAliases", "TransmittalCode"))
        .Add<SoDPolicy>("SoDPolicyId", e => e.Roles(AdminRoles, AdminRoles)
            .Readable("PermissionAId", "PermissionBId", "Description", "IsActive", "CreatedDate").Writable("PermissionAId", "PermissionBId", "Description", "IsActive", "CreatedDate"))
        .Add<ContractApprovalAuthorityRule>("ContractApprovalAuthorityRuleId", e => e.Roles(AdminRoles, AdminRoles)
            .Readable("MinValue", "MaxValue", "CriticalityClass", "RequiredApproverRole", "Description", "Priority", "IsActive")
            .Writable("MinValue", "MaxValue", "CriticalityClass", "RequiredApproverRole", "Description", "Priority", "IsActive"))
        .Add<RoutingRule>("RoutingRuleId", e => e.Roles(AdminRoles, null).Readable("DocumentCategory", "TargetModule", "AllowAutoRouting", "Description", "IsActive"))
        .Add<AIConfidenceSettings>("AIConfidenceSettingsId", e => e.Roles(AdminRoles, null).Readable("HighThreshold", "MediumThreshold", "ModifiedDate"))
        .Add<PurchaseOrderAuthorityRule>("PurchaseOrderAuthorityRuleId", e => e.Roles(AdminRoles, null).Readable("Level", "MinAmount", "MaxAmount", "Currency", "ApproverRole", "Description", "IsActive"))
        .Add<StorageRoute>("StorageRouteId", e => e.Roles(AdminRoles, null).Readable("Module", "DocumentType", "StorageProviderId", "RelativeRootPath", "IsEnabled", "IsDefault", "Priority"))
        .Add<StorageProvider>("StorageProviderId", e => e.Roles(AdminRoles, null).Readable("Name", "Code", "ProviderType", "Host", "Port", "Username", "RootPath", "BaseUrl", "UseSsl", "UsePassiveMode", "AllowRead", "AllowWrite", "AllowDelete", "IsEnabled", "IsDefault", "LastHealthStatus", "LastHealthCheckAt"))
        // read-only registrations used for related counts and labels
        .Add<Equipment>("EquipmentId", e => e.Read("Equipment.View").Readable("TagNumber", "Name"))   // SCRUM-667: the choices of the AI tag approval form
        .Add<ApplicationUser>("Id", e => e.Roles(AdminRoles, null).Readable("UserName"))
        .Add<Permission>("PermissionId", e => e.Roles(AdminRoles, null).Readable("Name", "IsActive"))
        .Add<Employee>("EmployeeId", e => e.Roles(OrganizationRoles, OrganizationRoles)   // SCRUM-687: the edit form loads the row and entity.update writes the personal fields (the checks are the Employee.ValidateEdit operation)
            .Readable("PersonnelCode", "FirstName", "LastName", "Email", "Phone", "DepartmentId", "DisciplineId", "PersonnelType", "VendorId", "IsActive", "HireDate", "Location", "Shift", "HomeAddress")
            .Writable("PersonnelCode", "FirstName", "LastName", "Email", "Phone", "DepartmentId", "DisciplineId", "PersonnelType", "VendorId", "HireDate", "Location", "Shift", "HomeAddress"))
        .Add<IdentityUserRole<int>>("RoleId", e => e.Roles(AdminRoles, null).Readable("UserId", "RoleId"))
        .Add<RolePermissionGrant>("RolePermissionGrantId", e => e.Roles(AdminRoles, null).Readable("RoleId", "IsActive"))
        // Inventory master data (W4): InventoryMasterData permissions
        .Add<MaterialUOM>("MaterialUOMId", e => e.Create("InventoryMasterData.Create").Edit("InventoryMasterData.Edit")
            .Readable("Code", "Name", "Description", "ConversionFactor", "IsActive", "CreatedDate").Writable("Code", "Name", "Description", "ConversionFactor", "IsActive", "CreatedDate"))
        .Add<MaterialCategory>("MaterialCategoryId", e => e.Create("InventoryMasterData.Create").Edit("InventoryMasterData.Edit")
            .Readable("Code", "Name", "Description", "ParentCategoryId", "IsActive", "CreatedDate").Writable("Code", "Name", "Description", "ParentCategoryId", "IsActive", "CreatedDate"))
        .Add<StorageLocation>("StorageLocationId", e => e.Create("InventoryMasterData.Create").Edit("InventoryMasterData.Edit")
            .Readable("Code", "Name", "WarehouseId", "ParentLocationId", "IsActive", "CreatedDate").Writable("Code", "Name", "WarehouseId", "ParentLocationId", "IsActive", "CreatedDate"))
        // Maintenance master data (W2): MaintenanceMasterData permissions
        .Add<MaintenanceGroup>("MaintenanceGroupId", e => e.Read("MaintenanceMasterData.View").Create("MaintenanceMasterData.Create").Edit("MaintenanceMasterData.Edit")
            .Readable("Code", "Name", "DisciplineId", "ParentMaintenanceGroupId", "Description", "IsSystemDefault", "IsActive", "CreatedDate").Writable("Code", "Name", "DisciplineId", "ParentMaintenanceGroupId", "Description", "IsActive", "CreatedDate"))
        .Add<Skill>("SkillId", e => e.Read("MaintenanceMasterData.View").Create("MaintenanceMasterData.Create").Edit("MaintenanceMasterData.Edit")
            .Readable("Name", "DisciplineId", "Description", "IsActive", "CreatedDate").Writable("Name", "DisciplineId", "Description", "IsActive", "CreatedDate"))
        .Add<Qualification>("QualificationId", e => e.Read("MaintenanceMasterData.View").Create("MaintenanceMasterData.Create").Edit("MaintenanceMasterData.Edit")
            .Readable("Name", "DisciplineId", "Description", "IsActive", "CreatedDate").Writable("Name", "DisciplineId", "Description", "IsActive", "CreatedDate"))
        // Material Master (SCRUM-656): create, edit and activate / deactivate are the generic entity actions; the permissions are the ones of the old controller
        .Add<MaterialItem>("MaterialItemId", e => e.Read("MaterialItem.View").Create("MaterialItem.Create").Edit("MaterialItem.Edit")
            .Readable("MaterialCode", "MaterialType", "Description", "LongDescription", "CategoryId", "DisciplineId", "UomId", "VendorId", "Manufacturer", "ManufacturerPartNumber", "Model", "Size", "MaterialGrade", "Specification", "IsStockItem", "IsCritical", "IsActive", "CreatedDate")
            .Writable("MaterialCode", "MaterialType", "Description", "LongDescription", "CategoryId", "DisciplineId", "UomId", "VendorId", "Manufacturer", "ManufacturerPartNumber", "Model", "Size", "MaterialGrade", "Specification", "IsStockItem", "IsCritical", "IsActive", "CreatedDate"))
        // Vendors (SCRUM-763): create, edit and activate / deactivate are the generic entity actions (the qualification and the risk stay on the vendor services)
        .Add<Vendor>("VendorId", e => e.Read("Vendors.View").Create("Vendors.Create").Edit("Vendors.Edit")
            .Readable("Name", "VendorCode", "Country", "ContactName", "ContactEmail", "ContactPhone", "IsActive", "CreatedDate").Writable("Name", "VendorCode", "Country", "ContactName", "ContactEmail", "ContactPhone", "IsActive", "CreatedDate"))
        // Spare parts (W2): the controller has no permission check beyond signing in
        .Add<SparePart>("SparePartId", e => e.Readable("PartNumber", "PartName", "Manufacturer", "Model", "Unit", "Description", "QuantityOnHand", "MinStockLevel", "ReorderPoint", "StorageLocation", "IsActive", "CreatedDate")
            .Writable("PartNumber", "PartName", "Manufacturer", "Model", "Unit", "Description", "QuantityOnHand", "MinStockLevel", "ReorderPoint", "StorageLocation", "IsActive", "CreatedDate"))
        .Add<Warehouse>("WarehouseId", e => e.Create("InventoryMasterData.Create").Edit("InventoryMasterData.Edit")
            .Readable("Code", "Name", "Description", "IsActive", "CreatedDate").Writable("Code", "Name", "Description", "IsActive", "CreatedDate"));
}
