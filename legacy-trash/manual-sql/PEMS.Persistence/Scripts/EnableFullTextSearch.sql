-- ==============================================================================
-- PEMS Global Search Framework - فاز C: فعال‌سازی SQL Server Full-Text Search
--
-- چرا این اسکریپت جدا از EF Migration است (نه dotnet ef migrations add):
-- Full-Text Catalog/Index یک ویژگی سطح Instance/Database است، نه بخشی از مدل Entity که
-- EF Core مدیریت می‌کند. بسیاری از تیم‌ها عمداً این نوع DDL را جدا از ORM Migration نگه
-- می‌دارند تا تاریخچه‌ی Migration با جزئیات زیرساخت دیتابیس قاطی نشود. این اسکریپت کاملاً
-- Idempotent است - هر چند بار اجرا شود، مشکلی پیش نمی‌آید.
--
-- پیش‌نیاز: باید Full-Text Search هنگام نصب SQL Server انتخاب شده باشد. اگر نصب نیست،
-- این اسکریپت به‌صورت امن هیچ کاری نمی‌کند (بخش IF ابتدایی) و برنامه هم‌چنان با روش
-- LIKE/Fallback فعلی کار می‌کند - هیچ‌چیزی خراب نمی‌شود.
--
-- نحوه‌ی اجرا: خودکار - DatabaseInitializer این اسکریپت (Embedded Resource) را بعد از Migration ها
-- روی دیتابیس جاری اجرا می‌کند؛ هیچ مرحله‌ی دستی (SSMS) لازم نیست.
-- ==============================================================================

IF (SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')) = 1
BEGIN
    PRINT N'Full-Text Search نصب است - در حال بررسی/ساخت Catalog و Index ها...';

    -- ==============================================================================
    -- Catalog مشترک برای همه‌ی جداول قابل‌جستجو
    -- ==============================================================================
    IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'PEMSFullTextCatalog')
    BEGIN
        CREATE FULLTEXT CATALOG PEMSFullTextCatalog AS DEFAULT;
        PRINT N'Catalog ساخته شد.';
    END

    -- ==============================================================================
    -- Equipment - طبق فاز B: TagNumber, Name, Description, Manufacturer
    -- ==============================================================================
    IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Equipments')
       AND NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.Equipments'))
       AND EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_Equipments')
    BEGIN
        CREATE FULLTEXT INDEX ON dbo.Equipments(TagNumber, Name, Description, Manufacturer)
            KEY INDEX PK_Equipments ON PEMSFullTextCatalog
            WITH CHANGE_TRACKING AUTO;
        PRINT N'Full-Text Index روی Equipments ساخته شد.';
    END

    -- ==============================================================================
    -- EngineeringDocuments - طبق فاز B: DocumentNo, Title, Description
    -- ==============================================================================
    IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'EngineeringDocuments')
       AND NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.EngineeringDocuments'))
       AND EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_EngineeringDocuments')
    BEGIN
        CREATE FULLTEXT INDEX ON dbo.EngineeringDocuments(DocumentNo, Title, Description)
            KEY INDEX PK_EngineeringDocuments ON PEMSFullTextCatalog
            WITH CHANGE_TRACKING AUTO;
        PRINT N'Full-Text Index روی EngineeringDocuments ساخته شد.';
    END

    -- ==============================================================================
    -- VDRItems - طبق فاز B: DocumentNumber, DocumentDescription
    -- ==============================================================================
    IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'VDRItems')
       AND NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.VDRItems'))
       AND EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_VDRItems')
    BEGIN
        CREATE FULLTEXT INDEX ON dbo.VDRItems(DocumentNumber, DocumentDescription)
            KEY INDEX PK_VDRItems ON PEMSFullTextCatalog
            WITH CHANGE_TRACKING AUTO;
        PRINT N'Full-Text Index روی VDRItems ساخته شد.';
    END

    -- ==============================================================================
    -- Vendors - برای جستجوی نام وندور در بخش VDR (طبق مثال کاربر)
    -- ==============================================================================
    IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Vendors')
       AND NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.Vendors'))
       AND EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_Vendors')
    BEGIN
        CREATE FULLTEXT INDEX ON dbo.Vendors(Name)
            KEY INDEX PK_Vendors ON PEMSFullTextCatalog
            WITH CHANGE_TRACKING AUTO;
        PRINT N'Full-Text Index روی Vendors ساخته شد.';
    END

    PRINT N'انجام شد. Change Tracking = AUTO یعنی Index ها خودکار با تغییر داده به‌روز می‌شوند.';
END
ELSE
BEGIN
    PRINT N'هشدار: Full-Text Search روی این SQL Server نصب نیست. برنامه هم‌چنان با روش LIKE/Fuzzy فعلی کار می‌کند - فقط از کیفیت جستجوی پیشرفته‌تر بهره نمی‌برید. برای نصب، از SQL Server Installation Center گزینه‌ی "Full-Text and Semantic Extractions for Search" را اضافه کنید.';
END
GO
