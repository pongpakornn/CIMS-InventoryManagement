-- Update_20261015.sql
-- Multi-Scanner: Admin sets the scan stocks of each UserID (User Management > PERMISSION > Multi-Scanner row)
--   * CIMS.UserScanStocks: stocks each user may scan (1 row = 1 stock) + IsPriority = ★ stock used first for SCAN OUT
--     A user with no rows cannot scan until an Admin sets the stocks.
--     First run only: each user gets the stocks they scanned in the last 90 days (users with Multi-Scanner VIEW or Level 1),
--     so current users keep working. Running the script again never changes rows an Admin has set.
--   * CIMS.AdminContacts: who to contact for help (name, phone, nickname) - shown on the scan page when a user has no
--     scan stock. First run adds the default contact.
-- DISPLAY per user stays in CIMS.UserScanDisplays (Update_20261013.sql) - now set by the Admin.
-- Creates two tables only - nothing else changes. Safe to run again.

IF OBJECT_ID('CIMS.UserScanStocks', 'U') IS NULL
BEGIN
    CREATE TABLE CIMS.UserScanStocks (
        UserID      VARCHAR(20) NOT NULL,
        StockID     INT         NOT NULL,
        IsPriority  BIT         NOT NULL CONSTRAINT DF_UserScanStocks_IsPriority DEFAULT 0,
        UpdatedBy   VARCHAR(20) NULL,
        UpdatedDate DATETIME    NOT NULL CONSTRAINT DF_UserScanStocks_UpdatedDate DEFAULT GETDATE(),
        CONSTRAINT PK_UserScanStocks PRIMARY KEY (UserID, StockID)
    );

    EXEC (N'
        INSERT INTO CIMS.UserScanStocks (UserID, StockID, IsPriority, UpdatedBy)
        SELECT DISTINCT t.UserID, t.StockID, 0, ''SYSTEM''
        FROM CIMS.ScanTransactions t
        JOIN CIMS.Users u  ON u.UserID = t.UserID
        JOIN CIMS.Stocks s ON s.StockID = t.StockID
        WHERE ISNULL(t.IsCancelled, 0) = 0
          AND t.TransactionDate >= DATEADD(DAY, -90, GETDATE())
          AND (u.UserLevel = 1 OR EXISTS (SELECT 1 FROM CIMS.Permissions p
                                          WHERE p.UserID = t.UserID AND p.SystemID = ''MultiScanner'' AND p.CanView = ''Y''));');
END
GO

IF OBJECT_ID('CIMS.AdminContacts', 'U') IS NULL
BEGIN
    CREATE TABLE CIMS.AdminContacts (
        ContactID   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdminContacts PRIMARY KEY,
        FullName    NVARCHAR(100) NOT NULL,
        Phone       VARCHAR(30)   NOT NULL,
        Nickname    NVARCHAR(50)  NULL,
        SortNo      INT           NOT NULL CONSTRAINT DF_AdminContacts_SortNo DEFAULT 0,
        UpdatedBy   VARCHAR(20)   NULL,
        UpdatedDate DATETIME      NOT NULL CONSTRAINT DF_AdminContacts_UpdatedDate DEFAULT GETDATE()
    );
    EXEC (N'INSERT INTO CIMS.AdminContacts (FullName, Phone, Nickname, SortNo, UpdatedBy)
            VALUES (N''พงศ์ปกรณ์ อุรัง'', ''0982290569'', N''นนท์'', 1, ''SYSTEM'');');
END
GO
