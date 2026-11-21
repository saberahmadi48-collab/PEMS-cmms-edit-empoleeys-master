BEGIN TRANSACTION;
CREATE TABLE [NetDemandRuns] (
    [NetDemandRunId] int NOT NULL IDENTITY,
    [RequestKey] nvarchar(64) NULL,
    [RequestedByEmployeeId] int NULL,
    [StartedAt] datetime2 NOT NULL,
    [CompletedAt] datetime2 NULL,
    [Status] nvarchar(20) NOT NULL,
    [FailureMessage] nvarchar(500) NULL,
    [CalculatorVersion] int NOT NULL,
    [ScopeMaterialItemIds] nvarchar(2000) NULL,
    [ScopeWarehouseIds] nvarchar(2000) NULL,
    [ResultsEvaluated] int NOT NULL,
    [Created] int NOT NULL,
    [Updated] int NOT NULL,
    [Unchanged] int NOT NULL,
    [Closed] int NOT NULL,
    [AutoReopened] int NOT NULL,
    [NotDefinitive] int NOT NULL,
    [NotComparableNoWarehouse] int NOT NULL,
    [NotDefinitiveSummary] nvarchar(4000) NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_NetDemandRuns] PRIMARY KEY ([NetDemandRunId])
);

CREATE TABLE [ReplenishmentRecommendations] (
    [ReplenishmentRecommendationId] int NOT NULL IDENTITY,
    [MaterialItemId] int NOT NULL,
    [WarehouseId] int NOT NULL,
    [Status] nvarchar(20) NOT NULL,
    [Revision] int NOT NULL,
    [UomId] int NULL,
    [OnHandQuantity] decimal(18,3) NOT NULL,
    [ReservedQuantity] decimal(18,3) NOT NULL,
    [UsableQuantity] decimal(18,3) NOT NULL,
    [GrossEligibleDemand] decimal(18,3) NOT NULL,
    [CoveredByReservations] decimal(18,3) NOT NULL,
    [UnreservedDemand] decimal(18,3) NOT NULL,
    [ShortageQuantity] decimal(18,3) NOT NULL,
    [RecommendedQuantity] decimal(18,3) NOT NULL,
    [NoticeCodes] nvarchar(500) NULL,
    [IsCurrentlyDefinitive] bit NOT NULL,
    [BlockingIssueCodes] nvarchar(500) NULL,
    [InputFingerprint] nvarchar(64) NOT NULL,
    [StockLastUpdatedAt] datetime2 NULL,
    [DemandLastChangedAt] datetime2 NULL,
    [FirstRunId] int NOT NULL,
    [LastRunId] int NOT NULL,
    [FirstDetectedAt] datetime2 NOT NULL,
    [LastEvaluatedAt] datetime2 NOT NULL,
    [DismissedAt] datetime2 NULL,
    [DismissedByEmployeeId] int NULL,
    [DismissedReason] nvarchar(500) NULL,
    [DismissedShortageQuantity] decimal(18,3) NULL,
    [DismissedSourceKeys] nvarchar(4000) NULL,
    [ClosedAt] datetime2 NULL,
    [ClosedReason] nvarchar(100) NULL,
    [CreatedBy] nvarchar(max) NULL,
    [ModifiedBy] nvarchar(max) NULL,
    [RowVersion] rowversion NULL,
    [TenantId] int NULL,
    CONSTRAINT [PK_ReplenishmentRecommendations] PRIMARY KEY ([ReplenishmentRecommendationId]),
    CONSTRAINT [FK_ReplenishmentRecommendations_MaterialItems_MaterialItemId] FOREIGN KEY ([MaterialItemId]) REFERENCES [MaterialItems] ([MaterialItemId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReplenishmentRecommendations_NetDemandRuns_FirstRunId] FOREIGN KEY ([FirstRunId]) REFERENCES [NetDemandRuns] ([NetDemandRunId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReplenishmentRecommendations_NetDemandRuns_LastRunId] FOREIGN KEY ([LastRunId]) REFERENCES [NetDemandRuns] ([NetDemandRunId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ReplenishmentRecommendations_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([WarehouseId]) ON DELETE NO ACTION
);

CREATE TABLE [ReplenishmentRecommendationHistories] (
    [ReplenishmentRecommendationHistoryId] int NOT NULL IDENTITY,
    [ReplenishmentRecommendationId] int NOT NULL,
    [Revision] int NOT NULL,
    [Action] nvarchar(30) NOT NULL,
    [FromStatus] nvarchar(20) NULL,
    [ToStatus] nvarchar(20) NOT NULL,
    [ShortageQuantity] decimal(18,3) NOT NULL,
    [UsableQuantity] decimal(18,3) NOT NULL,
    [UnreservedDemand] decimal(18,3) NOT NULL,
    [InputFingerprint] nvarchar(64) NULL,
    [RunId] int NULL,
    [ActorEmployeeId] int NULL,
    [Note] nvarchar(500) NULL,
    [OccurredAt] datetime2 NOT NULL,
    CONSTRAINT [PK_ReplenishmentRecommendationHistories] PRIMARY KEY ([ReplenishmentRecommendationHistoryId]),
    CONSTRAINT [FK_ReplenishmentRecommendationHistories_ReplenishmentRecommendations_ReplenishmentRecommendationId] FOREIGN KEY ([ReplenishmentRecommendationId]) REFERENCES [ReplenishmentRecommendations] ([ReplenishmentRecommendationId]) ON DELETE NO ACTION
);

CREATE TABLE [ReplenishmentRecommendationSources] (
    [ReplenishmentRecommendationSourceId] int NOT NULL IDENTITY,
    [ReplenishmentRecommendationId] int NOT NULL,
    [Revision] int NOT NULL,
    [SourceType] int NOT NULL,
    [StoreRequisitionLineId] int NULL,
    [WorkOrderMaterialId] int NULL,
    [GrossRemainingQuantity] decimal(18,3) NOT NULL,
    [CoveredByReservation] decimal(18,3) NOT NULL,
    [UnreservedQuantity] decimal(18,3) NOT NULL,
    CONSTRAINT [PK_ReplenishmentRecommendationSources] PRIMARY KEY ([ReplenishmentRecommendationSourceId]),
    CONSTRAINT [CK_ReplenishmentRecommendationSources_OneSource] CHECK (([SourceType] = 1 AND [StoreRequisitionLineId] IS NOT NULL AND [WorkOrderMaterialId] IS NULL) OR ([SourceType] = 2 AND [WorkOrderMaterialId] IS NOT NULL AND [StoreRequisitionLineId] IS NULL)),
    CONSTRAINT [FK_ReplenishmentRecommendationSources_ReplenishmentRecommendations_ReplenishmentRecommendationId] FOREIGN KEY ([ReplenishmentRecommendationId]) REFERENCES [ReplenishmentRecommendations] ([ReplenishmentRecommendationId]) ON DELETE NO ACTION
);

CREATE UNIQUE INDEX [UX_NetDemandRuns_RequestKey] ON [NetDemandRuns] ([RequestKey]) WHERE [RequestKey] IS NOT NULL;

CREATE INDEX [IX_ReplenishmentRecommendationHistories_ReplenishmentRecommendationId_Revision_ReplenishmentRecommendationHistoryId] ON [ReplenishmentRecommendationHistories] ([ReplenishmentRecommendationId], [Revision], [ReplenishmentRecommendationHistoryId]);

CREATE INDEX [IX_ReplenishmentRecommendations_FirstRunId] ON [ReplenishmentRecommendations] ([FirstRunId]);

CREATE INDEX [IX_ReplenishmentRecommendations_LastRunId] ON [ReplenishmentRecommendations] ([LastRunId]);

CREATE INDEX [IX_ReplenishmentRecommendations_Status] ON [ReplenishmentRecommendations] ([Status]);

CREATE INDEX [IX_ReplenishmentRecommendations_WarehouseId] ON [ReplenishmentRecommendations] ([WarehouseId]);

CREATE UNIQUE INDEX [UX_ReplenishmentRecommendations_Live] ON [ReplenishmentRecommendations] ([MaterialItemId], [WarehouseId]) WHERE [Status] IN (N'Open', N'Dismissed');

CREATE INDEX [IX_ReplenishmentRecommendationSources_ReplenishmentRecommendationId_Revision] ON [ReplenishmentRecommendationSources] ([ReplenishmentRecommendationId], [Revision]);

CREATE INDEX [IX_ReplenishmentRecommendationSources_StoreRequisitionLineId] ON [ReplenishmentRecommendationSources] ([StoreRequisitionLineId]) WHERE [StoreRequisitionLineId] IS NOT NULL;

CREATE INDEX [IX_ReplenishmentRecommendationSources_WorkOrderMaterialId] ON [ReplenishmentRecommendationSources] ([WorkOrderMaterialId]) WHERE [WorkOrderMaterialId] IS NOT NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261011082322_ReplenishmentRecommendations', N'10.0.10');

COMMIT;
GO

