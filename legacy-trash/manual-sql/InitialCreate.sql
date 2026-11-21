IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [AIConfidenceSettings] (
    [AIConfidenceSettingsId] int NOT NULL IDENTITY,
    [HighThreshold] float NOT NULL,
    [MediumThreshold] float NOT NULL,
    [ModifiedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_AIConfidenceSettings] PRIMARY KEY ([AIConfidenceSettingsId])
);

CREATE TABLE [AspNetRoles] (
    [Id] int NOT NULL IDENTITY,
    [DisplayName] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetUsers] (
    [Id] int NOT NULL IDENTITY,
    [FullName] nvarchar(max) NOT NULL,
    [EmployeeId] int NULL,
    [DisciplineId] int NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [LastLoginDate] datetime2 NULL,
    [MustChangePassword] bit NOT NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);

CREATE TABLE [AuditLogs] (
    [AuditLogId] int NOT NULL IDENTITY,
    [EntityName] nvarchar(max) NOT NULL,
    [EntityKeyValue] nvarchar(max) NOT NULL,
    [Action] nvarchar(max) NOT NULL,
    [ChangedFieldsSummary] nvarchar(max) NULL,
    [UserId] int NULL,
    [UserName] nvarchar(max) NULL,
    [Timestamp] datetime2 NOT NULL,
    [IsAIAssisted] bit NOT NULL,
    CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([AuditLogId])
);

CREATE TABLE [Cables] (
    [CableId] int NOT NULL IDENTITY,
    [CableNumber] nvarchar(max) NOT NULL,
    [CableType] nvarchar(max) NOT NULL,
    [FromEquipment] nvarchar(max) NULL,
    [ToEquipment] nvarchar(max) NULL,
    [VoltageLevel] nvarchar(max) NULL,
    [Length] decimal(18,3) NULL,
    [Size] nvarchar(max) NULL,
    [CoreNumber] int NULL,
    [InstallationMethod] nvarchar(max) NULL,
    [CableTray] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Cables] PRIMARY KEY ([CableId])
);

CREATE TABLE [ChatMessages] (
    [ChatMessageId] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [Role] nvarchar(max) NOT NULL,
    [Content] nvarchar(max) NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_ChatMessages] PRIMARY KEY ([ChatMessageId])
);

CREATE TABLE [Departments] (
    [DepartmentId] int NOT NULL IDENTITY,
    [Code] nvarchar(30) NOT NULL,
    [Name] nvarchar(150) NOT NULL,
    [Description] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_Departments] PRIMARY KEY ([DepartmentId])
);

CREATE TABLE [Disciplines] (
    [DisciplineId] int NOT NULL IDENTITY,
    [Code] nvarchar(max) NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    [ImportAliases] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_Disciplines] PRIMARY KEY ([DisciplineId])
);

CREATE TABLE [EngineeringRelationships] (
    [EngineeringRelationshipId] int NOT NULL IDENTITY,
    [SourceType] nvarchar(max) NOT NULL,
    [SourceId] int NOT NULL,
    [TargetType] nvarchar(max) NOT NULL,
    [TargetId] int NOT NULL,
    [RelationshipType] nvarchar(max) NOT NULL,
    [Confidence] float NULL,
    [IsAIProposed] bit NOT NULL,
    [ReviewStatus] nvarchar(max) NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_EngineeringRelationships] PRIMARY KEY ([EngineeringRelationshipId])
);

CREATE TABLE [FunctionalLocations] (
    [FunctionalLocationId] int NOT NULL IDENTITY,
    [Code] nvarchar(max) NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [Plant] nvarchar(max) NULL,
    [Area] nvarchar(max) NULL,
    [Unit] nvarchar(max) NULL,
    [ParentId] int NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_FunctionalLocations] PRIMARY KEY ([FunctionalLocationId]),
    CONSTRAINT [FK_FunctionalLocations_FunctionalLocations_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [FunctionalLocations] ([FunctionalLocationId]) ON DELETE NO ACTION
);

CREATE TABLE [KnowledgeBaseEntries] (
    [KnowledgeBaseEntryId] int NOT NULL IDENTITY,
    [SourceType] nvarchar(450) NOT NULL,
    [SourceId] int NOT NULL,
    [Content] nvarchar(max) NOT NULL,
    [EmbeddingJson] nvarchar(max) NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [UpdatedDate] datetime2 NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_KnowledgeBaseEntries] PRIMARY KEY ([KnowledgeBaseEntryId])
);

CREATE TABLE [Notifications] (
    [NotificationId] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [Title] nvarchar(max) NOT NULL,
    [Message] nvarchar(max) NOT NULL,
    [LinkUrl] nvarchar(max) NULL,
    [RelatedType] nvarchar(max) NULL,
    [RelatedId] int NULL,
    [IsRead] bit NOT NULL,
    [ReadDate] datetime2 NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_Notifications] PRIMARY KEY ([NotificationId])
);

CREATE TABLE [Panels] (
    [PanelId] int NOT NULL IDENTITY,
    [PanelNumber] nvarchar(max) NOT NULL,
    [PanelType] nvarchar(max) NOT NULL,
    [Location] nvarchar(max) NULL,
    [Manufacturer] nvarchar(max) NULL,
    [Voltage] nvarchar(max) NULL,
    [IPRating] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Panels] PRIMARY KEY ([PanelId])
);

CREATE TABLE [PipeSpecifications] (
    [PipeSpecificationId] int NOT NULL IDENTITY,
    [SpecCode] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    [Material] nvarchar(max) NULL,
    [Rating] nvarchar(max) NULL,
    [DesignPressure] decimal(18,3) NULL,
    [DesignTemperature] decimal(18,3) NULL,
    [Service] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_PipeSpecifications] PRIMARY KEY ([PipeSpecificationId])
);

CREATE TABLE [PipingComponents] (
    [PipingComponentId] int NOT NULL IDENTITY,
    [TagNumber] nvarchar(max) NOT NULL,
    [ComponentType] nvarchar(max) NOT NULL,
    [Size] nvarchar(max) NULL,
    [Material] nvarchar(max) NULL,
    [Rating] nvarchar(max) NULL,
    [LineNumber] nvarchar(max) NULL,
    [Specification] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_PipingComponents] PRIMARY KEY ([PipingComponentId])
);

CREATE TABLE [PipingLines] (
    [PipingLineId] int NOT NULL IDENTITY,
    [LineNumber] nvarchar(max) NOT NULL,
    [Service] nvarchar(max) NULL,
    [Fluid] nvarchar(max) NULL,
    [Size] nvarchar(max) NULL,
    [PipeClass] nvarchar(max) NULL,
    [DesignPressure] decimal(18,3) NULL,
    [DesignTemperature] decimal(18,3) NULL,
    [FromEquipment] nvarchar(max) NULL,
    [ToEquipment] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_PipingLines] PRIMARY KEY ([PipingLineId])
);

CREATE TABLE [Projects] (
    [ProjectId] int NOT NULL IDENTITY,
    [ProjectCode] nvarchar(max) NOT NULL,
    [ProjectName] nvarchar(max) NOT NULL,
    [ClientName] nvarchar(max) NULL,
    [Location] nvarchar(max) NULL,
    [StartDate] datetime2 NULL,
    [EndDate] datetime2 NULL,
    [Status] nvarchar(max) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_Projects] PRIMARY KEY ([ProjectId])
);

CREATE TABLE [QCDisciplines] (
    [QCDisciplineId] int NOT NULL IDENTITY,
    [Code] nvarchar(30) NOT NULL,
    [Name] nvarchar(150) NOT NULL,
    [Description] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_QCDisciplines] PRIMARY KEY ([QCDisciplineId])
);

CREATE TABLE [RolePermissions] (
    [RolePermissionId] int NOT NULL IDENTITY,
    [RoleName] nvarchar(450) NOT NULL,
    [ModuleName] nvarchar(450) NOT NULL,
    [CanView] bit NOT NULL,
    [CanCreate] bit NOT NULL,
    [CanEdit] bit NOT NULL,
    [CanDelete] bit NOT NULL,
    [CanApprove] bit NOT NULL,
    [CanManualNumber] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([RolePermissionId])
);

CREATE TABLE [RoutingRules] (
    [RoutingRuleId] int NOT NULL IDENTITY,
    [DocumentCategory] nvarchar(max) NOT NULL,
    [TargetModule] nvarchar(max) NOT NULL,
    [AllowAutoRouting] bit NOT NULL,
    [Description] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_RoutingRules] PRIMARY KEY ([RoutingRuleId])
);

CREATE TABLE [SavedFilters] (
    [SavedFilterId] int NOT NULL IDENTITY,
    [ApplicationUserId] int NOT NULL,
    [PageKey] nvarchar(450) NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [QueryString] nvarchar(max) NOT NULL,
    [IsDefault] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_SavedFilters] PRIMARY KEY ([SavedFilterId])
);

CREATE TABLE [SpareParts] (
    [SparePartId] int NOT NULL IDENTITY,
    [PartNumber] nvarchar(max) NOT NULL,
    [PartName] nvarchar(max) NOT NULL,
    [Manufacturer] nvarchar(max) NULL,
    [Model] nvarchar(max) NULL,
    [Unit] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [QuantityOnHand] decimal(18,2) NOT NULL,
    [MinStockLevel] decimal(18,2) NULL,
    [ReorderPoint] decimal(18,2) NULL,
    [StorageLocation] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_SpareParts] PRIMARY KEY ([SparePartId])
);

CREATE TABLE [Tags] (
    [TagId] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    [Color] nvarchar(max) NOT NULL,
    [CreatedById] int NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_Tags] PRIMARY KEY ([TagId])
);

CREATE TABLE [UserDashboardWidgets] (
    [UserDashboardWidgetId] int NOT NULL IDENTITY,
    [ApplicationUserId] int NOT NULL,
    [WidgetKey] nvarchar(450) NOT NULL,
    [DisplayOrder] int NOT NULL,
    [Size] nvarchar(max) NOT NULL,
    [IsVisible] bit NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_UserDashboardWidgets] PRIMARY KEY ([UserDashboardWidgetId])
);

CREATE TABLE [Users] (
    [UserId] int NOT NULL IDENTITY,
    [EmployeeNo] nvarchar(max) NOT NULL,
    [FirstName] nvarchar(max) NOT NULL,
    [LastName] nvarchar(max) NOT NULL,
    [Username] nvarchar(max) NOT NULL,
    [PasswordHash] nvarchar(max) NOT NULL,
    [Email] nvarchar(max) NULL,
    [Mobile] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([UserId])
);

CREATE TABLE [Valves] (
    [ValveId] int NOT NULL IDENTITY,
    [TagNumber] nvarchar(max) NOT NULL,
    [ValveType] nvarchar(max) NOT NULL,
    [Size] nvarchar(max) NULL,
    [Rating] nvarchar(max) NULL,
    [Material] nvarchar(max) NULL,
    [ActuatorType] nvarchar(max) NULL,
    [FailPosition] nvarchar(max) NULL,
    [LineNumber] nvarchar(max) NULL,
    [Manufacturer] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Valves] PRIMARY KEY ([ValveId])
);

CREATE TABLE [Vendors] (
    [VendorId] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    [Country] nvarchar(max) NULL,
    [ContactName] nvarchar(max) NULL,
    [ContactEmail] nvarchar(max) NULL,
    [ContactPhone] nvarchar(max) NULL,
    [RiskLevel] nvarchar(max) NOT NULL,
    [RiskScore] float(5) NULL,
    [RiskAssessedDate] datetime2 NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_Vendors] PRIMARY KEY ([VendorId])
);

CREATE TABLE [WorkflowDefinitions] (
    [WorkflowDefinitionId] int NOT NULL IDENTITY,
    [Name] nvarchar(200) NOT NULL,
    [Category] nvarchar(100) NOT NULL,
    [Module] nvarchar(100) NOT NULL,
    [Description] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedById] int NULL,
    CONSTRAINT [PK_WorkflowDefinitions] PRIMARY KEY ([WorkflowDefinitionId])
);

CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] int NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] int NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserRoles] (
    [UserId] int NOT NULL,
    [RoleId] int NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserTokens] (
    [UserId] int NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [CableConnections] (
    [ConnectionId] int NOT NULL IDENTITY,
    [CableId] int NOT NULL,
    [FromPanel] nvarchar(max) NULL,
    [FromTerminal] nvarchar(max) NULL,
    [ToEquipment] nvarchar(max) NULL,
    [ToTerminal] nvarchar(max) NULL,
    [ConnectionType] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_CableConnections] PRIMARY KEY ([ConnectionId]),
    CONSTRAINT [FK_CableConnections_Cables_CableId] FOREIGN KEY ([CableId]) REFERENCES [Cables] ([CableId]) ON DELETE CASCADE
);

CREATE TABLE [DocumentIdcReferrals] (
    [DocumentIdcReferralId] int NOT NULL IDENTITY,
    [DocumentId] int NOT NULL,
    [DisciplineId] int NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedById] int NULL,
    CONSTRAINT [PK_DocumentIdcReferrals] PRIMARY KEY ([DocumentIdcReferralId]),
    CONSTRAINT [FK_DocumentIdcReferrals_Disciplines_DisciplineId] FOREIGN KEY ([DisciplineId]) REFERENCES [Disciplines] ([DisciplineId]) ON DELETE NO ACTION
);

CREATE TABLE [Employees] (
    [EmployeeId] int NOT NULL IDENTITY,
    [PersonnelCode] nvarchar(50) NULL,
    [FirstName] nvarchar(100) NOT NULL,
    [LastName] nvarchar(100) NOT NULL,
    [Position] nvarchar(max) NULL,
    [Email] nvarchar(max) NULL,
    [Phone] nvarchar(max) NULL,
    [DepartmentId] int NOT NULL,
    [DisciplineId] int NOT NULL,
    [IsActive] bit NOT NULL,
    [PhotoPath] nvarchar(max) NULL,
    [SignaturePath] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_Employees] PRIMARY KEY ([EmployeeId]),
    CONSTRAINT [FK_Employees_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([DepartmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Employees_Disciplines_DisciplineId] FOREIGN KEY ([DisciplineId]) REFERENCES [Disciplines] ([DisciplineId]) ON DELETE NO ACTION
);

CREATE TABLE [MaintenanceRoles] (
    [MaintenanceRoleId] int NOT NULL IDENTITY,
    [Name] nvarchar(150) NOT NULL,
    [DisciplineId] int NOT NULL,
    [Level] nvarchar(20) NOT NULL,
    [IsCrewLeadRole] bit NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_MaintenanceRoles] PRIMARY KEY ([MaintenanceRoleId]),
    CONSTRAINT [FK_MaintenanceRoles_Disciplines_DisciplineId] FOREIGN KEY ([DisciplineId]) REFERENCES [Disciplines] ([DisciplineId]) ON DELETE NO ACTION
);

