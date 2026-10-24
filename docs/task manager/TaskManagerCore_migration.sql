BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD [ItemKey] nvarchar(30) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD [ModifiedDate] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD [ParentTaskItemId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD [Rank] float NOT NULL DEFAULT 0.0E0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD [ReporterEmployeeId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD [SprintId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD [StartDate] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD [StoryPoints] decimal(9,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD [WorkItemStatusId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD [WorkItemTypeId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD [WorkSpaceId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE TABLE [WorkItemComments] (
        [WorkItemCommentId] int NOT NULL IDENTITY,
        [TaskItemId] int NOT NULL,
        [AuthorEmployeeId] int NULL,
        [AuthorUserId] int NULL,
        [AuthorName] nvarchar(200) NULL,
        [Body] nvarchar(max) NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [ModifiedBy] nvarchar(200) NULL,
        [RowVersion] rowversion NULL,
        [TenantId] int NULL,
        CONSTRAINT [PK_WorkItemComments] PRIMARY KEY ([WorkItemCommentId]),
        CONSTRAINT [FK_WorkItemComments_TaskItems_TaskItemId] FOREIGN KEY ([TaskItemId]) REFERENCES [TaskItems] ([TaskItemId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE TABLE [WorkItemLinks] (
        [WorkItemLinkId] int NOT NULL IDENTITY,
        [SourceTaskItemId] int NOT NULL,
        [LinkType] nvarchar(20) NOT NULL,
        [TargetTaskItemId] int NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [ModifiedBy] nvarchar(200) NULL,
        [RowVersion] rowversion NULL,
        [TenantId] int NULL,
        CONSTRAINT [PK_WorkItemLinks] PRIMARY KEY ([WorkItemLinkId]),
        CONSTRAINT [FK_WorkItemLinks_TaskItems_SourceTaskItemId] FOREIGN KEY ([SourceTaskItemId]) REFERENCES [TaskItems] ([TaskItemId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WorkItemLinks_TaskItems_TargetTaskItemId] FOREIGN KEY ([TargetTaskItemId]) REFERENCES [TaskItems] ([TaskItemId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE TABLE [WorkItemWatchers] (
        [WorkItemWatcherId] int NOT NULL IDENTITY,
        [TaskItemId] int NOT NULL,
        [EmployeeId] int NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkItemWatchers] PRIMARY KEY ([WorkItemWatcherId]),
        CONSTRAINT [FK_WorkItemWatchers_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WorkItemWatchers_TaskItems_TaskItemId] FOREIGN KEY ([TaskItemId]) REFERENCES [TaskItems] ([TaskItemId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE TABLE [WorkSpaces] (
        [WorkSpaceId] int NOT NULL IDENTITY,
        [Key] nvarchar(10) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [NameFa] nvarchar(150) NULL,
        [Description] nvarchar(1000) NULL,
        [OwnerDepartmentId] int NULL,
        [IsActive] bit NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [ModifiedBy] nvarchar(200) NULL,
        [RowVersion] rowversion NULL,
        [TenantId] int NULL,
        CONSTRAINT [PK_WorkSpaces] PRIMARY KEY ([WorkSpaceId]),
        CONSTRAINT [FK_WorkSpaces_Departments_OwnerDepartmentId] FOREIGN KEY ([OwnerDepartmentId]) REFERENCES [Departments] ([DepartmentId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE TABLE [Sprints] (
        [SprintId] int NOT NULL IDENTITY,
        [WorkSpaceId] int NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Goal] nvarchar(500) NULL,
        [StartDate] datetime2 NULL,
        [EndDate] datetime2 NULL,
        [State] nvarchar(20) NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [CompletedDate] datetime2 NULL,
        [CreatedBy] nvarchar(200) NULL,
        [ModifiedBy] nvarchar(200) NULL,
        [RowVersion] rowversion NULL,
        [TenantId] int NULL,
        CONSTRAINT [PK_Sprints] PRIMARY KEY ([SprintId]),
        CONSTRAINT [FK_Sprints_WorkSpaces_WorkSpaceId] FOREIGN KEY ([WorkSpaceId]) REFERENCES [WorkSpaces] ([WorkSpaceId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE TABLE [WorkItemStatuses] (
        [WorkItemStatusId] int NOT NULL IDENTITY,
        [WorkSpaceId] int NOT NULL,
        [Name] nvarchar(60) NOT NULL,
        [NameFa] nvarchar(60) NULL,
        [Category] nvarchar(20) NOT NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [ModifiedBy] nvarchar(200) NULL,
        [RowVersion] rowversion NULL,
        [TenantId] int NULL,
        CONSTRAINT [PK_WorkItemStatuses] PRIMARY KEY ([WorkItemStatusId]),
        CONSTRAINT [FK_WorkItemStatuses_WorkSpaces_WorkSpaceId] FOREIGN KEY ([WorkSpaceId]) REFERENCES [WorkSpaces] ([WorkSpaceId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE TABLE [WorkItemTypes] (
        [WorkItemTypeId] int NOT NULL IDENTITY,
        [WorkSpaceId] int NOT NULL,
        [Name] nvarchar(60) NOT NULL,
        [NameFa] nvarchar(60) NULL,
        [Icon] nvarchar(60) NOT NULL,
        [IsSubtask] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [ModifiedBy] nvarchar(200) NULL,
        [RowVersion] rowversion NULL,
        [TenantId] int NULL,
        CONSTRAINT [PK_WorkItemTypes] PRIMARY KEY ([WorkItemTypeId]),
        CONSTRAINT [FK_WorkItemTypes_WorkSpaces_WorkSpaceId] FOREIGN KEY ([WorkSpaceId]) REFERENCES [WorkSpaces] ([WorkSpaceId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE TABLE [WorkSpaceMembers] (
        [WorkSpaceMemberId] int NOT NULL IDENTITY,
        [WorkSpaceId] int NOT NULL,
        [EmployeeId] int NOT NULL,
        [Role] nvarchar(20) NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [CreatedBy] nvarchar(200) NULL,
        [ModifiedBy] nvarchar(200) NULL,
        [RowVersion] rowversion NULL,
        [TenantId] int NULL,
        CONSTRAINT [PK_WorkSpaceMembers] PRIMARY KEY ([WorkSpaceMemberId]),
        CONSTRAINT [FK_WorkSpaceMembers_Employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WorkSpaceMembers_WorkSpaces_WorkSpaceId] FOREIGN KEY ([WorkSpaceId]) REFERENCES [WorkSpaces] ([WorkSpaceId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE TABLE [WorkItemStatusTransitions] (
        [WorkItemStatusTransitionId] int NOT NULL IDENTITY,
        [WorkSpaceId] int NOT NULL,
        [FromStatusId] int NOT NULL,
        [ToStatusId] int NOT NULL,
        CONSTRAINT [PK_WorkItemStatusTransitions] PRIMARY KEY ([WorkItemStatusTransitionId]),
        CONSTRAINT [FK_WorkItemStatusTransitions_WorkItemStatuses_FromStatusId] FOREIGN KEY ([FromStatusId]) REFERENCES [WorkItemStatuses] ([WorkItemStatusId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WorkItemStatusTransitions_WorkItemStatuses_ToStatusId] FOREIGN KEY ([ToStatusId]) REFERENCES [WorkItemStatuses] ([WorkItemStatusId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WorkItemStatusTransitions_WorkSpaces_WorkSpaceId] FOREIGN KEY ([WorkSpaceId]) REFERENCES [WorkSpaces] ([WorkSpaceId]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_TaskItems_ItemKey] ON [TaskItems] ([ItemKey]) WHERE [ItemKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE INDEX [IX_TaskItems_ParentTaskItemId] ON [TaskItems] ([ParentTaskItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE INDEX [IX_TaskItems_ReporterEmployeeId] ON [TaskItems] ([ReporterEmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE INDEX [IX_TaskItems_SprintId] ON [TaskItems] ([SprintId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE INDEX [IX_TaskItems_WorkItemStatusId] ON [TaskItems] ([WorkItemStatusId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE INDEX [IX_TaskItems_WorkItemTypeId] ON [TaskItems] ([WorkItemTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE INDEX [IX_TaskItems_WorkSpaceId_IsActive_Rank] ON [TaskItems] ([WorkSpaceId], [IsActive], [Rank]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE INDEX [IX_Sprints_WorkSpaceId_State] ON [Sprints] ([WorkSpaceId], [State]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Sprints_OneActivePerSpace] ON [Sprints] ([WorkSpaceId]) WHERE [State] = N''Active''');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE INDEX [IX_WorkItemComments_TaskItemId_CreatedDate] ON [WorkItemComments] ([TaskItemId], [CreatedDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WorkItemLinks_SourceTaskItemId_LinkType_TargetTaskItemId] ON [WorkItemLinks] ([SourceTaskItemId], [LinkType], [TargetTaskItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE INDEX [IX_WorkItemLinks_TargetTaskItemId] ON [WorkItemLinks] ([TargetTaskItemId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WorkItemStatuses_WorkSpaceId_Name] ON [WorkItemStatuses] ([WorkSpaceId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE INDEX [IX_WorkItemStatusTransitions_FromStatusId] ON [WorkItemStatusTransitions] ([FromStatusId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE INDEX [IX_WorkItemStatusTransitions_ToStatusId] ON [WorkItemStatusTransitions] ([ToStatusId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WorkItemStatusTransitions_WorkSpaceId_FromStatusId_ToStatusId] ON [WorkItemStatusTransitions] ([WorkSpaceId], [FromStatusId], [ToStatusId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WorkItemTypes_WorkSpaceId_Name] ON [WorkItemTypes] ([WorkSpaceId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE INDEX [IX_WorkItemWatchers_EmployeeId] ON [WorkItemWatchers] ([EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WorkItemWatchers_TaskItemId_EmployeeId] ON [WorkItemWatchers] ([TaskItemId], [EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE INDEX [IX_WorkSpaceMembers_EmployeeId] ON [WorkSpaceMembers] ([EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WorkSpaceMembers_WorkSpaceId_EmployeeId] ON [WorkSpaceMembers] ([WorkSpaceId], [EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WorkSpaces_Key] ON [WorkSpaces] ([Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    CREATE INDEX [IX_WorkSpaces_OwnerDepartmentId] ON [WorkSpaces] ([OwnerDepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD CONSTRAINT [FK_TaskItems_Employees_ReporterEmployeeId] FOREIGN KEY ([ReporterEmployeeId]) REFERENCES [Employees] ([EmployeeId]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD CONSTRAINT [FK_TaskItems_Sprints_SprintId] FOREIGN KEY ([SprintId]) REFERENCES [Sprints] ([SprintId]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD CONSTRAINT [FK_TaskItems_TaskItems_ParentTaskItemId] FOREIGN KEY ([ParentTaskItemId]) REFERENCES [TaskItems] ([TaskItemId]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD CONSTRAINT [FK_TaskItems_WorkItemStatuses_WorkItemStatusId] FOREIGN KEY ([WorkItemStatusId]) REFERENCES [WorkItemStatuses] ([WorkItemStatusId]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD CONSTRAINT [FK_TaskItems_WorkItemTypes_WorkItemTypeId] FOREIGN KEY ([WorkItemTypeId]) REFERENCES [WorkItemTypes] ([WorkItemTypeId]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    ALTER TABLE [TaskItems] ADD CONSTRAINT [FK_TaskItems_WorkSpaces_WorkSpaceId] FOREIGN KEY ([WorkSpaceId]) REFERENCES [WorkSpaces] ([WorkSpaceId]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015125354_TaskManagerCore'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261015125354_TaskManagerCore', N'10.0.10');
END;

COMMIT;
GO

