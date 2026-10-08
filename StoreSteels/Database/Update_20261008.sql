-- =====================================================================================================
-- CIMS update 2026-10-08: COIL register (coil-by-coil detail of KG stocks)
--   * CIMS.Coils: one row per child coil (e.g. CWE0885B-006) - mother coil (CWE0885B), product, stock where it is,
--     weight (KG, from the scanned label), status IN (in that stock) / OUT (issued as a whole coil)
--   * CIMS.CoilMoves: every coil change, linked to the scan (ScanTransactionID) so ADJUST SCAN can undo it
--   * CIMS.BarcodeFormats.CoilNoPosition / MotherCoilPosition: label fields that hold the child / mother coil number
--   Products, product imports and templates are not changed. Safe to run more than once.
-- =====================================================================================================
SET XACT_ABORT ON;
GO
IF COL_LENGTH('CIMS.BarcodeFormats', 'CoilNoPosition') IS NULL
    ALTER TABLE CIMS.BarcodeFormats ADD CoilNoPosition INT NULL;
GO
IF COL_LENGTH('CIMS.BarcodeFormats', 'MotherCoilPosition') IS NULL
    ALTER TABLE CIMS.BarcodeFormats ADD MotherCoilPosition INT NULL;
GO

IF OBJECT_ID('CIMS.Coils', 'U') IS NULL
BEGIN
    CREATE TABLE CIMS.Coils (
        CoilID       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Coils PRIMARY KEY,
        CoilNo       NVARCHAR(60)  NOT NULL,                 -- child coil (label), unique
        MotherCoil   NVARCHAR(60)  NULL,                     -- mother coil the child was slit from
        PartID       INT           NOT NULL,                 -- product in the stock where the coil is now
        StockID      INT           NOT NULL,
        WeightKG     DECIMAL(18,3) NOT NULL CONSTRAINT DF_Coils_Weight DEFAULT (0),
        Status       VARCHAR(10)   NOT NULL CONSTRAINT DF_Coils_Status DEFAULT ('IN'),   -- IN / OUT
        LabelDate    NVARCHAR(30)  NULL,
        ReceivedDate DATETIME      NOT NULL CONSTRAINT DF_Coils_Received DEFAULT (GETDATE()),
        ReceivedBy   VARCHAR(20)   NULL,
        OutDate      DATETIME      NULL,
        UpdatedDate  DATETIME      NOT NULL CONSTRAINT DF_Coils_Updated DEFAULT (GETDATE()),
        CONSTRAINT UQ_Coils_CoilNo UNIQUE (CoilNo),
        CONSTRAINT FK_Coils_Part  FOREIGN KEY (PartID)  REFERENCES CIMS.Parts (PartID),
        CONSTRAINT FK_Coils_Stock FOREIGN KEY (StockID) REFERENCES CIMS.Stocks (StockID),
        CONSTRAINT CK_Coils_Status CHECK (Status IN ('IN', 'OUT'))
    );
    CREATE INDEX IX_Coils_Stock_Part ON CIMS.Coils (StockID, PartID, Status) INCLUDE (CoilNo, MotherCoil, WeightKG);
    CREATE INDEX IX_Coils_Mother ON CIMS.Coils (MotherCoil, StockID, Status);
END
GO

IF OBJECT_ID('CIMS.CoilMoves', 'U') IS NULL
BEGIN
    CREATE TABLE CIMS.CoilMoves (
        MoveID            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CoilMoves PRIMARY KEY,
        CoilID            INT           NOT NULL,
        Action            VARCHAR(20)   NOT NULL,     -- CREATE (new coil) / MOVE (from another stock) / OUT / IMPORT
        ScanTransactionID INT           NULL,
        FromStockID       INT           NULL,
        FromPartID        INT           NULL,
        FromStatus        VARCHAR(10)   NULL,
        FromWeightKG      DECIMAL(18,3) NULL,
        ToStockID         INT           NULL,
        ToPartID          INT           NULL,
        ToStatus          VARCHAR(10)   NULL,
        WeightKG          DECIMAL(18,3) NULL,
        UserID            VARCHAR(20)   NULL,
        MoveDate          DATETIME      NOT NULL CONSTRAINT DF_CoilMoves_Date DEFAULT (GETDATE()),
        Undone            BIT           NOT NULL CONSTRAINT DF_CoilMoves_Undone DEFAULT (0),
        CONSTRAINT FK_CoilMoves_Coil FOREIGN KEY (CoilID) REFERENCES CIMS.Coils (CoilID)
    );
    CREATE INDEX IX_CoilMoves_Tx ON CIMS.CoilMoves (ScanTransactionID);
END
GO
