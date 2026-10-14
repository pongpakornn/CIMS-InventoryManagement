-- Pick List: นับการพิมพ์ซ้ำ + คนพิมพ์ล่าสุด (ตารางหน้า Pick List แสดงรายการวันนี้ครบเสมอ พิมพ์แล้วไม่หาย)
--   PrintedBy / PrintedDate = พิมพ์ครั้งแรก (เดิม) · LastPrintedBy / LastPrintedDate = พิมพ์ล่าสุด · PrintCount = จำนวนครั้งที่พิมพ์
-- เพิ่มคอลัมน์อย่างเดียว ไม่ลบ / ไม่แก้ข้อมูลเดิม · รันซ้ำได้
IF COL_LENGTH('CIMS.PickListPrintLogs', 'PrintCount') IS NULL
    ALTER TABLE CIMS.PickListPrintLogs ADD PrintCount INT NOT NULL CONSTRAINT DF_PickListPrintLogs_PrintCount DEFAULT 1;
GO
IF COL_LENGTH('CIMS.PickListPrintLogs', 'LastPrintedBy') IS NULL
    ALTER TABLE CIMS.PickListPrintLogs ADD LastPrintedBy VARCHAR(50) NULL;
GO
IF COL_LENGTH('CIMS.PickListPrintLogs', 'LastPrintedDate') IS NULL
    ALTER TABLE CIMS.PickListPrintLogs ADD LastPrintedDate DATETIME NULL;
GO