CREATE TABLE [Documents] (
    [DocumentId] int NOT NULL IDENTITY,
    [DocumentKind] nvarchar(max) NOT NULL,
    [DocumentNo] nvarchar(max) NOT NULL,
    [Title] nvarchar(max) NOT NULL,
    [DocumentType] nvarchar(max) NOT NULL,
    [ProjectId] int NOT NULL,
    [DisciplineId] int NULL,
    [Area] nvarchar(max) NULL,
    [RevisionNo] nvarchar(max) NULL,
    [Status] nvarchar(450) NOT NULL,
    [CommentStatus] nvarchar(max) NULL,
    [IsFrozen] bit NOT NULL,
    [FrozenDate] datetime2 NULL,
    [PlannedDate] datetime2 NULL,
    [ActualDate] datetime2 NULL,
    [AIWorkflowStage] nvarchar(max) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [ModifiedDate] datetime2 NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_Documents] PRIMARY KEY ([DocumentId]),
    CONSTRAINT [FK_Documents_Disciplines_DisciplineId] FOREIGN KEY ([DisciplineId]) REFERENCES [Disciplines] ([DisciplineId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Documents_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE NO ACTION
);

CREATE TABLE [MDRImportBatches] (
    [MDRImportBatchId] int NOT NULL IDENTITY,
    [FileName] nvarchar(max) NOT NULL,
    [ProjectId] int NOT NULL,
    [ImportedById] int NULL,
    [ImportDate] datetime2 NOT NULL,
    [RowsTotal] int NOT NULL,
    [RowsValid] int NOT NULL,
    [RowsWarning] int NOT NULL,
    [RowsError] int NOT NULL,
    [RowsDuplicate] int NOT NULL,
    [Status] nvarchar(max) NOT NULL,
    [CommittedDate] datetime2 NULL,
    [HeaderWarningsJson] nvarchar(max) NULL,
    CONSTRAINT [PK_MDRImportBatches] PRIMARY KEY ([MDRImportBatchId]),
    CONSTRAINT [FK_MDRImportBatches_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE NO ACTION
);

CREATE TABLE [Meetings] (
    [MeetingId] int NOT NULL IDENTITY,
    [Title] nvarchar(max) NOT NULL,
    [MeetingDate] datetime2 NOT NULL,
    [Location] nvarchar(max) NULL,
    [OrganizerName] nvarchar(max) NULL,
    [ProjectId] int NULL,
    [Agenda] nvarchar(max) NULL,
    [MinutesNotes] nvarchar(max) NULL,
    [Status] nvarchar(max) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_Meetings] PRIMARY KEY ([MeetingId]),
    CONSTRAINT [FK_Meetings_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE NO ACTION
);

CREATE TABLE [ProcurementPackages] (
    [ProcurementPackageId] int NOT NULL IDENTITY,
    [PackageNumber] nvarchar(max) NOT NULL,
    [ProjectId] int NOT NULL,
    [DisciplineId] int NULL,
    [Title] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    [Stage] nvarchar(max) NOT NULL,
    [Status] nvarchar(max) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [ClosedDate] datetime2 NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_ProcurementPackages] PRIMARY KEY ([ProcurementPackageId]),
    CONSTRAINT [FK_ProcurementPackages_Disciplines_DisciplineId] FOREIGN KEY ([DisciplineId]) REFERENCES [Disciplines] ([DisciplineId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProcurementPackages_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE NO ACTION
);

CREATE TABLE [VDRImportBatches] (
    [VDRImportBatchId] int NOT NULL IDENTITY,
    [FileName] nvarchar(max) NOT NULL,
    [ProjectId] int NOT NULL,
    [ImportedById] int NULL,
    [ImportDate] datetime2 NOT NULL,
    [RowsTotal] int NOT NULL,
    [RowsValid] int NOT NULL,
    [RowsWarning] int NOT NULL,
    [RowsError] int NOT NULL,
    [RowsDuplicate] int NOT NULL,
    [Status] nvarchar(max) NOT NULL,
    [CommittedDate] datetime2 NULL,
    CONSTRAINT [PK_VDRImportBatches] PRIMARY KEY ([VDRImportBatchId]),
    CONSTRAINT [FK_VDRImportBatches_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE NO ACTION
);

CREATE TABLE [Contracts] (
    [ContractId] int NOT NULL IDENTITY,
    [ContractNo] nvarchar(max) NOT NULL,
    [ProjectId] int NOT NULL,
    [VendorId] int NOT NULL,
    [ScopeOfWork] nvarchar(max) NULL,
    [ContractValue] decimal(18,2) NULL,
    [Currency] nvarchar(max) NULL,
    [StartDate] datetime2 NULL,
    [EndDate] datetime2 NULL,
    [Status] nvarchar(max) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_Contracts] PRIMARY KEY ([ContractId]),
    CONSTRAINT [FK_Contracts_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Contracts_Vendors_VendorId] FOREIGN KEY ([VendorId]) REFERENCES [Vendors] ([VendorId]) ON DELETE NO ACTION
);

CREATE TABLE [WorkflowVersions] (
    [WorkflowVersionId] int NOT NULL IDENTITY,
    [WorkflowDefinitionId] int NOT NULL,
    [VersionNumber] int NOT NULL,
    [Status] nvarchar(30) NOT NULL,
    [EffectiveDate] datetime2 NULL,
    [PublishedDate] datetime2 NULL,
    [PublishedById] int NULL,
    CONSTRAINT [PK_WorkflowVersions] PRIMARY KEY ([WorkflowVersionId]),
    CONSTRAINT [FK_WorkflowVersions_WorkflowDefinitions_WorkflowDefinitionId] FOREIGN KEY ([WorkflowDefinitionId]) REFERENCES [WorkflowDefinitions] ([WorkflowDefinitionId]) ON DELETE NO ACTION
);

CREATE TABLE [DisciplineTasks] (
    [DisciplineTaskId] int NOT NULL IDENTITY,
    [DisciplineId] int NOT NULL,
    [ProjectId] int NULL,
    [Title] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    [Type] nvarchar(max) NOT NULL,
    [ResponsibleEmployeeId] int NULL,
    [DueDate] datetime2 NULL,
    [Status] nvarchar(max) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CompletedDate] datetime2 NULL,
    CONSTRAINT [PK_DisciplineTasks] PRIMARY KEY ([DisciplineTaskId]),
    CONSTRAINT [FK_DisciplineTasks_Disciplines_DisciplineId] FOREIGN KEY ([DisciplineId]) REFERENCES [Disciplines] ([DisciplineId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DisciplineTasks_Employees_ResponsibleEmployeeId] FOREIGN KEY ([ResponsibleEmployeeId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DisciplineTasks_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE NO ACTION
);

CREATE TABLE [EmployeeLaborRates] (
    [EmployeeLaborRateId] int NOT NULL IDENTITY,
    [EmployeeId] int NOT NULL,
    [RateType] nvarchar(20) NOT NULL,
    [Rate] decimal(18,2) NOT NULL,
    [Currency] nvarchar(10) NOT NULL,
    [EffectiveFrom] datetime2 NOT NULL,
    [EffectiveTo] datetime2 NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_EmployeeLaborRates] PRIMARY KEY ([EmployeeLaborRateId]),
    CONSTRAINT [FK_EmployeeLaborRates_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION
);

CREATE TABLE [EquipmentImportBatches] (
    [EquipmentImportBatchId] int NOT NULL IDENTITY,
    [ProjectId] int NOT NULL,
    [FileName] nvarchar(max) NOT NULL,
    [ImportedById] int NULL,
    [ImportDate] datetime2 NOT NULL,
    [Status] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_EquipmentImportBatches] PRIMARY KEY ([EquipmentImportBatchId]),
    CONSTRAINT [FK_EquipmentImportBatches_Employees_ImportedById] FOREIGN KEY ([ImportedById]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_EquipmentImportBatches_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE NO ACTION
);

CREATE TABLE [TaskItems] (
    [TaskItemId] int NOT NULL IDENTITY,
    [Title] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    [AssignedToEmployeeId] int NULL,
    [AssignedByApplicationUserId] int NULL,
    [AssignedByName] nvarchar(max) NULL,
    [DueDate] datetime2 NULL,
    [Priority] nvarchar(max) NOT NULL,
    [Status] nvarchar(max) NOT NULL,
    [RelatedType] nvarchar(450) NULL,
    [RelatedId] int NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CompletedDate] datetime2 NULL,
    [CompletionNote] nvarchar(max) NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_TaskItems] PRIMARY KEY ([TaskItemId]),
    CONSTRAINT [FK_TaskItems_Employees_AssignedToEmployeeId] FOREIGN KEY ([AssignedToEmployeeId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION
);

CREATE TABLE [Teams] (
    [TeamId] int NOT NULL IDENTITY,
    [Code] nvarchar(30) NOT NULL,
    [Name] nvarchar(150) NOT NULL,
    [DisciplineId] int NOT NULL,
    [SupervisorEmployeeId] int NULL,
    [Shift] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_Teams] PRIMARY KEY ([TeamId]),
    CONSTRAINT [FK_Teams_Disciplines_DisciplineId] FOREIGN KEY ([DisciplineId]) REFERENCES [Disciplines] ([DisciplineId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Teams_Employees_SupervisorEmployeeId] FOREIGN KEY ([SupervisorEmployeeId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION
);

CREATE TABLE [Transmittals] (
    [TransmittalId] int NOT NULL IDENTITY,
    [TransmittalNo] nvarchar(max) NOT NULL,
    [ProjectId] int NOT NULL,
    [From] nvarchar(max) NOT NULL,
    [To] nvarchar(max) NOT NULL,
    [Purpose] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    [Status] nvarchar(max) NOT NULL,
    [TransmittalDate] datetime2 NOT NULL,
    [SentById] int NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_Transmittals] PRIMARY KEY ([TransmittalId]),
    CONSTRAINT [FK_Transmittals_Employees_SentById] FOREIGN KEY ([SentById]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Transmittals_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE NO ACTION
);

CREATE TABLE [MDRImportRows] (
    [MDRImportRowId] int NOT NULL IDENTITY,
    [MDRImportBatchId] int NOT NULL,
    [RowNumber] int NOT NULL,
    [DocumentNumber] nvarchar(max) NOT NULL,
    [SheetName] nvarchar(max) NULL,
    [PayloadJson] nvarchar(max) NOT NULL,
    [ValidationStatus] nvarchar(max) NOT NULL,
    [ValidationMessage] nvarchar(max) NULL,
    [IsDuplicate] bit NOT NULL,
    [IncludeInCommit] bit NOT NULL,
    [CreatedDocumentId] int NULL,
    CONSTRAINT [PK_MDRImportRows] PRIMARY KEY ([MDRImportRowId]),
    CONSTRAINT [FK_MDRImportRows_MDRImportBatches_MDRImportBatchId] FOREIGN KEY ([MDRImportBatchId]) REFERENCES [MDRImportBatches] ([MDRImportBatchId]) ON DELETE CASCADE
);

CREATE TABLE [MeetingAttendees] (
    [MeetingAttendeeId] int NOT NULL IDENTITY,
    [MeetingId] int NOT NULL,
    [EmployeeId] int NULL,
    [ExternalName] nvarchar(max) NULL,
    [IsRequired] bit NOT NULL,
    [Attended] bit NOT NULL,
    CONSTRAINT [PK_MeetingAttendees] PRIMARY KEY ([MeetingAttendeeId]),
    CONSTRAINT [FK_MeetingAttendees_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MeetingAttendees_Meetings_MeetingId] FOREIGN KEY ([MeetingId]) REFERENCES [Meetings] ([MeetingId]) ON DELETE CASCADE
);

CREATE TABLE [ProcurementApprovals] (
    [ProcurementApprovalId] int NOT NULL IDENTITY,
    [ProcurementPackageId] int NOT NULL,
    [ApproverName] nvarchar(max) NOT NULL,
    [Decision] nvarchar(max) NOT NULL,
    [Comment] nvarchar(max) NULL,
    [ApprovalDate] datetime2 NOT NULL,
    CONSTRAINT [PK_ProcurementApprovals] PRIMARY KEY ([ProcurementApprovalId]),
    CONSTRAINT [FK_ProcurementApprovals_ProcurementPackages_ProcurementPackageId] FOREIGN KEY ([ProcurementPackageId]) REFERENCES [ProcurementPackages] ([ProcurementPackageId]) ON DELETE CASCADE
);

CREATE TABLE [VDRImportRows] (
    [VDRImportRowId] int NOT NULL IDENTITY,
    [VDRImportBatchId] int NOT NULL,
    [RowNumber] int NOT NULL,
    [DocumentNumber] nvarchar(max) NOT NULL,
    [VendorNameRaw] nvarchar(max) NULL,
    [PayloadJson] nvarchar(max) NOT NULL,
    [ValidationStatus] nvarchar(max) NOT NULL,
    [ValidationMessage] nvarchar(max) NULL,
    [IsDuplicate] bit NOT NULL,
    [IncludeInCommit] bit NOT NULL,
    [CreatedVDRItemId] int NULL,
    CONSTRAINT [PK_VDRImportRows] PRIMARY KEY ([VDRImportRowId]),
    CONSTRAINT [FK_VDRImportRows_VDRImportBatches_VDRImportBatchId] FOREIGN KEY ([VDRImportBatchId]) REFERENCES [VDRImportBatches] ([VDRImportBatchId]) ON DELETE CASCADE
);

CREATE TABLE [ContractDocuments] (
    [DocumentId] int NOT NULL,
    [ContractId] int NOT NULL,
    [ClauseReference] nvarchar(max) NULL,
    CONSTRAINT [PK_ContractDocuments] PRIMARY KEY ([DocumentId]),
    CONSTRAINT [FK_ContractDocuments_Contracts_ContractId] FOREIGN KEY ([ContractId]) REFERENCES [Contracts] ([ContractId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ContractDocuments_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([DocumentId]) ON DELETE CASCADE
);

CREATE TABLE [PurchaseOrders] (
    [PoId] int NOT NULL IDENTITY,
    [PoNumber] nvarchar(max) NOT NULL,
    [ProjectId] int NOT NULL,
    [VendorId] int NOT NULL,
    [ContractId] int NULL,
    [Description] nvarchar(max) NULL,
    [Value] decimal(18,2) NULL,
    [Currency] nvarchar(max) NULL,
    [Status] nvarchar(max) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_PurchaseOrders] PRIMARY KEY ([PoId]),
    CONSTRAINT [FK_PurchaseOrders_Contracts_ContractId] FOREIGN KEY ([ContractId]) REFERENCES [Contracts] ([ContractId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PurchaseOrders_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PurchaseOrders_Vendors_VendorId] FOREIGN KEY ([VendorId]) REFERENCES [Vendors] ([VendorId]) ON DELETE NO ACTION
);

CREATE TABLE [WorkflowStages] (
    [WorkflowStageId] int NOT NULL IDENTITY,
    [WorkflowVersionId] int NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [StageOrder] int NOT NULL,
    [StageType] nvarchar(30) NOT NULL,
    [ResponsibleRole] nvarchar(100) NULL,
    [ApprovalStrategy] nvarchar(30) NOT NULL,
    [MinimumApprovalCount] int NULL,
    [IsMandatory] bit NOT NULL,
    [IsSkippable] bit NOT NULL,
    [IsDelegatable] bit NOT NULL,
    [IsEscalatable] bit NOT NULL,
    [TimeoutHours] int NULL,
    [Color] nvarchar(max) NULL,
    [Icon] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    CONSTRAINT [PK_WorkflowStages] PRIMARY KEY ([WorkflowStageId]),
    CONSTRAINT [FK_WorkflowStages_WorkflowVersions_WorkflowVersionId] FOREIGN KEY ([WorkflowVersionId]) REFERENCES [WorkflowVersions] ([WorkflowVersionId]) ON DELETE NO ACTION
);

CREATE TABLE [EquipmentImportRows] (
    [EquipmentImportRowId] int NOT NULL IDENTITY,
    [EquipmentImportBatchId] int NOT NULL,
    [RowNumber] int NOT NULL,
    [PayloadJson] nvarchar(max) NOT NULL,
    [IncludeInCommit] bit NOT NULL,
    [ValidationStatus] nvarchar(max) NOT NULL,
    [ValidationMessage] nvarchar(max) NULL,
    [IsUpdate] bit NOT NULL,
    [CreatedEquipmentId] int NULL,
    CONSTRAINT [PK_EquipmentImportRows] PRIMARY KEY ([EquipmentImportRowId]),
    CONSTRAINT [FK_EquipmentImportRows_EquipmentImportBatches_EquipmentImportBatchId] FOREIGN KEY ([EquipmentImportBatchId]) REFERENCES [EquipmentImportBatches] ([EquipmentImportBatchId]) ON DELETE CASCADE
);

CREATE TABLE [TeamMembers] (
    [TeamMemberId] int NOT NULL IDENTITY,
    [TeamId] int NOT NULL,
    [EmployeeId] int NOT NULL,
    [RoleId] int NOT NULL,
    [JoinedDate] datetime2 NOT NULL,
    [LeftDate] datetime2 NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_TeamMembers] PRIMARY KEY ([TeamMemberId]),
    CONSTRAINT [FK_TeamMembers_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TeamMembers_MaintenanceRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [MaintenanceRoles] ([MaintenanceRoleId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TeamMembers_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([TeamId]) ON DELETE NO ACTION
);

CREATE TABLE [TransmittalItems] (
    [TransmittalItemId] int NOT NULL IDENTITY,
    [TransmittalId] int NOT NULL,
    [DocumentId] int NOT NULL,
    [RevisionNo] nvarchar(max) NOT NULL,
    [Remarks] nvarchar(max) NULL,
    CONSTRAINT [PK_TransmittalItems] PRIMARY KEY ([TransmittalItemId]),
    CONSTRAINT [FK_TransmittalItems_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([DocumentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TransmittalItems_Transmittals_TransmittalId] FOREIGN KEY ([TransmittalId]) REFERENCES [Transmittals] ([TransmittalId]) ON DELETE CASCADE
);

CREATE TABLE [WorkflowInstances] (
    [WorkflowInstanceId] int NOT NULL IDENTITY,
    [WorkflowVersionId] int NOT NULL,
    [Module] nvarchar(100) NOT NULL,
    [BusinessObjectType] nvarchar(100) NOT NULL,
    [BusinessObjectId] int NOT NULL,
    [ProjectId] int NULL,
    [CurrentStageId] int NULL,
    [CurrentOwnerId] int NULL,
    [Status] nvarchar(30) NOT NULL,
    [Priority] nvarchar(max) NULL,
    [StartDate] datetime2 NOT NULL,
    [CompletionDate] datetime2 NULL,
    [InitiatedById] int NULL,
    CONSTRAINT [PK_WorkflowInstances] PRIMARY KEY ([WorkflowInstanceId]),
    CONSTRAINT [FK_WorkflowInstances_WorkflowStages_CurrentStageId] FOREIGN KEY ([CurrentStageId]) REFERENCES [WorkflowStages] ([WorkflowStageId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkflowInstances_WorkflowVersions_WorkflowVersionId] FOREIGN KEY ([WorkflowVersionId]) REFERENCES [WorkflowVersions] ([WorkflowVersionId]) ON DELETE NO ACTION
);

CREATE TABLE [WorkflowTransitions] (
    [WorkflowTransitionId] int NOT NULL IDENTITY,
    [WorkflowVersionId] int NOT NULL,
    [FromStageId] int NULL,
    [ToStageId] int NULL,
    [ActionType] nvarchar(50) NOT NULL,
    [ConditionExpression] nvarchar(max) NULL,
    [Priority] int NOT NULL,
    CONSTRAINT [PK_WorkflowTransitions] PRIMARY KEY ([WorkflowTransitionId]),
    CONSTRAINT [FK_WorkflowTransitions_WorkflowStages_FromStageId] FOREIGN KEY ([FromStageId]) REFERENCES [WorkflowStages] ([WorkflowStageId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkflowTransitions_WorkflowStages_ToStageId] FOREIGN KEY ([ToStageId]) REFERENCES [WorkflowStages] ([WorkflowStageId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkflowTransitions_WorkflowVersions_WorkflowVersionId] FOREIGN KEY ([WorkflowVersionId]) REFERENCES [WorkflowVersions] ([WorkflowVersionId]) ON DELETE NO ACTION
);

CREATE TABLE [WorkflowHistories] (
    [WorkflowHistoryId] int NOT NULL IDENTITY,
    [WorkflowInstanceId] int NOT NULL,
    [FromStageId] int NULL,
    [ToStageId] int NULL,
    [ActionType] nvarchar(50) NOT NULL,
    [Decision] nvarchar(max) NULL,
    [Comment] nvarchar(max) NULL,
    [Reason] nvarchar(max) NULL,
    [RuleApplied] nvarchar(max) NULL,
    [UserId] int NULL,
    [Role] nvarchar(max) NULL,
    [ActionDate] datetime2 NOT NULL,
    [DurationSeconds] int NULL,
    [IPAddress] nvarchar(max) NULL,
    [Browser] nvarchar(max) NULL,
    CONSTRAINT [PK_WorkflowHistories] PRIMARY KEY ([WorkflowHistoryId]),
    CONSTRAINT [FK_WorkflowHistories_WorkflowInstances_WorkflowInstanceId] FOREIGN KEY ([WorkflowInstanceId]) REFERENCES [WorkflowInstances] ([WorkflowInstanceId]) ON DELETE NO ACTION
);

CREATE TABLE [WorkflowInstanceStages] (
    [WorkflowInstanceStageId] int NOT NULL IDENTITY,
    [WorkflowInstanceId] int NOT NULL,
    [WorkflowStageId] int NOT NULL,
    [AssignedToEmployeeId] int NULL,
    [AssignedToRole] nvarchar(100) NULL,
    [Status] nvarchar(30) NOT NULL,
    [Decision] nvarchar(max) NULL,
    [Comment] nvarchar(max) NULL,
    [AssignedDate] datetime2 NOT NULL,
    [ActionDate] datetime2 NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_WorkflowInstanceStages] PRIMARY KEY ([WorkflowInstanceStageId]),
    CONSTRAINT [FK_WorkflowInstanceStages_WorkflowInstances_WorkflowInstanceId] FOREIGN KEY ([WorkflowInstanceId]) REFERENCES [WorkflowInstances] ([WorkflowInstanceId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkflowInstanceStages_WorkflowStages_WorkflowStageId] FOREIGN KEY ([WorkflowStageId]) REFERENCES [WorkflowStages] ([WorkflowStageId]) ON DELETE NO ACTION
);

CREATE TABLE [AIDocumentClassifications] (
    [AIDocumentClassificationId] int NOT NULL IDENTITY,
    [AttachmentId] int NOT NULL,
    [DocumentId] int NULL,
    [PredictedCategory] nvarchar(max) NULL,
    [PredictedDocumentType] nvarchar(max) NULL,
    [PredictedDisciplineCode] nvarchar(max) NULL,
    [PredictedProjectHint] nvarchar(max) NULL,
    [PredictedArea] nvarchar(max) NULL,
    [PredictedUnit] nvarchar(max) NULL,
    [PredictedSystem] nvarchar(max) NULL,
    [PredictedSubsystem] nvarchar(max) NULL,
    [PredictedRevisionNo] nvarchar(max) NULL,
    [PredictedIssuePurpose] nvarchar(max) NULL,
    [PredictedVendorName] nvarchar(max) NULL,
    [PredictedMRNumber] nvarchar(max) NULL,
    [PredictedEquipmentTagNumber] nvarchar(max) NULL,
    [ConfidenceScore] float(5) NOT NULL,
    [ConfidenceLevel] nvarchar(max) NOT NULL,
    [SuggestedTargetModule] nvarchar(max) NULL,
    [ReviewStatus] nvarchar(max) NOT NULL,
    [ReviewedById] int NULL,
    [ReviewedDate] datetime2 NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_AIDocumentClassifications] PRIMARY KEY ([AIDocumentClassificationId]),
    CONSTRAINT [FK_AIDocumentClassifications_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([DocumentId]) ON DELETE CASCADE
);

CREATE TABLE [AIExtractedSpecifications] (
    [ExtractedSpecificationId] int NOT NULL IDENTITY,
    [AttachmentId] int NOT NULL,
    [RelatedTagNumber] nvarchar(max) NULL,
    [FieldName] nvarchar(max) NOT NULL,
    [FieldValue] nvarchar(max) NULL,
    [Unit] nvarchar(max) NULL,
    [Confidence] float(5) NULL,
    [ReviewStatus] nvarchar(max) NOT NULL,
    [ReviewComment] nvarchar(max) NULL,
    [ReviewedDate] datetime2 NULL,
    [ReviewedById] int NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_AIExtractedSpecifications] PRIMARY KEY ([ExtractedSpecificationId]),
    CONSTRAINT [FK_AIExtractedSpecifications_Employees_ReviewedById] FOREIGN KEY ([ReviewedById]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION
);

CREATE TABLE [AIExtractedTags] (
    [ExtractedTagId] int NOT NULL IDENTITY,
    [AttachmentId] int NOT NULL,
    [TagNumber] nvarchar(max) NOT NULL,
    [TagType] nvarchar(max) NULL,
    [Area] nvarchar(max) NULL,
    [Service] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [Confidence] float(5) NULL,
    [ReviewStatus] nvarchar(max) NOT NULL,
    [ReviewComment] nvarchar(max) NULL,
    [ReviewedDate] datetime2 NULL,
    [ReviewedById] int NULL,
    [LinkedEquipmentId] int NULL,
    [CreatedTagId] int NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_AIExtractedTags] PRIMARY KEY ([ExtractedTagId]),
    CONSTRAINT [FK_AIExtractedTags_Employees_ReviewedById] FOREIGN KEY ([ReviewedById]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION
);

CREATE TABLE [Alarms] (
    [AlarmId] int NOT NULL IDENTITY,
    [InstrumentId] int NOT NULL,
    [AlarmTag] nvarchar(max) NOT NULL,
    [AlarmType] nvarchar(max) NOT NULL,
    [Priority] nvarchar(max) NOT NULL,
    [SetPoint] decimal(18,3) NULL,
    [Unit] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [Action] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Alarms] PRIMARY KEY ([AlarmId])
);

CREATE TABLE [CauseEffects] (
    [CauseEffectId] int NOT NULL IDENTITY,
    [AlarmId] int NOT NULL,
    [Cause] nvarchar(max) NOT NULL,
    [Effect] nvarchar(max) NOT NULL,
    [ActionType] nvarchar(max) NULL,
    [TripSignal] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_CauseEffects] PRIMARY KEY ([CauseEffectId]),
    CONSTRAINT [FK_CauseEffects_Alarms_AlarmId] FOREIGN KEY ([AlarmId]) REFERENCES [Alarms] ([AlarmId]) ON DELETE CASCADE
);

CREATE TABLE [Assets] (
    [AssetId] int NOT NULL IDENTITY,
    [EquipmentId] int NOT NULL,
    [AssetNumber] nvarchar(max) NOT NULL,
    [FunctionalLocation] nvarchar(max) NULL,
    [Criticality] nvarchar(max) NULL,
    [MaintenanceClass] nvarchar(max) NULL,
    [CommissioningDate] datetime2 NULL,
    [Status] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Assets] PRIMARY KEY ([AssetId])
);

CREATE TABLE [ControlLoops] (
    [LoopId] int NOT NULL IDENTITY,
    [LoopNumber] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    [ProcessArea] nvarchar(max) NULL,
    [ControllerType] nvarchar(max) NULL,
    [ControllerSystem] nvarchar(max) NULL,
    [InstrumentId] int NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_ControlLoops] PRIMARY KEY ([LoopId])
);

CREATE TABLE [DocumentAttachments] (
    [AttachmentId] int NOT NULL IDENTITY,
    [DocumentId] int NULL,
    [UploadedById] int NULL,
    [IntakeProjectId] int NULL,
    [IntakeStage] nvarchar(max) NULL,
    [RoutedModule] nvarchar(max) NULL,
    [RoutedEntityId] int NULL,
    [LetterId] int NULL,
    [TechnicalQueryId] int NULL,
    [MOCId] int NULL,
    [FileName] nvarchar(max) NOT NULL,
    [FileExtension] nvarchar(max) NOT NULL,
    [FilePath] nvarchar(max) NOT NULL,
    [FileSize] bigint NOT NULL,
    [MimeType] nvarchar(max) NULL,
    [AIStatus] nvarchar(max) NOT NULL,
    [AISummary] nvarchar(max) NULL,
    [AIKeywords] nvarchar(max) NULL,
    [AITags] nvarchar(max) NULL,
    [AIConfidence] float(5) NULL,
    [AIError] nvarchar(max) NULL,
    [AIProcessedDate] datetime2 NULL,
    [UploadDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_DocumentAttachments] PRIMARY KEY ([AttachmentId]),
    CONSTRAINT [FK_DocumentAttachments_Employees_UploadedById] FOREIGN KEY ([UploadedById]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DocumentAttachments_Projects_IntakeProjectId] FOREIGN KEY ([IntakeProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE NO ACTION
);

CREATE TABLE [ProcurementCommunications] (
    [ProcurementCommunicationId] int NOT NULL IDENTITY,
    [ProcurementPackageId] int NOT NULL,
    [Type] nvarchar(max) NOT NULL,
    [Subject] nvarchar(max) NOT NULL,
    [Body] nvarchar(max) NOT NULL,
    [VendorId] int NULL,
    [RaisedByName] nvarchar(max) NULL,
    [RaisedDate] datetime2 NOT NULL,
    [AttachmentId] int NULL,
    [ActionStatus] nvarchar(max) NOT NULL,
    [ResponseText] nvarchar(max) NULL,
    [RespondedByName] nvarchar(max) NULL,
    [ResponseDate] datetime2 NULL,
    CONSTRAINT [PK_ProcurementCommunications] PRIMARY KEY ([ProcurementCommunicationId]),
    CONSTRAINT [FK_ProcurementCommunications_DocumentAttachments_AttachmentId] FOREIGN KEY ([AttachmentId]) REFERENCES [DocumentAttachments] ([AttachmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProcurementCommunications_ProcurementPackages_ProcurementPackageId] FOREIGN KEY ([ProcurementPackageId]) REFERENCES [ProcurementPackages] ([ProcurementPackageId]) ON DELETE CASCADE,
    CONSTRAINT [FK_ProcurementCommunications_Vendors_VendorId] FOREIGN KEY ([VendorId]) REFERENCES [Vendors] ([VendorId]) ON DELETE NO ACTION
);

CREATE TABLE [DocumentComments] (
    [DocumentCommentId] int NOT NULL IDENTITY,
    [DocumentId] int NOT NULL,
    [VDRSubmissionId] int NULL,
    [CommentNo] nvarchar(max) NOT NULL,
    [Sheet] nvarchar(max) NULL,
    [Zone] nvarchar(max) NULL,
    [Reference] nvarchar(max) NULL,
    [Category] nvarchar(max) NULL,
    [Severity] nvarchar(max) NULL,
    [RequiredAction] nvarchar(max) NULL,
    [DueDate] datetime2 NULL,
    [CommentText] nvarchar(max) NOT NULL,
    [RaisedByName] nvarchar(max) NULL,
    [RaisedDate] datetime2 NOT NULL,
    [ActionStatus] nvarchar(max) NOT NULL,
    [AttachmentId] int NULL,
    [ResponseText] nvarchar(max) NULL,
    [ResponseDate] datetime2 NULL,
    [ReplyAttachmentId] int NULL,
    [ClosedByName] nvarchar(max) NULL,
    [ClosedDate] datetime2 NULL,
    [ParentCommentId] int NULL,
    [PageNumber] int NULL,
    [PinX] float NULL,
    [PinY] float NULL,
    CONSTRAINT [PK_DocumentComments] PRIMARY KEY ([DocumentCommentId]),
    CONSTRAINT [FK_DocumentComments_DocumentAttachments_AttachmentId] FOREIGN KEY ([AttachmentId]) REFERENCES [DocumentAttachments] ([AttachmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DocumentComments_DocumentAttachments_ReplyAttachmentId] FOREIGN KEY ([ReplyAttachmentId]) REFERENCES [DocumentAttachments] ([AttachmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DocumentComments_DocumentComments_ParentCommentId] FOREIGN KEY ([ParentCommentId]) REFERENCES [DocumentComments] ([DocumentCommentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DocumentComments_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([DocumentId]) ON DELETE CASCADE
);

CREATE TABLE [DocumentRevisions] (
    [RevisionId] int NOT NULL IDENTITY,
    [DocumentId] int NOT NULL,
    [RevisionNo] nvarchar(450) NOT NULL,
    [PreviousRevisionNo] nvarchar(max) NULL,
    [Status] nvarchar(max) NOT NULL,
    [IssuePurpose] nvarchar(max) NULL,
    [Comment] nvarchar(max) NULL,
    [RevisionDate] datetime2 NOT NULL,
    [PreparedById] int NULL,
    [CheckedById] int NULL,
    [ApprovedById] int NULL,
    [Reason] nvarchar(max) NULL,
    [ChangeDescription] nvarchar(max) NULL,
    [VendorComment] nvarchar(max) NULL,
    [ReviewerId] int NULL,
    [CreatedById] int NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IssuedDate] datetime2 NULL,
    [EffectiveDate] datetime2 NULL,
    [SupersededRevisionId] int NULL,
    [AIRevisionSummary] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_DocumentRevisions] PRIMARY KEY ([RevisionId]),
    CONSTRAINT [FK_DocumentRevisions_DocumentRevisions_SupersededRevisionId] FOREIGN KEY ([SupersededRevisionId]) REFERENCES [DocumentRevisions] ([RevisionId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DocumentRevisions_Employees_ApprovedById] FOREIGN KEY ([ApprovedById]) REFERENCES [Employees] ([EmployeeId]),
    CONSTRAINT [FK_DocumentRevisions_Employees_CheckedById] FOREIGN KEY ([CheckedById]) REFERENCES [Employees] ([EmployeeId]),
    CONSTRAINT [FK_DocumentRevisions_Employees_CreatedById] FOREIGN KEY ([CreatedById]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_DocumentRevisions_Employees_PreparedById] FOREIGN KEY ([PreparedById]) REFERENCES [Employees] ([EmployeeId]),
    CONSTRAINT [FK_DocumentRevisions_Employees_ReviewerId] FOREIGN KEY ([ReviewerId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION
);

CREATE TABLE [DocumentTagLinks] (
    [DocumentTagLinkId] int NOT NULL IDENTITY,
    [DocumentId] int NOT NULL,
    [EquipmentTagId] int NOT NULL,
    [IsAIProposed] bit NOT NULL,
    [ReviewStatus] nvarchar(max) NOT NULL,
    [Confidence] float(5) NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_DocumentTagLinks] PRIMARY KEY ([DocumentTagLinkId]),
    CONSTRAINT [FK_DocumentTagLinks_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([DocumentId]) ON DELETE CASCADE
);

CREATE TABLE [DocumentWorkflows] (
    [WorkflowId] int NOT NULL IDENTITY,
    [DocumentId] int NOT NULL,
    [FromStatus] nvarchar(max) NOT NULL,
    [ToStatus] nvarchar(max) NOT NULL,
    [ActionType] nvarchar(max) NOT NULL,
    [Comment] nvarchar(max) NULL,
    [ActionDate] datetime2 NOT NULL,
    [ActionById] int NULL,
    [IPAddress] nvarchar(max) NULL,
    [Browser] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_DocumentWorkflows] PRIMARY KEY ([WorkflowId]),
    CONSTRAINT [FK_DocumentWorkflows_Employees_ActionById] FOREIGN KEY ([ActionById]) REFERENCES [Employees] ([EmployeeId])
);

CREATE TABLE [ElectricalEquipments] (
    [ElectricalEquipmentId] int NOT NULL IDENTITY,
    [EquipmentId] int NULL,
    [TagNumber] nvarchar(max) NOT NULL,
    [EquipmentType] nvarchar(max) NOT NULL,
    [Manufacturer] nvarchar(max) NULL,
    [Model] nvarchar(max) NULL,
    [PowerKW] decimal(18,3) NULL,
    [Voltage] decimal(18,3) NULL,
    [RatedCurrent] decimal(18,3) NULL,
    [Phase] int NULL,
    [SupplySource] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_ElectricalEquipments] PRIMARY KEY ([ElectricalEquipmentId])
);

CREATE TABLE [EngineeringDocuments] (
    [DocumentId] int NOT NULL,
    [Description] nvarchar(max) NULL,
    [EquipmentId] int NULL,
    [ContractId] int NULL,
    [VendorId] int NULL,
    [RelatedMRId] int NULL,
    [IssuePurpose] nvarchar(max) NULL,
    [PreparedBy] nvarchar(max) NULL,
    [CheckedBy] nvarchar(max) NULL,
    [ApprovedBy] nvarchar(max) NULL,
    [PreparedById] int NULL,
    [CheckedById] int NULL,
    [ApprovedById] int NULL,
    [CheckedDate] datetime2 NULL,
    [ApprovedDate] datetime2 NULL,
    [ClientApprovalStatus] nvarchar(max) NULL,
    [ClientComment] nvarchar(max) NULL,
    [ClientResponseDate] datetime2 NULL,
    [DeletedDate] datetime2 NULL,
    [DeletedById] int NULL,
    CONSTRAINT [PK_EngineeringDocuments] PRIMARY KEY ([DocumentId]),
    CONSTRAINT [FK_EngineeringDocuments_Contracts_ContractId] FOREIGN KEY ([ContractId]) REFERENCES [Contracts] ([ContractId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_EngineeringDocuments_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([DocumentId]) ON DELETE CASCADE,
    CONSTRAINT [FK_EngineeringDocuments_Employees_ApprovedById] FOREIGN KEY ([ApprovedById]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_EngineeringDocuments_Employees_CheckedById] FOREIGN KEY ([CheckedById]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_EngineeringDocuments_Employees_DeletedById] FOREIGN KEY ([DeletedById]) REFERENCES [Employees] ([EmployeeId]),
    CONSTRAINT [FK_EngineeringDocuments_Employees_PreparedById] FOREIGN KEY ([PreparedById]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_EngineeringDocuments_Vendors_VendorId] FOREIGN KEY ([VendorId]) REFERENCES [Vendors] ([VendorId]) ON DELETE NO ACTION
);

CREATE TABLE [MaterialRequisitions] (
    [MRId] int NOT NULL IDENTITY,
    [MRNumber] nvarchar(max) NOT NULL,
    [ProjectId] int NOT NULL,
    [ProcurementPackageId] int NULL,
    [DisciplineId] int NOT NULL,
    [EquipmentTagNumber] nvarchar(max) NULL,
    [Description] nvarchar(max) NOT NULL,
    [RequiredDate] datetime2 NULL,
    [ResponsibleEngineerId] int NULL,
    [Status] nvarchar(max) NOT NULL,
    [RelatedDocumentId] int NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_MaterialRequisitions] PRIMARY KEY ([MRId]),
    CONSTRAINT [FK_MaterialRequisitions_Disciplines_DisciplineId] FOREIGN KEY ([DisciplineId]) REFERENCES [Disciplines] ([DisciplineId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MaterialRequisitions_Employees_ResponsibleEngineerId] FOREIGN KEY ([ResponsibleEngineerId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MaterialRequisitions_EngineeringDocuments_RelatedDocumentId] FOREIGN KEY ([RelatedDocumentId]) REFERENCES [EngineeringDocuments] ([DocumentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MaterialRequisitions_ProcurementPackages_ProcurementPackageId] FOREIGN KEY ([ProcurementPackageId]) REFERENCES [ProcurementPackages] ([ProcurementPackageId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MaterialRequisitions_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE NO ACTION
);

CREATE TABLE [Equipments] (
    [EquipmentId] int NOT NULL IDENTITY,
    [TagNumber] nvarchar(max) NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [EquipmentType] nvarchar(max) NOT NULL,
    [ProjectId] int NOT NULL,
    [DisciplineId] int NOT NULL,
    [Manufacturer] nvarchar(max) NULL,
    [Model] nvarchar(max) NULL,
    [SerialNumber] nvarchar(max) NULL,
    [Status] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    [Area] nvarchar(max) NULL,
    [PIDNumber] nvarchar(max) NULL,
    [RelatedMRId] int NULL,
    [MRNumberRaw] nvarchar(max) NULL,
    [PONumberRaw] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_Equipments] PRIMARY KEY ([EquipmentId]),
    CONSTRAINT [FK_Equipments_Disciplines_DisciplineId] FOREIGN KEY ([DisciplineId]) REFERENCES [Disciplines] ([DisciplineId]) ON DELETE CASCADE,
    CONSTRAINT [FK_Equipments_MaterialRequisitions_RelatedMRId] FOREIGN KEY ([RelatedMRId]) REFERENCES [MaterialRequisitions] ([MRId]),
    CONSTRAINT [FK_Equipments_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE CASCADE
);

CREATE TABLE [Letters] (
    [LetterId] int NOT NULL IDENTITY,
    [LetterType] nvarchar(450) NOT NULL,
    [LetterNumber] nvarchar(450) NOT NULL,
    [LetterDate] datetime2 NOT NULL,
    [Sender] nvarchar(max) NULL,
    [Receiver] nvarchar(max) NULL,
    [ReceiverDepartmentId] int NULL,
    [ReceiverEmployeeId] int NULL,
    [Subject] nvarchar(max) NOT NULL,
    [Body] nvarchar(max) NULL,
    [Priority] nvarchar(max) NOT NULL,
    [ConfidentialityLevel] nvarchar(max) NOT NULL,
    [ProjectId] int NULL,
    [DisciplineId] int NULL,
    [RelatedDocumentId] int NULL,
    [RelatedMRId] int NULL,
    [Status] nvarchar(450) NOT NULL,
    [RegisteredById] int NULL,
    [ApprovedDate] datetime2 NULL,
    [ApprovedById] int NULL,
    [ParentLetterId] int NULL,
    [IssuedDate] datetime2 NULL,
    [SignedByName] nvarchar(max) NULL,
    [SignedDate] datetime2 NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_Letters] PRIMARY KEY ([LetterId]),
    CONSTRAINT [FK_Letters_Departments_ReceiverDepartmentId] FOREIGN KEY ([ReceiverDepartmentId]) REFERENCES [Departments] ([DepartmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Letters_Disciplines_DisciplineId] FOREIGN KEY ([DisciplineId]) REFERENCES [Disciplines] ([DisciplineId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Letters_Documents_RelatedDocumentId] FOREIGN KEY ([RelatedDocumentId]) REFERENCES [Documents] ([DocumentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Letters_Employees_ReceiverEmployeeId] FOREIGN KEY ([ReceiverEmployeeId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Letters_Letters_ParentLetterId] FOREIGN KEY ([ParentLetterId]) REFERENCES [Letters] ([LetterId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Letters_MaterialRequisitions_RelatedMRId] FOREIGN KEY ([RelatedMRId]) REFERENCES [MaterialRequisitions] ([MRId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Letters_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE NO ACTION
);

CREATE TABLE [MaterialRequisitionItems] (
    [MRItemId] int NOT NULL IDENTITY,
    [MRId] int NOT NULL,
    [ItemDescription] nvarchar(max) NOT NULL,
    [PartNumber] nvarchar(max) NULL,
    [Quantity] float NOT NULL,
    [Unit] nvarchar(max) NULL,
    [Remarks] nvarchar(max) NULL,
    CONSTRAINT [PK_MaterialRequisitionItems] PRIMARY KEY ([MRItemId]),
    CONSTRAINT [FK_MaterialRequisitionItems_MaterialRequisitions_MRId] FOREIGN KEY ([MRId]) REFERENCES [MaterialRequisitions] ([MRId]) ON DELETE CASCADE
);

CREATE TABLE [MaterialRequisitionQuotations] (
    [MRQId] int NOT NULL IDENTITY,
    [MRQNumber] nvarchar(max) NOT NULL,
    [MRId] int NOT NULL,
    [ProcurementPackageId] int NULL,
    [VendorName] nvarchar(max) NOT NULL,
    [InquiryDate] datetime2 NULL,
    [QuotedAmount] decimal(18,2) NULL,
    [Currency] nvarchar(max) NULL,
    [QuotationStatus] nvarchar(max) NOT NULL,
    [TechnicalEvaluation] nvarchar(max) NOT NULL,
    [CommercialEvaluation] nvarchar(max) NOT NULL,
    [ApprovalStatus] nvarchar(max) NOT NULL,
    [Notes] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_MaterialRequisitionQuotations] PRIMARY KEY ([MRQId]),
    CONSTRAINT [FK_MaterialRequisitionQuotations_MaterialRequisitions_MRId] FOREIGN KEY ([MRId]) REFERENCES [MaterialRequisitions] ([MRId]) ON DELETE CASCADE,
    CONSTRAINT [FK_MaterialRequisitionQuotations_ProcurementPackages_ProcurementPackageId] FOREIGN KEY ([ProcurementPackageId]) REFERENCES [ProcurementPackages] ([ProcurementPackageId]) ON DELETE NO ACTION
);

CREATE TABLE [EquipmentCriticalities] (
    [EquipmentCriticalityId] int NOT NULL IDENTITY,
    [EquipmentId] int NOT NULL,
    [CriticalityClass] nvarchar(max) NOT NULL,
    [RiskLevel] nvarchar(max) NULL,
    [SafetyImpact] nvarchar(max) NULL,
    [ProductionImpact] nvarchar(max) NULL,
    [EnvironmentImpact] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_EquipmentCriticalities] PRIMARY KEY ([EquipmentCriticalityId]),
    CONSTRAINT [FK_EquipmentCriticalities_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE CASCADE
);

CREATE TABLE [EquipmentDatasheets] (
    [DatasheetId] int NOT NULL IDENTITY,
    [EquipmentId] int NOT NULL,
    [DocumentNumber] nvarchar(max) NOT NULL,
    [RevisionNo] nvarchar(max) NOT NULL,
    [DesignPressure] decimal(18,3) NULL,
    [DesignTemperature] decimal(18,3) NULL,
    [OperatingPressure] decimal(18,3) NULL,
    [OperatingTemperature] decimal(18,3) NULL,
    [Manufacturer] nvarchar(max) NULL,
    [Model] nvarchar(max) NULL,
    [Specification] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_EquipmentDatasheets] PRIMARY KEY ([DatasheetId]),
    CONSTRAINT [FK_EquipmentDatasheets_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE CASCADE
);

CREATE TABLE [EquipmentNozzles] (
    [EquipmentNozzleId] int NOT NULL IDENTITY,
    [EquipmentId] int NOT NULL,
    [NozzleTag] nvarchar(max) NOT NULL,
    [Size] nvarchar(max) NULL,
    [Rating] nvarchar(max) NULL,
    [Type] nvarchar(max) NULL,
    [Service] nvarchar(max) NULL,
    [Orientation] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_EquipmentNozzles] PRIMARY KEY ([EquipmentNozzleId]),
    CONSTRAINT [FK_EquipmentNozzles_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE CASCADE
);

CREATE TABLE [EquipmentTags] (
    [TagId] int NOT NULL IDENTITY,
    [EquipmentId] int NOT NULL,
    [TagNumber] nvarchar(max) NOT NULL,
    [TagType] nvarchar(max) NOT NULL,
    [Area] nvarchar(max) NULL,
    [Service] nvarchar(max) NULL,
    [LoopNumber] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_EquipmentTags] PRIMARY KEY ([TagId]),
    CONSTRAINT [FK_EquipmentTags_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE CASCADE
);

CREATE TABLE [InspectionTestPlans] (
    [InspectionTestPlanId] int NOT NULL IDENTITY,
    [ITPNumber] nvarchar(100) NOT NULL,
    [ProjectId] int NOT NULL,
    [EquipmentId] int NULL,
    [QCDisciplineId] int NOT NULL,
    [Title] nvarchar(300) NOT NULL,
    [ReferenceCodeStandard] nvarchar(max) NULL,
    [Revision] int NOT NULL,
    [Status] nvarchar(30) NOT NULL,
    [ApprovedDate] datetime2 NULL,
    [ApprovedById] int NULL,
    [Description] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_InspectionTestPlans] PRIMARY KEY ([InspectionTestPlanId]),
    CONSTRAINT [FK_InspectionTestPlans_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InspectionTestPlans_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE CASCADE,
    CONSTRAINT [FK_InspectionTestPlans_QCDisciplines_QCDisciplineId] FOREIGN KEY ([QCDisciplineId]) REFERENCES [QCDisciplines] ([QCDisciplineId]) ON DELETE NO ACTION
);

CREATE TABLE [Instruments] (
    [InstrumentId] int NOT NULL IDENTITY,
    [EquipmentId] int NOT NULL,
    [TagNumber] nvarchar(max) NOT NULL,
    [InstrumentType] nvarchar(max) NOT NULL,
    [Manufacturer] nvarchar(max) NULL,
    [Model] nvarchar(max) NULL,
    [RangeMin] decimal(18,3) NULL,
    [RangeMax] decimal(18,3) NULL,
    [Unit] nvarchar(max) NULL,
    [SignalType] nvarchar(max) NULL,
    [CalibrationInterval] int NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Instruments] PRIMARY KEY ([InstrumentId]),
    CONSTRAINT [FK_Instruments_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE CASCADE
);

CREATE TABLE [MaintenanceStrategies] (
    [MaintenanceStrategyId] int NOT NULL IDENTITY,
    [EquipmentId] int NOT NULL,
    [StrategyType] nvarchar(max) NOT NULL,
    [FailureModeCode] nvarchar(max) NULL,
    [ConsequenceCategory] nvarchar(max) NOT NULL,
    [Justification] nvarchar(max) NULL,
    [RecommendedTriggerType] nvarchar(max) NOT NULL,
    [Frequency] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [StartDate] datetime2 NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_MaintenanceStrategies] PRIMARY KEY ([MaintenanceStrategyId]),
    CONSTRAINT [FK_MaintenanceStrategies_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE CASCADE
);

CREATE TABLE [MechanicalEquipments] (
    [MechanicalEquipmentId] int NOT NULL IDENTITY,
    [EquipmentId] int NULL,
    [TagNumber] nvarchar(max) NOT NULL,
    [EquipmentType] nvarchar(max) NOT NULL,
    [DesignPressure] decimal(18,3) NULL,
    [DesignTemperature] decimal(18,3) NULL,
    [OperatingPressure] decimal(18,3) NULL,
    [OperatingTemperature] decimal(18,3) NULL,
    [Material] nvarchar(max) NULL,
    [Manufacturer] nvarchar(max) NULL,
    [Model] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_MechanicalEquipments] PRIMARY KEY ([MechanicalEquipmentId]),
    CONSTRAINT [FK_MechanicalEquipments_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId])
);

CREATE TABLE [MOCs] (
    [MOCId] int NOT NULL IDENTITY,
    [MOCNumber] nvarchar(100) NOT NULL,
    [ProjectId] int NOT NULL,
    [DisciplineId] int NOT NULL,
    [Title] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NOT NULL,
    [Reason] nvarchar(max) NULL,
    [ChangeType] nvarchar(20) NOT NULL,
    [TemporaryExpiryDate] datetime2 NULL,
    [RiskLevel] nvarchar(20) NOT NULL,
    [RelatedDocumentId] int NULL,
    [RelatedEquipmentId] int NULL,
    [RequestedById] int NULL,
    [RequestedDate] datetime2 NOT NULL,
    [RiskAssessmentText] nvarchar(max) NULL,
    [RiskAssessedById] int NULL,
    [RiskAssessedDate] datetime2 NULL,
    [ApprovedById] int NULL,
    [ApprovedDate] datetime2 NULL,
    [RejectionReason] nvarchar(max) NULL,
    [ImplementationPlan] nvarchar(max) NULL,
    [ImplementedById] int NULL,
    [ImplementedDate] datetime2 NULL,
    [DocumentsUpdated] bit NOT NULL,
    [UpdatedDocumentsNote] nvarchar(max) NULL,
    [TrainingRequired] bit NOT NULL,
    [TrainingCompleted] bit NOT NULL,
    [Status] nvarchar(30) NOT NULL,
    [ClosedById] int NULL,
    [ClosedDate] datetime2 NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_MOCs] PRIMARY KEY ([MOCId]),
    CONSTRAINT [FK_MOCs_Disciplines_DisciplineId] FOREIGN KEY ([DisciplineId]) REFERENCES [Disciplines] ([DisciplineId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MOCs_Documents_RelatedDocumentId] FOREIGN KEY ([RelatedDocumentId]) REFERENCES [Documents] ([DocumentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MOCs_Equipments_RelatedEquipmentId] FOREIGN KEY ([RelatedEquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MOCs_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE CASCADE
);

CREATE TABLE [TechnicalQueries] (
    [TechnicalQueryId] int NOT NULL IDENTITY,
    [QueryNumber] nvarchar(100) NOT NULL,
    [QueryType] nvarchar(10) NOT NULL,
    [ProjectId] int NOT NULL,
    [DisciplineId] int NOT NULL,
    [Subject] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    [RelatedDocumentId] int NULL,
    [RelatedEquipmentId] int NULL,
    [RaisedById] int NULL,
    [RaisedDate] datetime2 NOT NULL,
    [RequiredResponseDate] datetime2 NULL,
    [ResponseText] nvarchar(max) NULL,
    [RespondedById] int NULL,
    [RespondedDate] datetime2 NULL,
    [Status] nvarchar(30) NOT NULL,
    [Priority] nvarchar(20) NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_TechnicalQueries] PRIMARY KEY ([TechnicalQueryId]),
    CONSTRAINT [FK_TechnicalQueries_Disciplines_DisciplineId] FOREIGN KEY ([DisciplineId]) REFERENCES [Disciplines] ([DisciplineId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TechnicalQueries_Documents_RelatedDocumentId] FOREIGN KEY ([RelatedDocumentId]) REFERENCES [Documents] ([DocumentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TechnicalQueries_Equipments_RelatedEquipmentId] FOREIGN KEY ([RelatedEquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_TechnicalQueries_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE CASCADE
);

CREATE TABLE [VDRItems] (
    [DocumentId] int NOT NULL,
    [SequenceNo] int NOT NULL,
    [DisciplineCodeRaw] nvarchar(max) NULL,
    [IsMultiDiscipline] bit NOT NULL,
    [MRNumber] nvarchar(max) NULL,
    [MRId] int NULL,
    [CommercialExpeditorName] nvarchar(max) NULL,
    [EngineeringExpeditorName] nvarchar(max) NULL,
    [VendorId] int NOT NULL,
    [EquipmentId] int NULL,
    [FolderNoOnFtp] nvarchar(max) NULL,
    [PoId] int NULL,
    [PackageDescription] nvarchar(max) NULL,
    [DocumentTypeCode] nvarchar(max) NOT NULL,
    [DocumentNumber] nvarchar(450) NOT NULL,
    [DocumentDescription] nvarchar(max) NULL,
    [ResponsiblePartyCode] nvarchar(max) NULL,
    [DocumentClass] int NULL,
    [DocumentClassRaw] nvarchar(max) NULL,
    [WrapUpStatusImported] nvarchar(max) NULL,
    [PlannedFirstIssueDate] datetime2 NULL,
    [WorkflowStatus] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_VDRItems] PRIMARY KEY ([DocumentId]),
    CONSTRAINT [FK_VDRItems_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([DocumentId]) ON DELETE CASCADE,
    CONSTRAINT [FK_VDRItems_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_VDRItems_MaterialRequisitions_MRId] FOREIGN KEY ([MRId]) REFERENCES [MaterialRequisitions] ([MRId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_VDRItems_PurchaseOrders_PoId] FOREIGN KEY ([PoId]) REFERENCES [PurchaseOrders] ([PoId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_VDRItems_Vendors_VendorId] FOREIGN KEY ([VendorId]) REFERENCES [Vendors] ([VendorId]) ON DELETE NO ACTION
);

CREATE TABLE [LetterRecipients] (
    [LetterRecipientId] int NOT NULL IDENTITY,
    [LetterId] int NOT NULL,
    [RecipientType] nvarchar(max) NOT NULL,
    [EmployeeId] int NULL,
    [DepartmentId] int NULL,
    [ExternalName] nvarchar(max) NULL,
    [IsRead] bit NOT NULL,
    [ReadDate] datetime2 NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_LetterRecipients] PRIMARY KEY ([LetterRecipientId]),
    CONSTRAINT [FK_LetterRecipients_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([DepartmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_LetterRecipients_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_LetterRecipients_Letters_LetterId] FOREIGN KEY ([LetterId]) REFERENCES [Letters] ([LetterId]) ON DELETE CASCADE
);

CREATE TABLE [LetterReferrals] (
    [LetterReferralId] int NOT NULL IDENTITY,
    [LetterId] int NOT NULL,
    [ReferredToEmployeeId] int NULL,
    [ReferredToDepartmentId] int NULL,
    [Instructions] nvarchar(max) NULL,
    [DueDate] datetime2 NULL,
    [Status] nvarchar(max) NOT NULL,
    [IsRead] bit NOT NULL,
    [ReadDate] datetime2 NULL,
    [ReferredByName] nvarchar(max) NULL,
    [ReferredDate] datetime2 NOT NULL,
    [ResponseNote] nvarchar(max) NULL,
    [CompletedDate] datetime2 NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_LetterReferrals] PRIMARY KEY ([LetterReferralId]),
    CONSTRAINT [FK_LetterReferrals_Departments_ReferredToDepartmentId] FOREIGN KEY ([ReferredToDepartmentId]) REFERENCES [Departments] ([DepartmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_LetterReferrals_Employees_ReferredToEmployeeId] FOREIGN KEY ([ReferredToEmployeeId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_LetterReferrals_Letters_LetterId] FOREIGN KEY ([LetterId]) REFERENCES [Letters] ([LetterId]) ON DELETE CASCADE
);

CREATE TABLE [LetterStars] (
    [LetterStarId] int NOT NULL IDENTITY,
    [LetterId] int NOT NULL,
    [ApplicationUserId] int NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_LetterStars] PRIMARY KEY ([LetterStarId]),
    CONSTRAINT [FK_LetterStars_Letters_LetterId] FOREIGN KEY ([LetterId]) REFERENCES [Letters] ([LetterId]) ON DELETE CASCADE
);

CREATE TABLE [LetterTags] (
    [LetterTagId] int NOT NULL IDENTITY,
    [LetterId] int NOT NULL,
    [TagId] int NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_LetterTags] PRIMARY KEY ([LetterTagId]),
    CONSTRAINT [FK_LetterTags_Letters_LetterId] FOREIGN KEY ([LetterId]) REFERENCES [Letters] ([LetterId]) ON DELETE CASCADE,
    CONSTRAINT [FK_LetterTags_Tags_TagId] FOREIGN KEY ([TagId]) REFERENCES [Tags] ([TagId]) ON DELETE CASCADE
);

CREATE TABLE [ITPLineItems] (
    [ITPLineItemId] int NOT NULL IDENTITY,
    [InspectionTestPlanId] int NOT NULL,
    [SequenceNo] int NOT NULL,
    [Activity] nvarchar(300) NOT NULL,
    [QCDisciplineId] int NOT NULL,
    [InspectionCategory] nvarchar(5) NOT NULL,
    [AcceptanceCriteria] nvarchar(max) NULL,
    [ReferenceCodeStandard] nvarchar(max) NULL,
    [ResponsibleParty] nvarchar(max) NULL,
    [Status] nvarchar(30) NOT NULL,
    [NotifiedDate] datetime2 NULL,
    [NotifiedById] int NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_ITPLineItems] PRIMARY KEY ([ITPLineItemId]),
    CONSTRAINT [FK_ITPLineItems_InspectionTestPlans_InspectionTestPlanId] FOREIGN KEY ([InspectionTestPlanId]) REFERENCES [InspectionTestPlans] ([InspectionTestPlanId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ITPLineItems_QCDisciplines_QCDisciplineId] FOREIGN KEY ([QCDisciplineId]) REFERENCES [QCDisciplines] ([QCDisciplineId]) ON DELETE NO ACTION
);

CREATE TABLE [IOPoints] (
    [IOPointId] int NOT NULL IDENTITY,
    [InstrumentId] int NOT NULL,
    [TagNumber] nvarchar(max) NOT NULL,
    [IOType] nvarchar(max) NOT NULL,
    [SignalType] nvarchar(max) NULL,
    [PLCName] nvarchar(max) NULL,
    [Rack] nvarchar(max) NULL,
    [Slot] nvarchar(max) NULL,
    [Channel] nvarchar(max) NULL,
    [Address] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_IOPoints] PRIMARY KEY ([IOPointId]),
    CONSTRAINT [FK_IOPoints_Instruments_InstrumentId] FOREIGN KEY ([InstrumentId]) REFERENCES [Instruments] ([InstrumentId]) ON DELETE CASCADE
);

CREATE TABLE [MaintenancePlans] (
    [MaintenancePlanId] int NOT NULL IDENTITY,
    [EquipmentId] int NOT NULL,
    [MaintenanceStrategyId] int NOT NULL,
    [PlanNumber] nvarchar(max) NOT NULL,
    [Title] nvarchar(max) NOT NULL,
    [IntervalDays] int NULL,
    [TriggerType] nvarchar(max) NOT NULL,
    [MeterType] nvarchar(max) NULL,
    [MeterInterval] decimal(18,2) NULL,
    [LastExecutionMeterReading] decimal(18,2) NULL,
    [NextDueMeterReading] decimal(18,2) NULL,
    [LastExecutionDate] datetime2 NULL,
    [NextDueDate] datetime2 NULL,
    [Description] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_MaintenancePlans] PRIMARY KEY ([MaintenancePlanId]),
    CONSTRAINT [FK_MaintenancePlans_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE CASCADE,
    CONSTRAINT [FK_MaintenancePlans_MaintenanceStrategies_MaintenanceStrategyId] FOREIGN KEY ([MaintenanceStrategyId]) REFERENCES [MaintenanceStrategies] ([MaintenanceStrategyId]) ON DELETE NO ACTION
);

CREATE TABLE [RotatingEquipments] (
    [RotatingEquipmentId] int NOT NULL IDENTITY,
    [MechanicalEquipmentId] int NOT NULL,
    [EquipmentType] nvarchar(max) NOT NULL,
    [FlowRate] decimal(18,3) NULL,
    [Head] decimal(18,3) NULL,
    [SpeedRPM] int NULL,
    [PowerKW] decimal(18,3) NULL,
    [DriverType] nvarchar(max) NULL,
    [BearingType] nvarchar(max) NULL,
    [LubricationType] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_RotatingEquipments] PRIMARY KEY ([RotatingEquipmentId]),
    CONSTRAINT [FK_RotatingEquipments_MechanicalEquipments_MechanicalEquipmentId] FOREIGN KEY ([MechanicalEquipmentId]) REFERENCES [MechanicalEquipments] ([MechanicalEquipmentId]) ON DELETE CASCADE
);

CREATE TABLE [StaticEquipments] (
    [StaticEquipmentId] int NOT NULL IDENTITY,
    [MechanicalEquipmentId] int NOT NULL,
    [EquipmentType] nvarchar(max) NOT NULL,
    [Volume] decimal(18,3) NULL,
    [DesignPressure] decimal(18,3) NULL,
    [DesignTemperature] decimal(18,3) NULL,
    [Material] nvarchar(max) NULL,
    [Orientation] nvarchar(max) NULL,
    [CodeStandard] nvarchar(max) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_StaticEquipments] PRIMARY KEY ([StaticEquipmentId]),
    CONSTRAINT [FK_StaticEquipments_MechanicalEquipments_MechanicalEquipmentId] FOREIGN KEY ([MechanicalEquipmentId]) REFERENCES [MechanicalEquipments] ([MechanicalEquipmentId]) ON DELETE CASCADE
);

CREATE TABLE [VDRSubmissions] (
    [VDRSubmissionId] int NOT NULL IDENTITY,
    [VDRItemId] int NOT NULL,
    [RevisionNo] int NOT NULL,
    [VendorTransmittalNo] nvarchar(max) NULL,
    [ClientTransmittalNo] nvarchar(max) NULL,
    [ClientTransmittalId] int NULL,
    [VendorActualIssuedDate] datetime2 NULL,
    [PurposeOfIssueCode] nvarchar(max) NULL,
    [ClientCommentSheetNo] nvarchar(max) NULL,
    [ClientCommentDate] datetime2 NULL,
    [ReviewCategory] nvarchar(max) NULL,
    [ReplySheetNo] nvarchar(max) NULL,
    [ReplySheetDate] datetime2 NULL,
    [AttachmentId] int NULL,
    [CommentSheetAttachmentId] int NULL,
    [ReplySheetAttachmentId] int NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_VDRSubmissions] PRIMARY KEY ([VDRSubmissionId]),
    CONSTRAINT [FK_VDRSubmissions_DocumentAttachments_AttachmentId] FOREIGN KEY ([AttachmentId]) REFERENCES [DocumentAttachments] ([AttachmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_VDRSubmissions_DocumentAttachments_CommentSheetAttachmentId] FOREIGN KEY ([CommentSheetAttachmentId]) REFERENCES [DocumentAttachments] ([AttachmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_VDRSubmissions_DocumentAttachments_ReplySheetAttachmentId] FOREIGN KEY ([ReplySheetAttachmentId]) REFERENCES [DocumentAttachments] ([AttachmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_VDRSubmissions_Transmittals_ClientTransmittalId] FOREIGN KEY ([ClientTransmittalId]) REFERENCES [Transmittals] ([TransmittalId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_VDRSubmissions_VDRItems_VDRItemId] FOREIGN KEY ([VDRItemId]) REFERENCES [VDRItems] ([DocumentId]) ON DELETE CASCADE
);

CREATE TABLE [InspectionRecords] (
    [InspectionRecordId] int NOT NULL IDENTITY,
    [EquipmentId] int NOT NULL,
    [ITPLineItemId] int NULL,
    [QCDisciplineId] int NULL,
    [InspectionType] nvarchar(max) NOT NULL,
    [InspectionDate] datetime2 NOT NULL,
    [Inspector] nvarchar(max) NULL,
    [Result] nvarchar(max) NULL,
    [Condition] nvarchar(max) NULL,
    [Recommendation] nvarchar(max) NULL,
    [NextInspectionDate] datetime2 NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_InspectionRecords] PRIMARY KEY ([InspectionRecordId]),
    CONSTRAINT [FK_InspectionRecords_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE CASCADE,
    CONSTRAINT [FK_InspectionRecords_ITPLineItems_ITPLineItemId] FOREIGN KEY ([ITPLineItemId]) REFERENCES [ITPLineItems] ([ITPLineItemId]),
    CONSTRAINT [FK_InspectionRecords_QCDisciplines_QCDisciplineId] FOREIGN KEY ([QCDisciplineId]) REFERENCES [QCDisciplines] ([QCDisciplineId])
);

CREATE TABLE [WorkOrders] (
    [WorkOrderId] int NOT NULL IDENTITY,
    [EquipmentId] int NOT NULL,
    [MaintenancePlanId] int NULL,
    [WorkOrderNumber] nvarchar(450) NOT NULL,
    [WorkOrderType] nvarchar(max) NOT NULL,
    [Priority] nvarchar(max) NOT NULL,
    [Status] nvarchar(max) NOT NULL,
    [AssignedTo] nvarchar(max) NULL,
    [AssignedTeamId] int NULL,
    [AssignedEmployeeId] int NULL,
    [FailureModeCode] nvarchar(max) NULL,
    [FailureCauseCode] nvarchar(max) NULL,
    [RootCause] nvarchar(max) NULL,
    [DowntimeHours] decimal(18,2) NULL,
    [StartDate] datetime2 NULL,
    [CompletionDate] datetime2 NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_WorkOrders] PRIMARY KEY ([WorkOrderId]),
    CONSTRAINT [FK_WorkOrders_Employees_AssignedEmployeeId] FOREIGN KEY ([AssignedEmployeeId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkOrders_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkOrders_MaintenancePlans_MaintenancePlanId] FOREIGN KEY ([MaintenancePlanId]) REFERENCES [MaintenancePlans] ([MaintenancePlanId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkOrders_Teams_AssignedTeamId] FOREIGN KEY ([AssignedTeamId]) REFERENCES [Teams] ([TeamId]) ON DELETE NO ACTION
);

CREATE TABLE [InspectionChecklists] (
    [InspectionChecklistId] int NOT NULL IDENTITY,
    [InspectionRecordId] int NOT NULL,
    [ItemName] nvarchar(max) NOT NULL,
    [Requirement] nvarchar(max) NULL,
    [Result] nvarchar(max) NULL,
    [IsPassed] bit NOT NULL,
    [Remarks] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_InspectionChecklists] PRIMARY KEY ([InspectionChecklistId]),
    CONSTRAINT [FK_InspectionChecklists_InspectionRecords_InspectionRecordId] FOREIGN KEY ([InspectionRecordId]) REFERENCES [InspectionRecords] ([InspectionRecordId]) ON DELETE NO ACTION
);

CREATE TABLE [NonConformanceReports] (
    [NonConformanceReportId] int NOT NULL IDENTITY,
    [NCRNumber] nvarchar(100) NOT NULL,
    [ProjectId] int NOT NULL,
    [QCDisciplineId] int NOT NULL,
    [EquipmentId] int NULL,
    [ITPLineItemId] int NULL,
    [InspectionRecordId] int NULL,
    [Description] nvarchar(max) NOT NULL,
    [Severity] nvarchar(20) NOT NULL,
    [Disposition] nvarchar(max) NULL,
    [RootCause] nvarchar(max) NULL,
    [CorrectiveAction] nvarchar(max) NULL,
    [PreventiveAction] nvarchar(max) NULL,
    [Status] nvarchar(30) NOT NULL,
    [RaisedById] int NULL,
    [RaisedDate] datetime2 NOT NULL,
    [DispositionedById] int NULL,
    [DispositionedDate] datetime2 NULL,
    [ClosedById] int NULL,
    [ClosedDate] datetime2 NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_NonConformanceReports] PRIMARY KEY ([NonConformanceReportId]),
    CONSTRAINT [FK_NonConformanceReports_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_NonConformanceReports_ITPLineItems_ITPLineItemId] FOREIGN KEY ([ITPLineItemId]) REFERENCES [ITPLineItems] ([ITPLineItemId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_NonConformanceReports_InspectionRecords_InspectionRecordId] FOREIGN KEY ([InspectionRecordId]) REFERENCES [InspectionRecords] ([InspectionRecordId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_NonConformanceReports_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([ProjectId]) ON DELETE CASCADE,
    CONSTRAINT [FK_NonConformanceReports_QCDisciplines_QCDisciplineId] FOREIGN KEY ([QCDisciplineId]) REFERENCES [QCDisciplines] ([QCDisciplineId]) ON DELETE NO ACTION
);

CREATE TABLE [FailureRecords] (
    [FailureRecordId] int NOT NULL IDENTITY,
    [EquipmentId] int NOT NULL,
    [WorkOrderId] int NULL,
    [FailureDate] datetime2 NOT NULL,
    [FailureMode] nvarchar(max) NOT NULL,
    [FailureCause] nvarchar(max) NULL,
    [FailureEffect] nvarchar(max) NULL,
    [RootCause] nvarchar(max) NULL,
    [CorrectiveAction] nvarchar(max) NULL,
    [DowntimeHours] decimal(18,3) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_FailureRecords] PRIMARY KEY ([FailureRecordId]),
    CONSTRAINT [FK_FailureRecords_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE CASCADE,
    CONSTRAINT [FK_FailureRecords_WorkOrders_WorkOrderId] FOREIGN KEY ([WorkOrderId]) REFERENCES [WorkOrders] ([WorkOrderId]) ON DELETE NO ACTION
);

CREATE TABLE [MaintenanceDocuments] (
    [DocumentId] int NOT NULL,
    [EquipmentId] int NOT NULL,
    [WorkOrderId] int NULL,
    CONSTRAINT [PK_MaintenanceDocuments] PRIMARY KEY ([DocumentId]),
    CONSTRAINT [FK_MaintenanceDocuments_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [Documents] ([DocumentId]) ON DELETE CASCADE,
    CONSTRAINT [FK_MaintenanceDocuments_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MaintenanceDocuments_WorkOrders_WorkOrderId] FOREIGN KEY ([WorkOrderId]) REFERENCES [WorkOrders] ([WorkOrderId]) ON DELETE NO ACTION
);

CREATE TABLE [MaintenanceHistories] (
    [MaintenanceHistoryId] int NOT NULL IDENTITY,
    [EquipmentId] int NOT NULL,
    [WorkOrderId] int NULL,
    [ExecutionDate] datetime2 NOT NULL,
    [Finding] nvarchar(max) NULL,
    [ActionTaken] nvarchar(max) NULL,
    [Technician] nvarchar(max) NULL,
    [DurationHours] decimal(18,3) NULL,
    [Cost] decimal(18,2) NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_MaintenanceHistories] PRIMARY KEY ([MaintenanceHistoryId]),
    CONSTRAINT [FK_MaintenanceHistories_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE CASCADE,
    CONSTRAINT [FK_MaintenanceHistories_WorkOrders_WorkOrderId] FOREIGN KEY ([WorkOrderId]) REFERENCES [WorkOrders] ([WorkOrderId]) ON DELETE NO ACTION
);

CREATE TABLE [MaintenanceSpareUsages] (
    [MaintenanceSpareUsageId] int NOT NULL IDENTITY,
    [WorkOrderId] int NOT NULL,
    [SparePartId] int NOT NULL,
    [Quantity] decimal(18,3) NOT NULL,
    [UnitCost] decimal(18,2) NULL,
    [TotalCost] decimal(18,2) NULL,
    [UsageDate] datetime2 NOT NULL,
    [Description] nvarchar(max) NULL,
    [CreatedDate] datetime2 NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_MaintenanceSpareUsages] PRIMARY KEY ([MaintenanceSpareUsageId]),
    CONSTRAINT [FK_MaintenanceSpareUsages_SpareParts_SparePartId] FOREIGN KEY ([SparePartId]) REFERENCES [SpareParts] ([SparePartId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MaintenanceSpareUsages_WorkOrders_WorkOrderId] FOREIGN KEY ([WorkOrderId]) REFERENCES [WorkOrders] ([WorkOrderId]) ON DELETE NO ACTION
);

CREATE TABLE [WorkOrderDisciplineFindings] (
    [WorkOrderDisciplineFindingId] int NOT NULL IDENTITY,
    [WorkOrderId] int NOT NULL,
    [DisciplineId] int NOT NULL,
    [CycleNumber] int NOT NULL,
    [Decision] nvarchar(20) NOT NULL,
    [Findings] nvarchar(max) NULL,
    [FailureModeCode] nvarchar(max) NULL,
    [FailureCauseCode] nvarchar(max) NULL,
    [RespondedById] int NULL,
    [RespondedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_WorkOrderDisciplineFindings] PRIMARY KEY ([WorkOrderDisciplineFindingId]),
    CONSTRAINT [FK_WorkOrderDisciplineFindings_Disciplines_DisciplineId] FOREIGN KEY ([DisciplineId]) REFERENCES [Disciplines] ([DisciplineId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkOrderDisciplineFindings_WorkOrders_WorkOrderId] FOREIGN KEY ([WorkOrderId]) REFERENCES [WorkOrders] ([WorkOrderId]) ON DELETE NO ACTION
);

CREATE TABLE [WorkOrderDisciplineReferrals] (
    [WorkOrderDisciplineReferralId] int NOT NULL IDENTITY,
    [WorkOrderId] int NOT NULL,
    [DisciplineId] int NOT NULL,
    [CycleNumber] int NOT NULL,
    [CreatedById] int NULL,
    [CreatedDate] datetime2 NOT NULL,
    CONSTRAINT [PK_WorkOrderDisciplineReferrals] PRIMARY KEY ([WorkOrderDisciplineReferralId]),
    CONSTRAINT [FK_WorkOrderDisciplineReferrals_Disciplines_DisciplineId] FOREIGN KEY ([DisciplineId]) REFERENCES [Disciplines] ([DisciplineId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkOrderDisciplineReferrals_WorkOrders_WorkOrderId] FOREIGN KEY ([WorkOrderId]) REFERENCES [WorkOrders] ([WorkOrderId]) ON DELETE NO ACTION
);

CREATE TABLE [WorkOrderLabors] (
    [WorkOrderLaborId] int NOT NULL IDENTITY,
    [WorkOrderId] int NOT NULL,
    [EmployeeId] int NOT NULL,
    [TeamId] int NULL,
    [WorkDate] datetime2 NOT NULL,
    [RegularHours] decimal(10,2) NOT NULL,
    [OvertimeHours] decimal(10,2) NOT NULL,
    [Rate] decimal(18,2) NOT NULL,
    [OvertimeRate] decimal(18,2) NOT NULL,
    [LaborCost] decimal(18,2) NOT NULL,
    [Notes] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    [CreatedDate] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_WorkOrderLabors] PRIMARY KEY ([WorkOrderLaborId]),
    CONSTRAINT [FK_WorkOrderLabors_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkOrderLabors_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([TeamId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_WorkOrderLabors_WorkOrders_WorkOrderId] FOREIGN KEY ([WorkOrderId]) REFERENCES [WorkOrders] ([WorkOrderId]) ON DELETE NO ACTION
);

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'DisciplineId', N'Code', N'CreatedDate', N'Description', N'ImportAliases', N'IsActive', N'Name') AND [object_id] = OBJECT_ID(N'[Disciplines]'))
    SET IDENTITY_INSERT [Disciplines] ON;
INSERT INTO [Disciplines] ([DisciplineId], [Code], [CreatedDate], [Description], [ImportAliases], [IsActive], [Name])
VALUES (1, N'PROC', '2026-01-01T00:00:00.0000000', N'Process Engineering', N'PR,PRO,PROCESS,PROCS', CAST(1 AS bit), N'Process'),
(2, N'MECH', '2026-01-01T00:00:00.0000000', N'Mechanical Engineering', N'ME,MEC,MECHANICAL,ROTATING,STATIC', CAST(1 AS bit), N'Mechanical'),
(3, N'ELEC', '2026-01-01T00:00:00.0000000', N'Electrical Engineering', N'EL,ELE,ELECTRICAL,PWR,POWER', CAST(1 AS bit), N'Electrical'),
(4, N'INST', '2026-01-01T00:00:00.0000000', N'Instrumentation and Control Engineering', N'IN,INS,IC,I&C,INSTRUMENT,CONTROL,INSTRUMENTATION', CAST(1 AS bit), N'Instrumentation'),
(5, N'PIPE', '2026-01-01T00:00:00.0000000', N'Piping Engineering', N'PI,PIP,PL,PIPING,PIPELINE', CAST(1 AS bit), N'Piping'),
(6, N'CIVL', '2026-01-01T00:00:00.0000000', N'Civil Engineering', N'CI,CIV,ST,STR,CIVIL,STRUCTURAL,CS', CAST(1 AS bit), N'Civil');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'DisciplineId', N'Code', N'CreatedDate', N'Description', N'ImportAliases', N'IsActive', N'Name') AND [object_id] = OBJECT_ID(N'[Disciplines]'))
    SET IDENTITY_INSERT [Disciplines] OFF;

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ProjectId', N'ClientName', N'CreatedDate', N'EndDate', N'IsActive', N'Location', N'ProjectCode', N'ProjectName', N'StartDate', N'Status') AND [object_id] = OBJECT_ID(N'[Projects]'))
    SET IDENTITY_INSERT [Projects] ON;
INSERT INTO [Projects] ([ProjectId], [ClientName], [CreatedDate], [EndDate], [IsActive], [Location], [ProjectCode], [ProjectName], [StartDate], [Status])
VALUES (1, N'Petrochemical', '2026-01-01T00:00:00.0000000', NULL, CAST(1 AS bit), N'Assaluyeh', N'PEMS-001', N'Polymer Plant Development', NULL, N'Active');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'ProjectId', N'ClientName', N'CreatedDate', N'EndDate', N'IsActive', N'Location', N'ProjectCode', N'ProjectName', N'StartDate', N'Status') AND [object_id] = OBJECT_ID(N'[Projects]'))
    SET IDENTITY_INSERT [Projects] OFF;

CREATE INDEX [IX_AIDocumentClassifications_AttachmentId] ON [AIDocumentClassifications] ([AttachmentId]);

CREATE INDEX [IX_AIDocumentClassifications_DocumentId] ON [AIDocumentClassifications] ([DocumentId]);

CREATE INDEX [IX_AIExtractedSpecifications_AttachmentId] ON [AIExtractedSpecifications] ([AttachmentId]);

CREATE INDEX [IX_AIExtractedSpecifications_ReviewedById] ON [AIExtractedSpecifications] ([ReviewedById]);

CREATE INDEX [IX_AIExtractedTags_AttachmentId] ON [AIExtractedTags] ([AttachmentId]);

CREATE INDEX [IX_AIExtractedTags_CreatedTagId] ON [AIExtractedTags] ([CreatedTagId]);

CREATE INDEX [IX_AIExtractedTags_LinkedEquipmentId] ON [AIExtractedTags] ([LinkedEquipmentId]);

CREATE INDEX [IX_AIExtractedTags_ReviewedById] ON [AIExtractedTags] ([ReviewedById]);

CREATE INDEX [IX_Alarms_InstrumentId] ON [Alarms] ([InstrumentId]);

CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);

CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;

CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);

CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);

CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);

CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);

CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;

CREATE INDEX [IX_Assets_EquipmentId] ON [Assets] ([EquipmentId]);

CREATE INDEX [IX_CableConnections_CableId] ON [CableConnections] ([CableId]);

CREATE INDEX [IX_CauseEffects_AlarmId] ON [CauseEffects] ([AlarmId]);

CREATE INDEX [IX_ContractDocuments_ContractId] ON [ContractDocuments] ([ContractId]);

CREATE INDEX [IX_Contracts_ProjectId] ON [Contracts] ([ProjectId]);

CREATE INDEX [IX_Contracts_VendorId] ON [Contracts] ([VendorId]);

CREATE INDEX [IX_ControlLoops_InstrumentId] ON [ControlLoops] ([InstrumentId]);

CREATE UNIQUE INDEX [IX_Departments_Name] ON [Departments] ([Name]);

CREATE INDEX [IX_DisciplineTasks_DisciplineId] ON [DisciplineTasks] ([DisciplineId]);

CREATE INDEX [IX_DisciplineTasks_ProjectId] ON [DisciplineTasks] ([ProjectId]);

CREATE INDEX [IX_DisciplineTasks_ResponsibleEmployeeId] ON [DisciplineTasks] ([ResponsibleEmployeeId]);

CREATE INDEX [IX_DocumentAttachments_DocumentId] ON [DocumentAttachments] ([DocumentId]);

CREATE INDEX [IX_DocumentAttachments_IntakeProjectId] ON [DocumentAttachments] ([IntakeProjectId]);

CREATE INDEX [IX_DocumentAttachments_LetterId] ON [DocumentAttachments] ([LetterId]);

CREATE INDEX [IX_DocumentAttachments_MOCId] ON [DocumentAttachments] ([MOCId]);

CREATE INDEX [IX_DocumentAttachments_TechnicalQueryId] ON [DocumentAttachments] ([TechnicalQueryId]);

CREATE INDEX [IX_DocumentAttachments_UploadedById] ON [DocumentAttachments] ([UploadedById]);

CREATE INDEX [IX_DocumentComments_AttachmentId] ON [DocumentComments] ([AttachmentId]);

CREATE INDEX [IX_DocumentComments_DocumentId] ON [DocumentComments] ([DocumentId]);

CREATE INDEX [IX_DocumentComments_ParentCommentId] ON [DocumentComments] ([ParentCommentId]);

CREATE INDEX [IX_DocumentComments_ReplyAttachmentId] ON [DocumentComments] ([ReplyAttachmentId]);

CREATE INDEX [IX_DocumentComments_VDRSubmissionId] ON [DocumentComments] ([VDRSubmissionId]);

CREATE INDEX [IX_DocumentIdcReferrals_DisciplineId] ON [DocumentIdcReferrals] ([DisciplineId]);

CREATE UNIQUE INDEX [IX_DocumentIdcReferrals_DocumentId_DisciplineId] ON [DocumentIdcReferrals] ([DocumentId], [DisciplineId]);

CREATE INDEX [IX_DocumentRevisions_ApprovedById] ON [DocumentRevisions] ([ApprovedById]);

CREATE INDEX [IX_DocumentRevisions_CheckedById] ON [DocumentRevisions] ([CheckedById]);

CREATE INDEX [IX_DocumentRevisions_CreatedById] ON [DocumentRevisions] ([CreatedById]);

CREATE UNIQUE INDEX [IX_DocumentRevisions_DocumentId_RevisionNo] ON [DocumentRevisions] ([DocumentId], [RevisionNo]);

CREATE INDEX [IX_DocumentRevisions_PreparedById] ON [DocumentRevisions] ([PreparedById]);

CREATE INDEX [IX_DocumentRevisions_ReviewerId] ON [DocumentRevisions] ([ReviewerId]);

CREATE INDEX [IX_DocumentRevisions_SupersededRevisionId] ON [DocumentRevisions] ([SupersededRevisionId]);

CREATE INDEX [IX_Documents_DisciplineId] ON [Documents] ([DisciplineId]);

CREATE INDEX [IX_Documents_IsActive_Status] ON [Documents] ([IsActive], [Status]);

CREATE INDEX [IX_Documents_ProjectId] ON [Documents] ([ProjectId]);

CREATE INDEX [IX_DocumentTagLinks_DocumentId] ON [DocumentTagLinks] ([DocumentId]);

CREATE INDEX [IX_DocumentTagLinks_EquipmentTagId] ON [DocumentTagLinks] ([EquipmentTagId]);

CREATE INDEX [IX_DocumentWorkflows_ActionById] ON [DocumentWorkflows] ([ActionById]);

CREATE INDEX [IX_DocumentWorkflows_DocumentId] ON [DocumentWorkflows] ([DocumentId]);

CREATE INDEX [IX_ElectricalEquipments_EquipmentId] ON [ElectricalEquipments] ([EquipmentId]);

CREATE INDEX [IX_EmployeeLaborRates_EmployeeId] ON [EmployeeLaborRates] ([EmployeeId]);

CREATE INDEX [IX_Employees_DepartmentId] ON [Employees] ([DepartmentId]);

CREATE INDEX [IX_Employees_DisciplineId] ON [Employees] ([DisciplineId]);

CREATE UNIQUE INDEX [IX_Employees_PersonnelCode] ON [Employees] ([PersonnelCode]) WHERE [PersonnelCode] IS NOT NULL AND [PersonnelCode] <> '';

CREATE INDEX [IX_EngineeringDocuments_ApprovedById] ON [EngineeringDocuments] ([ApprovedById]);

CREATE INDEX [IX_EngineeringDocuments_CheckedById] ON [EngineeringDocuments] ([CheckedById]);

CREATE INDEX [IX_EngineeringDocuments_ContractId] ON [EngineeringDocuments] ([ContractId]);

CREATE INDEX [IX_EngineeringDocuments_DeletedById] ON [EngineeringDocuments] ([DeletedById]);

CREATE INDEX [IX_EngineeringDocuments_EquipmentId] ON [EngineeringDocuments] ([EquipmentId]);

CREATE INDEX [IX_EngineeringDocuments_PreparedById] ON [EngineeringDocuments] ([PreparedById]);

CREATE INDEX [IX_EngineeringDocuments_RelatedMRId] ON [EngineeringDocuments] ([RelatedMRId]);

CREATE INDEX [IX_EngineeringDocuments_VendorId] ON [EngineeringDocuments] ([VendorId]);

CREATE INDEX [IX_EquipmentCriticalities_EquipmentId] ON [EquipmentCriticalities] ([EquipmentId]);

CREATE INDEX [IX_EquipmentDatasheets_EquipmentId] ON [EquipmentDatasheets] ([EquipmentId]);

CREATE INDEX [IX_EquipmentImportBatches_ImportedById] ON [EquipmentImportBatches] ([ImportedById]);

CREATE INDEX [IX_EquipmentImportBatches_ProjectId] ON [EquipmentImportBatches] ([ProjectId]);

CREATE INDEX [IX_EquipmentImportRows_EquipmentImportBatchId] ON [EquipmentImportRows] ([EquipmentImportBatchId]);

CREATE INDEX [IX_EquipmentNozzles_EquipmentId] ON [EquipmentNozzles] ([EquipmentId]);

CREATE INDEX [IX_Equipments_DisciplineId] ON [Equipments] ([DisciplineId]);

CREATE INDEX [IX_Equipments_ProjectId] ON [Equipments] ([ProjectId]);

CREATE INDEX [IX_Equipments_RelatedMRId] ON [Equipments] ([RelatedMRId]);

CREATE INDEX [IX_EquipmentTags_EquipmentId] ON [EquipmentTags] ([EquipmentId]);

CREATE INDEX [IX_FailureRecords_EquipmentId] ON [FailureRecords] ([EquipmentId]);

CREATE INDEX [IX_FailureRecords_WorkOrderId] ON [FailureRecords] ([WorkOrderId]);

CREATE INDEX [IX_FunctionalLocations_ParentId] ON [FunctionalLocations] ([ParentId]);

CREATE INDEX [IX_InspectionChecklists_InspectionRecordId] ON [InspectionChecklists] ([InspectionRecordId]);

CREATE INDEX [IX_InspectionRecords_EquipmentId] ON [InspectionRecords] ([EquipmentId]);

CREATE INDEX [IX_InspectionRecords_ITPLineItemId] ON [InspectionRecords] ([ITPLineItemId]);

CREATE INDEX [IX_InspectionRecords_QCDisciplineId] ON [InspectionRecords] ([QCDisciplineId]);

CREATE INDEX [IX_InspectionTestPlans_EquipmentId] ON [InspectionTestPlans] ([EquipmentId]);

CREATE UNIQUE INDEX [IX_InspectionTestPlans_ProjectId_ITPNumber_Revision] ON [InspectionTestPlans] ([ProjectId], [ITPNumber], [Revision]);

CREATE INDEX [IX_InspectionTestPlans_QCDisciplineId] ON [InspectionTestPlans] ([QCDisciplineId]);

CREATE INDEX [IX_Instruments_EquipmentId] ON [Instruments] ([EquipmentId]);

CREATE INDEX [IX_IOPoints_InstrumentId] ON [IOPoints] ([InstrumentId]);

CREATE INDEX [IX_ITPLineItems_InspectionTestPlanId_SequenceNo] ON [ITPLineItems] ([InspectionTestPlanId], [SequenceNo]);

CREATE INDEX [IX_ITPLineItems_QCDisciplineId] ON [ITPLineItems] ([QCDisciplineId]);

CREATE INDEX [IX_KnowledgeBaseEntries_SourceType_SourceId] ON [KnowledgeBaseEntries] ([SourceType], [SourceId]);

CREATE INDEX [IX_LetterRecipients_DepartmentId] ON [LetterRecipients] ([DepartmentId]);

CREATE INDEX [IX_LetterRecipients_EmployeeId] ON [LetterRecipients] ([EmployeeId]);

CREATE INDEX [IX_LetterRecipients_LetterId] ON [LetterRecipients] ([LetterId]);

CREATE INDEX [IX_LetterReferrals_LetterId] ON [LetterReferrals] ([LetterId]);

CREATE INDEX [IX_LetterReferrals_ReferredToDepartmentId] ON [LetterReferrals] ([ReferredToDepartmentId]);

CREATE INDEX [IX_LetterReferrals_ReferredToEmployeeId] ON [LetterReferrals] ([ReferredToEmployeeId]);

CREATE INDEX [IX_Letters_DisciplineId] ON [Letters] ([DisciplineId]);

CREATE INDEX [IX_Letters_IsActive_LetterDate] ON [Letters] ([IsActive], [LetterDate]);

CREATE INDEX [IX_Letters_LetterNumber] ON [Letters] ([LetterNumber]);

CREATE INDEX [IX_Letters_LetterType] ON [Letters] ([LetterType]);

CREATE INDEX [IX_Letters_ParentLetterId] ON [Letters] ([ParentLetterId]);

CREATE INDEX [IX_Letters_ProjectId] ON [Letters] ([ProjectId]);

CREATE INDEX [IX_Letters_ReceiverDepartmentId] ON [Letters] ([ReceiverDepartmentId]);

CREATE INDEX [IX_Letters_ReceiverEmployeeId] ON [Letters] ([ReceiverEmployeeId]);

CREATE INDEX [IX_Letters_RelatedDocumentId] ON [Letters] ([RelatedDocumentId]);

CREATE INDEX [IX_Letters_RelatedMRId] ON [Letters] ([RelatedMRId]);

CREATE INDEX [IX_Letters_Status] ON [Letters] ([Status]);

CREATE UNIQUE INDEX [IX_LetterStars_LetterId_ApplicationUserId] ON [LetterStars] ([LetterId], [ApplicationUserId]);

CREATE UNIQUE INDEX [IX_LetterTags_LetterId_TagId] ON [LetterTags] ([LetterId], [TagId]);

CREATE INDEX [IX_LetterTags_TagId] ON [LetterTags] ([TagId]);

CREATE INDEX [IX_MaintenanceDocuments_EquipmentId] ON [MaintenanceDocuments] ([EquipmentId]);

CREATE INDEX [IX_MaintenanceDocuments_WorkOrderId] ON [MaintenanceDocuments] ([WorkOrderId]);

CREATE INDEX [IX_MaintenanceHistories_EquipmentId] ON [MaintenanceHistories] ([EquipmentId]);

CREATE INDEX [IX_MaintenanceHistories_WorkOrderId] ON [MaintenanceHistories] ([WorkOrderId]);

CREATE INDEX [IX_MaintenancePlans_EquipmentId] ON [MaintenancePlans] ([EquipmentId]);

CREATE INDEX [IX_MaintenancePlans_MaintenanceStrategyId] ON [MaintenancePlans] ([MaintenanceStrategyId]);

CREATE UNIQUE INDEX [IX_MaintenanceRoles_DisciplineId_Name] ON [MaintenanceRoles] ([DisciplineId], [Name]);

CREATE INDEX [IX_MaintenanceSpareUsages_SparePartId] ON [MaintenanceSpareUsages] ([SparePartId]);

CREATE INDEX [IX_MaintenanceSpareUsages_WorkOrderId] ON [MaintenanceSpareUsages] ([WorkOrderId]);

CREATE INDEX [IX_MaintenanceStrategies_EquipmentId] ON [MaintenanceStrategies] ([EquipmentId]);

CREATE INDEX [IX_MaterialRequisitionItems_MRId] ON [MaterialRequisitionItems] ([MRId]);

CREATE INDEX [IX_MaterialRequisitionQuotations_MRId] ON [MaterialRequisitionQuotations] ([MRId]);

CREATE INDEX [IX_MaterialRequisitionQuotations_ProcurementPackageId] ON [MaterialRequisitionQuotations] ([ProcurementPackageId]);

CREATE INDEX [IX_MaterialRequisitions_DisciplineId] ON [MaterialRequisitions] ([DisciplineId]);

CREATE INDEX [IX_MaterialRequisitions_ProcurementPackageId] ON [MaterialRequisitions] ([ProcurementPackageId]);

CREATE INDEX [IX_MaterialRequisitions_ProjectId] ON [MaterialRequisitions] ([ProjectId]);

CREATE INDEX [IX_MaterialRequisitions_RelatedDocumentId] ON [MaterialRequisitions] ([RelatedDocumentId]);

CREATE INDEX [IX_MaterialRequisitions_ResponsibleEngineerId] ON [MaterialRequisitions] ([ResponsibleEngineerId]);

CREATE INDEX [IX_MDRImportBatches_ProjectId] ON [MDRImportBatches] ([ProjectId]);

CREATE INDEX [IX_MDRImportRows_MDRImportBatchId] ON [MDRImportRows] ([MDRImportBatchId]);

CREATE INDEX [IX_MechanicalEquipments_EquipmentId] ON [MechanicalEquipments] ([EquipmentId]);

CREATE INDEX [IX_MeetingAttendees_EmployeeId] ON [MeetingAttendees] ([EmployeeId]);

CREATE INDEX [IX_MeetingAttendees_MeetingId] ON [MeetingAttendees] ([MeetingId]);

CREATE INDEX [IX_Meetings_ProjectId] ON [Meetings] ([ProjectId]);

CREATE INDEX [IX_MOCs_DisciplineId] ON [MOCs] ([DisciplineId]);

CREATE UNIQUE INDEX [IX_MOCs_ProjectId_MOCNumber] ON [MOCs] ([ProjectId], [MOCNumber]);

CREATE INDEX [IX_MOCs_RelatedDocumentId] ON [MOCs] ([RelatedDocumentId]);

CREATE INDEX [IX_MOCs_RelatedEquipmentId] ON [MOCs] ([RelatedEquipmentId]);

CREATE INDEX [IX_NonConformanceReports_EquipmentId] ON [NonConformanceReports] ([EquipmentId]);

CREATE INDEX [IX_NonConformanceReports_InspectionRecordId] ON [NonConformanceReports] ([InspectionRecordId]);

CREATE INDEX [IX_NonConformanceReports_ITPLineItemId] ON [NonConformanceReports] ([ITPLineItemId]);

CREATE UNIQUE INDEX [IX_NonConformanceReports_ProjectId_NCRNumber] ON [NonConformanceReports] ([ProjectId], [NCRNumber]);

CREATE INDEX [IX_NonConformanceReports_QCDisciplineId] ON [NonConformanceReports] ([QCDisciplineId]);

CREATE INDEX [IX_Notifications_UserId_IsRead] ON [Notifications] ([UserId], [IsRead]);

CREATE INDEX [IX_ProcurementApprovals_ProcurementPackageId] ON [ProcurementApprovals] ([ProcurementPackageId]);

CREATE INDEX [IX_ProcurementCommunications_AttachmentId] ON [ProcurementCommunications] ([AttachmentId]);

CREATE INDEX [IX_ProcurementCommunications_ProcurementPackageId] ON [ProcurementCommunications] ([ProcurementPackageId]);

CREATE INDEX [IX_ProcurementCommunications_VendorId] ON [ProcurementCommunications] ([VendorId]);

CREATE INDEX [IX_ProcurementPackages_DisciplineId] ON [ProcurementPackages] ([DisciplineId]);

CREATE INDEX [IX_ProcurementPackages_ProjectId] ON [ProcurementPackages] ([ProjectId]);

CREATE INDEX [IX_PurchaseOrders_ContractId] ON [PurchaseOrders] ([ContractId]);

CREATE INDEX [IX_PurchaseOrders_ProjectId] ON [PurchaseOrders] ([ProjectId]);

CREATE INDEX [IX_PurchaseOrders_VendorId] ON [PurchaseOrders] ([VendorId]);

CREATE UNIQUE INDEX [IX_QCDisciplines_Code] ON [QCDisciplines] ([Code]);

CREATE UNIQUE INDEX [IX_RolePermissions_RoleName_ModuleName] ON [RolePermissions] ([RoleName], [ModuleName]);

CREATE INDEX [IX_RotatingEquipments_MechanicalEquipmentId] ON [RotatingEquipments] ([MechanicalEquipmentId]);

CREATE INDEX [IX_SavedFilters_ApplicationUserId_PageKey] ON [SavedFilters] ([ApplicationUserId], [PageKey]);

CREATE INDEX [IX_StaticEquipments_MechanicalEquipmentId] ON [StaticEquipments] ([MechanicalEquipmentId]);

CREATE INDEX [IX_TaskItems_AssignedToEmployeeId] ON [TaskItems] ([AssignedToEmployeeId]);

CREATE INDEX [IX_TaskItems_RelatedType_RelatedId] ON [TaskItems] ([RelatedType], [RelatedId]);

CREATE INDEX [IX_TeamMembers_EmployeeId] ON [TeamMembers] ([EmployeeId]);

CREATE INDEX [IX_TeamMembers_RoleId] ON [TeamMembers] ([RoleId]);

CREATE UNIQUE INDEX [IX_TeamMembers_TeamId_EmployeeId_IsActive] ON [TeamMembers] ([TeamId], [EmployeeId], [IsActive]) WHERE [IsActive] = 1;

CREATE UNIQUE INDEX [IX_Teams_Code] ON [Teams] ([Code]);

CREATE INDEX [IX_Teams_DisciplineId] ON [Teams] ([DisciplineId]);

CREATE UNIQUE INDEX [IX_Teams_Name] ON [Teams] ([Name]);

CREATE INDEX [IX_Teams_SupervisorEmployeeId] ON [Teams] ([SupervisorEmployeeId]);

CREATE INDEX [IX_TechnicalQueries_DisciplineId] ON [TechnicalQueries] ([DisciplineId]);

CREATE UNIQUE INDEX [IX_TechnicalQueries_ProjectId_QueryNumber] ON [TechnicalQueries] ([ProjectId], [QueryNumber]);

CREATE INDEX [IX_TechnicalQueries_RelatedDocumentId] ON [TechnicalQueries] ([RelatedDocumentId]);

CREATE INDEX [IX_TechnicalQueries_RelatedEquipmentId] ON [TechnicalQueries] ([RelatedEquipmentId]);

CREATE INDEX [IX_TransmittalItems_DocumentId] ON [TransmittalItems] ([DocumentId]);

CREATE INDEX [IX_TransmittalItems_TransmittalId] ON [TransmittalItems] ([TransmittalId]);

CREATE INDEX [IX_Transmittals_ProjectId] ON [Transmittals] ([ProjectId]);

CREATE INDEX [IX_Transmittals_SentById] ON [Transmittals] ([SentById]);

CREATE UNIQUE INDEX [IX_UserDashboardWidgets_ApplicationUserId_WidgetKey] ON [UserDashboardWidgets] ([ApplicationUserId], [WidgetKey]);

CREATE INDEX [IX_VDRImportBatches_ProjectId] ON [VDRImportBatches] ([ProjectId]);

CREATE INDEX [IX_VDRImportRows_VDRImportBatchId] ON [VDRImportRows] ([VDRImportBatchId]);

CREATE INDEX [IX_VDRItems_DocumentNumber] ON [VDRItems] ([DocumentNumber]);

CREATE INDEX [IX_VDRItems_EquipmentId] ON [VDRItems] ([EquipmentId]);

CREATE INDEX [IX_VDRItems_MRId] ON [VDRItems] ([MRId]);

CREATE INDEX [IX_VDRItems_PoId] ON [VDRItems] ([PoId]);

CREATE INDEX [IX_VDRItems_VendorId] ON [VDRItems] ([VendorId]);

CREATE INDEX [IX_VDRSubmissions_AttachmentId] ON [VDRSubmissions] ([AttachmentId]);

CREATE INDEX [IX_VDRSubmissions_ClientTransmittalId] ON [VDRSubmissions] ([ClientTransmittalId]);

CREATE INDEX [IX_VDRSubmissions_CommentSheetAttachmentId] ON [VDRSubmissions] ([CommentSheetAttachmentId]);

CREATE INDEX [IX_VDRSubmissions_ReplySheetAttachmentId] ON [VDRSubmissions] ([ReplySheetAttachmentId]);

CREATE UNIQUE INDEX [IX_VDRSubmissions_VDRItemId_RevisionNo] ON [VDRSubmissions] ([VDRItemId], [RevisionNo]);

CREATE UNIQUE INDEX [IX_WorkflowDefinitions_Module_Category_Name] ON [WorkflowDefinitions] ([Module], [Category], [Name]);

CREATE INDEX [IX_WorkflowHistories_WorkflowInstanceId] ON [WorkflowHistories] ([WorkflowInstanceId]);

CREATE INDEX [IX_WorkflowInstances_CurrentStageId] ON [WorkflowInstances] ([CurrentStageId]);

CREATE INDEX [IX_WorkflowInstances_Module_BusinessObjectType_BusinessObjectId_Status] ON [WorkflowInstances] ([Module], [BusinessObjectType], [BusinessObjectId], [Status]);

CREATE INDEX [IX_WorkflowInstances_WorkflowVersionId] ON [WorkflowInstances] ([WorkflowVersionId]);

CREATE INDEX [IX_WorkflowInstanceStages_AssignedToEmployeeId_Status] ON [WorkflowInstanceStages] ([AssignedToEmployeeId], [Status]);

CREATE INDEX [IX_WorkflowInstanceStages_WorkflowInstanceId] ON [WorkflowInstanceStages] ([WorkflowInstanceId]);

CREATE INDEX [IX_WorkflowInstanceStages_WorkflowStageId] ON [WorkflowInstanceStages] ([WorkflowStageId]);

CREATE INDEX [IX_WorkflowStages_WorkflowVersionId_StageOrder] ON [WorkflowStages] ([WorkflowVersionId], [StageOrder]);

CREATE INDEX [IX_WorkflowTransitions_FromStageId] ON [WorkflowTransitions] ([FromStageId]);

CREATE INDEX [IX_WorkflowTransitions_ToStageId] ON [WorkflowTransitions] ([ToStageId]);

CREATE INDEX [IX_WorkflowTransitions_WorkflowVersionId] ON [WorkflowTransitions] ([WorkflowVersionId]);

CREATE UNIQUE INDEX [IX_WorkflowVersions_WorkflowDefinitionId_VersionNumber] ON [WorkflowVersions] ([WorkflowDefinitionId], [VersionNumber]);

CREATE INDEX [IX_WorkOrderDisciplineFindings_DisciplineId] ON [WorkOrderDisciplineFindings] ([DisciplineId]);

CREATE INDEX [IX_WorkOrderDisciplineFindings_WorkOrderId] ON [WorkOrderDisciplineFindings] ([WorkOrderId]);

CREATE INDEX [IX_WorkOrderDisciplineReferrals_DisciplineId] ON [WorkOrderDisciplineReferrals] ([DisciplineId]);

CREATE UNIQUE INDEX [IX_WorkOrderDisciplineReferrals_WorkOrderId_DisciplineId_CycleNumber] ON [WorkOrderDisciplineReferrals] ([WorkOrderId], [DisciplineId], [CycleNumber]);

CREATE INDEX [IX_WorkOrderLabors_EmployeeId] ON [WorkOrderLabors] ([EmployeeId]);

CREATE INDEX [IX_WorkOrderLabors_TeamId] ON [WorkOrderLabors] ([TeamId]);

CREATE INDEX [IX_WorkOrderLabors_WorkOrderId] ON [WorkOrderLabors] ([WorkOrderId]);

CREATE INDEX [IX_WorkOrders_AssignedEmployeeId] ON [WorkOrders] ([AssignedEmployeeId]);

CREATE INDEX [IX_WorkOrders_AssignedTeamId] ON [WorkOrders] ([AssignedTeamId]);

CREATE INDEX [IX_WorkOrders_EquipmentId] ON [WorkOrders] ([EquipmentId]);

CREATE INDEX [IX_WorkOrders_MaintenancePlanId] ON [WorkOrders] ([MaintenancePlanId]);

CREATE UNIQUE INDEX [IX_WorkOrders_WorkOrderNumber] ON [WorkOrders] ([WorkOrderNumber]);

ALTER TABLE [AIDocumentClassifications] ADD CONSTRAINT [FK_AIDocumentClassifications_DocumentAttachments_AttachmentId] FOREIGN KEY ([AttachmentId]) REFERENCES [DocumentAttachments] ([AttachmentId]) ON DELETE NO ACTION;

ALTER TABLE [AIExtractedSpecifications] ADD CONSTRAINT [FK_AIExtractedSpecifications_DocumentAttachments_AttachmentId] FOREIGN KEY ([AttachmentId]) REFERENCES [DocumentAttachments] ([AttachmentId]) ON DELETE CASCADE;

ALTER TABLE [AIExtractedTags] ADD CONSTRAINT [FK_AIExtractedTags_DocumentAttachments_AttachmentId] FOREIGN KEY ([AttachmentId]) REFERENCES [DocumentAttachments] ([AttachmentId]) ON DELETE CASCADE;

ALTER TABLE [AIExtractedTags] ADD CONSTRAINT [FK_AIExtractedTags_EquipmentTags_CreatedTagId] FOREIGN KEY ([CreatedTagId]) REFERENCES [EquipmentTags] ([TagId]) ON DELETE NO ACTION;

ALTER TABLE [AIExtractedTags] ADD CONSTRAINT [FK_AIExtractedTags_Equipments_LinkedEquipmentId] FOREIGN KEY ([LinkedEquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE NO ACTION;

ALTER TABLE [Alarms] ADD CONSTRAINT [FK_Alarms_Instruments_InstrumentId] FOREIGN KEY ([InstrumentId]) REFERENCES [Instruments] ([InstrumentId]) ON DELETE CASCADE;

ALTER TABLE [Assets] ADD CONSTRAINT [FK_Assets_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE CASCADE;

ALTER TABLE [ControlLoops] ADD CONSTRAINT [FK_ControlLoops_Instruments_InstrumentId] FOREIGN KEY ([InstrumentId]) REFERENCES [Instruments] ([InstrumentId]);

ALTER TABLE [DocumentAttachments] ADD CONSTRAINT [FK_DocumentAttachments_EngineeringDocuments_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [EngineeringDocuments] ([DocumentId]) ON DELETE NO ACTION;

ALTER TABLE [DocumentAttachments] ADD CONSTRAINT [FK_DocumentAttachments_Letters_LetterId] FOREIGN KEY ([LetterId]) REFERENCES [Letters] ([LetterId]) ON DELETE NO ACTION;

ALTER TABLE [DocumentAttachments] ADD CONSTRAINT [FK_DocumentAttachments_MOCs_MOCId] FOREIGN KEY ([MOCId]) REFERENCES [MOCs] ([MOCId]) ON DELETE NO ACTION;

ALTER TABLE [DocumentAttachments] ADD CONSTRAINT [FK_DocumentAttachments_TechnicalQueries_TechnicalQueryId] FOREIGN KEY ([TechnicalQueryId]) REFERENCES [TechnicalQueries] ([TechnicalQueryId]) ON DELETE NO ACTION;

ALTER TABLE [DocumentComments] ADD CONSTRAINT [FK_DocumentComments_VDRSubmissions_VDRSubmissionId] FOREIGN KEY ([VDRSubmissionId]) REFERENCES [VDRSubmissions] ([VDRSubmissionId]) ON DELETE NO ACTION;

ALTER TABLE [DocumentRevisions] ADD CONSTRAINT [FK_DocumentRevisions_EngineeringDocuments_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [EngineeringDocuments] ([DocumentId]) ON DELETE NO ACTION;

ALTER TABLE [DocumentTagLinks] ADD CONSTRAINT [FK_DocumentTagLinks_EquipmentTags_EquipmentTagId] FOREIGN KEY ([EquipmentTagId]) REFERENCES [EquipmentTags] ([TagId]) ON DELETE NO ACTION;

ALTER TABLE [DocumentWorkflows] ADD CONSTRAINT [FK_DocumentWorkflows_EngineeringDocuments_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [EngineeringDocuments] ([DocumentId]) ON DELETE NO ACTION;

ALTER TABLE [ElectricalEquipments] ADD CONSTRAINT [FK_ElectricalEquipments_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]);

ALTER TABLE [EngineeringDocuments] ADD CONSTRAINT [FK_EngineeringDocuments_Equipments_EquipmentId] FOREIGN KEY ([EquipmentId]) REFERENCES [Equipments] ([EquipmentId]) ON DELETE NO ACTION;

ALTER TABLE [EngineeringDocuments] ADD CONSTRAINT [FK_EngineeringDocuments_MaterialRequisitions_RelatedMRId] FOREIGN KEY ([RelatedMRId]) REFERENCES [MaterialRequisitions] ([MRId]) ON DELETE NO ACTION;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260812075929_InitialCreate', N'10.0.10');

COMMIT;
GO

