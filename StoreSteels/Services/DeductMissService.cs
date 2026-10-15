using CIMS.Core;
using CIMS.Helpers;
using CIMS.Models;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CIMS.Services
{
    // 📋 NOT DEDUCTED: ป้ายของรูปแบบที่ "ตัดยอดจากคลัง" (เช่น Panta -> STOCK-PANTA) ที่ตัดคลังต้นทางไม่ได้ / สินค้ายังไม่ลงทะเบียน
    //    PENDING  = ยังไม่รับเข้าคลัง รอผู้มีสิทธิ์ APPROVE ของ Multi-Scanner (Update_20261016b.sql)
    //    RECEIVED = รายการเดิม (ก่อนอัพเดทนี้) รับเข้าคลังไปแล้ว ยังไม่ได้ตัดคลังต้นทาง
    //    APPROVED / REGISTERED = ดำเนินการแล้ว -> แสดงที่ DEDUCTED
    // ✔ DEDUCTED: ป้ายที่รับเข้าแล้ว + ตัดคลังต้นทาง (CIMS.StockTransfers SCAN) / APPROVE แล้วแต่ยังไม่มี Coil แม่ให้ตัด
    public class DeductMissRow : INotifyPropertyChanged
    {
        public int MissId { get; set; }
        public int? TxId { get; set; }
        public DateTime ScanDate { get; set; }
        public string CoilNo { get; set; }
        public string MotherCoil { get; set; }
        public int? PartId { get; set; }
        public string PartCode { get; set; }
        public string PartName { get; set; }
        public int StockId { get; set; }
        public int SourceStockId { get; set; }
        public string StockCode { get; set; }
        public string SourceCode { get; set; }
        public decimal LabelQty { get; set; }
        public decimal Deducted { get; set; }
        public string Reason { get; set; }
        public string Status { get; set; }
        public int? FormatId { get; set; }
        public string UserId { get; set; }
        public string Barcode { get; set; }
        public decimal? MotherNow { get; set; }   // Coil แม่ในคลังต้นทางตอนนี้ (IN) - null = ยังไม่มี

        // รายการ DEDUCTED (ตัดคลังต้นทางได้แล้ว)
        public string SourcePartCode { get; set; }
        public string SourcePartName { get; set; }
        public decimal? SourceAfter { get; set; }
        public string How { get; set; }             // MOTHER COIL / COIL / PRODUCT / NONE (APPROVE แล้วยังไม่มีให้ตัด)
        public decimal? MotherBefore { get; set; }  // น้ำหนัก Coil แม่ก่อนตัดครั้งนั้น
        public decimal? MotherAfter { get; set; }   // น้ำหนัก Coil แม่หลังตัดครั้งนั้น

        public bool HasProduct => PartId.HasValue && PartId.Value > 0;
        public bool IsPending => Status == "PENDING";
        public string PartCodeText => HasProduct ? PartCode : "— NO PD CODE —";
        public string PartNameText => HasProduct ? PartName : "ยังไม่มีสินค้าในระบบ";
        public string DateText => ScanDate.ToString("dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        public string LabelQtyText => Qty.Plain(LabelQty);
        public string DeductedText => Qty.Plain(Deducted);
        public string SourceAfterText => SourceAfter.HasValue ? Qty.Plain(SourceAfter.Value) : "-";
        public string MotherBeforeText => MotherBefore.HasValue ? $"{Qty.Plain(MotherBefore.Value)} KG" : "-";
        public string MotherNowText => MotherNow.HasValue ? $"IN {Qty.Plain(MotherNow.Value)} KG" : "NOT IN " + SourceCode;
        public string StockText => $"{StockCode} ← {SourceCode}";

        // สถานะ (NOT DEDUCTED): รอดำเนินการ + สาเหตุ
        public string StatusText => Status == "RECEIVED"
            ? $"รับเข้า {StockCode} แล้ว (รายการเดิม) • ยังไม่ตัด {SourceCode}"
            : $"⏸ รอดำเนินการ • ยังไม่รับเข้า {StockCode}";
        public string ReasonText =>
            Reason == "NO_PRODUCT" ? "ไม่มี PD CODE นี้ในระบบ" :
            Reason == "SHORT" ? $"Coil แม่ {MotherCoil} ใน {SourceCode} เหลือไม่พอ (ตัดได้ {Qty.Plain(Deducted)} KG)" :
            $"ไม่มี Coil แม่ {MotherCoil} ใน {SourceCode}";

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class DeductMissService
    {
        private readonly string _cs = GlobalConfig.ConnStr;

        // รายการที่ยังค้างอยู่ใน NOT DEDUCTED (สแกนที่ถูกยกเลิกแล้วไม่แสดง)
        private static string Visible => DbSchema.HasMissStatus
            ? "ISNULL(t.IsCancelled, 0) = 0 AND m.Status IN ('PENDING', 'RECEIVED')"
            : "ISNULL(t.IsCancelled, 0) = 0";

        // เฉพาะคลังที่ผู้ใช้เห็นปุ่ม DEDUCTED / NOT DEDUCTED (null = ทุกคลัง) - รายการที่คลังรับเข้าหรือคลังต้นทางอยู่ในชุดนี้
        private static string StockFilter(IEnumerable<int> stockIds, string toCol, string fromCol)
        {
            if (stockIds == null) return "";
            var ids = stockIds.Distinct().ToList();
            if (ids.Count == 0) return " AND 1 = 0";
            string list = string.Join(",", ids.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return $" AND ({toCol} IN ({list}) OR {fromCol} IN ({list}))";
        }

        // จำนวนรายการทั้งหมด (ปุ่ม NOT DEDUCTED (n) หน้า Multi-Scanner)
        public int Count(IEnumerable<int> stockIds = null)
        {
            if (!DbSchema.HasDeductMisses) return 0;
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM CIMS.DeductMisses m LEFT JOIN CIMS.ScanTransactions t ON t.TransactionID = m.TransactionID WHERE {Visible}" + StockFilter(stockIds, "m.StockID", "m.SourceStockID"), conn))
            {
                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public List<DeductMissRow> GetRows(string keyword, DateTime from, DateTime to, IEnumerable<int> stockIds = null)
        {
            var list = new List<DeductMissRow>();
            bool st = DbSchema.HasMissStatus;
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand($@"
                SELECT m.MissID, m.TransactionID, m.ScanDate, m.CoilNo, m.MotherCoil, m.LabelQty, m.Deducted, m.Reason, m.UserID, m.Barcode,
                       m.PartID, m.StockID, m.SourceStockID, {(st ? "m.Status, m.FormatID" : "'RECEIVED' AS Status, CAST(NULL AS int) AS FormatID")},
                       p.PartCode, p.Description, s.StockCode, src.StockCode AS SourceCode,
                       MotherNow = (SELECT TOP 1 c.WeightKG FROM CIMS.Coils c WHERE c.CoilNo = m.MotherCoil AND c.StockID = m.SourceStockID AND c.Status = 'IN')
                FROM CIMS.DeductMisses m
                LEFT JOIN CIMS.ScanTransactions t ON t.TransactionID = m.TransactionID
                LEFT JOIN CIMS.Parts p ON p.PartID = m.PartID
                LEFT JOIN CIMS.Stocks s ON s.StockID = m.StockID
                LEFT JOIN CIMS.Stocks src ON src.StockID = m.SourceStockID
                WHERE {Visible} AND m.ScanDate >= @from AND m.ScanDate < @to
                  AND (@k = '' OR m.CoilNo LIKE '%' + @k + '%' OR m.MotherCoil LIKE '%' + @k + '%' OR p.PartCode LIKE '%' + @k + '%'
                       OR p.Description LIKE '%' + @k + '%' OR m.UserID LIKE '%' + @k + '%' OR m.Barcode LIKE '%' + @k + '%')
                  {StockFilter(stockIds, "m.StockID", "m.SourceStockID")}
                ORDER BY m.ScanDate DESC, m.MissID DESC", conn))
            {
                cmd.Parameters.Add("@from", SqlDbType.DateTime).Value = from.Date;
                cmd.Parameters.Add("@to", SqlDbType.DateTime).Value = to.Date.AddDays(1);
                cmd.Parameters.Add("@k", SqlDbType.NVarChar, 200).Value = (keyword ?? "").Trim();
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new DeductMissRow
                        {
                            MissId = Convert.ToInt32(r["MissID"]),
                            TxId = r["TransactionID"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["TransactionID"]),
                            ScanDate = Convert.ToDateTime(r["ScanDate"]),
                            CoilNo = r["CoilNo"]?.ToString() ?? "",
                            MotherCoil = r["MotherCoil"]?.ToString() ?? "",
                            LabelQty = Qty.Read(r["LabelQty"]),
                            Deducted = Qty.Read(r["Deducted"]),
                            Reason = r["Reason"].ToString(),
                            Status = r["Status"].ToString(),
                            FormatId = r["FormatID"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["FormatID"]),
                            UserId = r["UserID"]?.ToString() ?? "",
                            Barcode = r["Barcode"]?.ToString() ?? "",
                            PartId = r["PartID"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["PartID"]),
                            StockId = Convert.ToInt32(r["StockID"]),
                            SourceStockId = Convert.ToInt32(r["SourceStockID"]),
                            PartCode = r["PartCode"]?.ToString() ?? "",
                            PartName = r["Description"]?.ToString() ?? "",
                            StockCode = r["StockCode"]?.ToString() ?? "",
                            SourceCode = r["SourceCode"]?.ToString() ?? "",
                            MotherNow = r["MotherNow"] == DBNull.Value ? (decimal?)null : Qty.Read(r["MotherNow"])
                        });
            }
            return list;
        }

        // ✔ DEDUCTED: จำนวนรายการ "วันนี้" (ปุ่ม DEDUCTED (n))
        public int CountDeductedToday(IEnumerable<int> stockIds = null)
        {
            if (!DbSchema.HasTransferScanTx) return 0;
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"SELECT (SELECT COUNT(*) FROM CIMS.StockTransfers st JOIN CIMS.ScanTransactions t ON t.TransactionID = st.ScanTransactionID
                                              WHERE st.TransferMode = 'SCAN' AND ISNULL(t.IsCancelled, 0) = 0 AND st.TransferDate >= CAST(GETDATE() AS date)"
                                              + StockFilter(stockIds, "st.ToStockID", "st.FromStockID") + ")"
                                              + (DbSchema.HasMissStatus ? @" + (SELECT COUNT(*) FROM CIMS.DeductMisses m JOIN CIMS.ScanTransactions t ON t.TransactionID = m.TransactionID
                                              WHERE m.Status IN ('APPROVED', 'REGISTERED') AND ISNULL(t.IsCancelled, 0) = 0 AND m.ActionDate >= CAST(GETDATE() AS date)
                                                AND NOT EXISTS (SELECT 1 FROM CIMS.StockTransfers x WHERE x.ScanTransactionID = m.TransactionID)"
                                              + StockFilter(stockIds, "m.StockID", "m.SourceStockID") + ")" : ""), conn))
            {
                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        // ✔ DEDUCTED: เพิ่มอะไรเข้ามา (PD CODE / Coil) แล้วตัดอะไรจากคลังต้นทาง (สินค้าต้นทาง / Coil แม่ก่อนตัด / ยอดต้นทางหลังตัด)
        //    + รายการ NOT DEDUCTED ที่ APPROVE / ลงทะเบียนแล้วแต่ยังไม่มี Coil แม่ให้ตัด (CUT = 0) / รายการที่ถูกยกเลิกไม่แสดง
        public List<DeductMissRow> GetDeducted(string keyword, DateTime from, DateTime to, IEnumerable<int> stockIds = null)
        {
            var list = new List<DeductMissRow>();
            if (!DbSchema.HasTransferScanTx) return list;
            string approvedNoCut = DbSchema.HasMissStatus ? $@"
                UNION ALL
                SELECT -m.MissID, t.TransactionID, ISNULL(m.ActionDate, t.TransactionDate), t.Quantity, CAST(0 AS decimal(18,3)), NULL, m.UserID, t.ReferenceNo,
                       s.StockCode, src.StockCode, p.PartCode, p.Description, NULL, NULL,
                       m.CoilNo, m.MotherCoil, NULL, NULL, NULL, NULL, 'NONE'
                FROM CIMS.DeductMisses m
                JOIN CIMS.ScanTransactions t ON t.TransactionID = m.TransactionID
                LEFT JOIN CIMS.Parts p ON p.PartID = m.PartID
                LEFT JOIN CIMS.Stocks s ON s.StockID = m.StockID
                LEFT JOIN CIMS.Stocks src ON src.StockID = m.SourceStockID
                WHERE m.Status IN ('APPROVED', 'REGISTERED') AND ISNULL(t.IsCancelled, 0) = 0
                  AND NOT EXISTS (SELECT 1 FROM CIMS.StockTransfers x WHERE x.ScanTransactionID = m.TransactionID)
                  AND ISNULL(m.ActionDate, t.TransactionDate) >= @from AND ISNULL(m.ActionDate, t.TransactionDate) < @to
                  {StockFilter(stockIds, "m.StockID", "m.SourceStockID")}" : "";
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand($@"
                SELECT * FROM (
                SELECT st.TransferID AS RowID, t.TransactionID, st.TransferDate AS RowDate, t.Quantity AS LabelQty, st.Quantity AS Cut, st.FromBalanceAfter, t.UserID, t.ReferenceNo,
                       st.ToStockCode, st.FromStockCode, p.PartCode, p.Description, sp.PartCode AS SourcePartCode, sp.Description AS SourcePartName,
                       ch.CoilNo, ch.MotherCoil, mo.CoilNo AS MotherNo, mo.FromWeightKG AS MotherBefore, mo.MoveWeight AS MotherAfter, mo.NowWeight AS MotherNow,
                       CASE WHEN mo.CoilNo IS NOT NULL THEN 'MOTHER COIL' WHEN ch.Action = 'MOVE' THEN 'COIL' ELSE 'PRODUCT' END AS How
                FROM CIMS.StockTransfers st
                JOIN CIMS.ScanTransactions t ON t.TransactionID = st.ScanTransactionID
                LEFT JOIN CIMS.Parts p ON p.PartID = st.PartID
                LEFT JOIN CIMS.Parts sp ON sp.PartID = ISNULL(st.SourcePartID, st.PartID)
                OUTER APPLY (SELECT TOP 1 c.CoilNo, c.MotherCoil, m.Action FROM CIMS.CoilMoves m JOIN CIMS.Coils c ON c.CoilID = m.CoilID
                             WHERE m.ScanTransactionID = t.TransactionID AND m.Action IN ('CREATE', 'MOVE') ORDER BY m.MoveID) ch
                OUTER APPLY (SELECT TOP 1 c.CoilNo, m.FromWeightKG, m.WeightKG AS MoveWeight, CASE WHEN c.Status = 'IN' THEN c.WeightKG END AS NowWeight
                             FROM CIMS.CoilMoves m JOIN CIMS.Coils c ON c.CoilID = m.CoilID
                             WHERE m.ScanTransactionID = t.TransactionID AND m.Action = 'CONSUME' ORDER BY m.MoveID) mo
                WHERE st.TransferMode = 'SCAN' AND ISNULL(t.IsCancelled, 0) = 0 AND st.TransferDate >= @from AND st.TransferDate < @to
                  {StockFilter(stockIds, "st.ToStockID", "st.FromStockID")}
                {approvedNoCut}
                ) x
                WHERE (@k = '' OR x.CoilNo LIKE '%' + @k + '%' OR x.MotherCoil LIKE '%' + @k + '%' OR x.MotherNo LIKE '%' + @k + '%' OR x.PartCode LIKE '%' + @k + '%'
                       OR x.SourcePartCode LIKE '%' + @k + '%' OR x.Description LIKE '%' + @k + '%' OR x.SourcePartName LIKE '%' + @k + '%'
                       OR x.UserID LIKE '%' + @k + '%' OR x.ReferenceNo LIKE '%' + @k + '%')
                ORDER BY x.RowDate DESC, x.RowID DESC", conn))
            {
                cmd.Parameters.Add("@from", SqlDbType.DateTime).Value = from.Date;
                cmd.Parameters.Add("@to", SqlDbType.DateTime).Value = to.Date.AddDays(1);
                cmd.Parameters.Add("@k", SqlDbType.NVarChar, 200).Value = (keyword ?? "").Trim();
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        string mother = r["MotherNo"] != DBNull.Value ? r["MotherNo"].ToString() : r["MotherCoil"]?.ToString() ?? "";
                        list.Add(new DeductMissRow
                        {
                            MissId = Convert.ToInt32(r["RowID"]),
                            TxId = Convert.ToInt32(r["TransactionID"]),
                            ScanDate = Convert.ToDateTime(r["RowDate"]),
                            CoilNo = r["CoilNo"]?.ToString() ?? "",
                            MotherCoil = mother,
                            LabelQty = Qty.Read(r["LabelQty"]),
                            Deducted = Qty.Read(r["Cut"]),
                            SourceAfter = r["FromBalanceAfter"] == DBNull.Value ? (decimal?)null : Qty.Read(r["FromBalanceAfter"]),
                            UserId = r["UserID"]?.ToString() ?? "",
                            Barcode = r["ReferenceNo"]?.ToString() ?? "",
                            PartId = 1,
                            PartCode = r["PartCode"]?.ToString() ?? "",
                            PartName = r["Description"]?.ToString() ?? "",
                            SourcePartCode = r["SourcePartCode"]?.ToString() ?? "",
                            SourcePartName = r["SourcePartName"]?.ToString() ?? "",
                            StockCode = r["ToStockCode"]?.ToString() ?? "",
                            SourceCode = r["FromStockCode"]?.ToString() ?? "",
                            How = r["How"].ToString(),
                            MotherBefore = r["MotherBefore"] == DBNull.Value ? (decimal?)null : Qty.Read(r["MotherBefore"]),
                            MotherAfter = r["MotherAfter"] == DBNull.Value ? (decimal?)null : Qty.Read(r["MotherAfter"]),
                            MotherNow = r["MotherNow"] == DBNull.Value ? (decimal?)null : Qty.Read(r["MotherNow"]),
                            Reason = "DONE"
                        });
                    }
            }
            return list;
        }

        // ===== ACTION (สิทธิ์ APPROVE ของ Multi-Scanner) =====

        public class ActionResult
        {
            public bool Ok { get; set; }
            public string Message { get; set; }
            public ScanService.DeductResult Deduct { get; set; }
        }

        // ✔ APPROVE: รอดำเนินการ = รับเข้าคลังตามป้าย + ตัดคลังต้นทางเท่าที่ตัดได้ตอนนี้ / รายการเดิมที่รับไปแล้ว = ตัดคลังต้นทางอย่างเดียว
        //    status = APPROVED (กด APPROVE) / REGISTERED (ลงทะเบียนสินค้าจาก PRODUCT CONTROL แล้วรับเข้า) - แล้วไปแสดงที่ DEDUCTED
        public ActionResult Receive(DeductMissRow row, int partId, decimal qty, string userId, string status = "APPROVED")
        {
            var scan = new ScanService();
            var stock = new StockService().GetStocks().FirstOrDefault(s => s.StkId == row.StockId);
            if (stock == null) return new ActionResult { Message = $"ไม่พบคลัง {row.StockCode}" };
            string partCode, partA;
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("SELECT PartCode FROM CIMS.Parts WHERE PartID = @p", conn))
            {
                cmd.Parameters.AddWithValue("@p", partId);
                conn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return new ActionResult { Message = "ไม่พบสินค้าในระบบ" };
                    // CIMS.Parts ไม่มี PartACode แล้ว - PartCode เป็นตัวระบุหลักตัวเดียว (เหมือน ScanService)
                    partCode = r.GetString(0); partA = partCode;
                }
            }
            var coil = new ScanService.CoilLabel { CoilNo = string.IsNullOrWhiteSpace(row.CoilNo) ? null : row.CoilNo, MotherCoil = string.IsNullOrWhiteSpace(row.MotherCoil) ? null : row.MotherCoil };
            bool received = row.Status == "RECEIVED" && row.TxId.HasValue;
            var res = scan.UpdateStockWithDeduct(partId, partCode, partA, received ? row.LabelQty : qty, userId, row.Barcode, stock, row.SourceStockId,
                                                 coil: coil, approveMissId: row.MissId, approveStatus: status,
                                                 existingTxId: received ? row.TxId : null, formatId: row.FormatId);
            if (!res.Saved) return new ActionResult { Message = "บันทึกไม่สำเร็จ ลองใหม่อีกครั้ง", Deduct = res };
            LogService.WriteLog(userId, status == "REGISTERED" ? "NOT_DEDUCTED_REGISTER" : "NOT_DEDUCTED_APPROVE",
                $"{row.StockCode} <- {row.SourceCode} | {partCode} | Coil {row.CoilNo} | Mother {row.MotherCoil} | " +
                (received ? "already received, " : $"received {Qty.Plain(qty)}, ") + $"cut {res.SourceCode} {Qty.Plain(res.Deducted)}" +
                (res.Deducted > 0 ? $" ({res.SourcePartCode} {Qty.Plain(res.SourceBefore)} -> {Qty.Plain(res.SourceAfter)})" : " (nothing to cut yet)"), partCode);
            return new ActionResult { Ok = true, Deduct = res };
        }

        // MAX / MIN / Coil เพิ่ม (STOCK COIL มากกว่า 1 ม้วนของป้าย) ของสินค้าในคลังที่รับ (คลังหลัก = CIMS.Parts / คลังอื่น = CIMS.PartStocks)
        public void ApplyStockSetting(int stockId, int partId, decimal? max, decimal? min, int? extraCoils, string userId)
        {
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                bool isMain;
                using (var cmd = new SqlCommand("SELECT ISNULL(IsMain, 0) FROM CIMS.Stocks WHERE StockID = @s", conn))
                {
                    cmd.Parameters.AddWithValue("@s", stockId);
                    isMain = Convert.ToBoolean(cmd.ExecuteScalar() ?? false);
                }
                string coilCol = isMain ? (DbSchema.HasCountCoil ? "CoilQuantity" : null) : "BoxQuantity";
                string sql = isMain
                    ? "UPDATE CIMS.Parts SET MaxQuantity = ISNULL(@max, MaxQuantity), MinQuantity = ISNULL(@min, MinQuantity)" +
                      (coilCol != null ? ", CoilQuantity = ISNULL(CoilQuantity, 0) + @coil" : "") + " WHERE PartID = @p"
                    : "UPDATE CIMS.PartStocks SET MaxQuantity = ISNULL(@max, MaxQuantity), MinQuantity = ISNULL(@min, MinQuantity), BoxQuantity = ISNULL(BoxQuantity, 0) + @coil, UpdatedDate = GETDATE() WHERE PartID = @p AND StockID = @s";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add(QtyParam.Of("@max", max));
                    cmd.Parameters.Add(QtyParam.Of("@min", min));
                    cmd.Parameters.AddWithValue("@coil", extraCoils ?? 0);
                    cmd.Parameters.AddWithValue("@p", partId);
                    cmd.Parameters.AddWithValue("@s", stockId);
                    cmd.ExecuteNonQuery();
                }
            }
            LogService.WriteLog(userId, "NOT_DEDUCTED_STOCK_SETTING",
                $"Stock {stockId} part {partId} | MAX {(max.HasValue ? Qty.Plain(max.Value) : "-")} | MIN {(min.HasValue ? Qty.Plain(min.Value) : "-")} | extra coil {extraCoils ?? 0}", partId.ToString());
        }

        // 🗑 ลบ: รอดำเนินการ = ลบรายการ (ไม่มีอะไรเข้าคลัง) / รายการเดิมที่รับเข้าไปแล้ว = ยกเลิกรายการสแกนนั้น (คืนยอดคลัง) แล้วลบ - เก็บ Log
        public ActionResult Delete(DeductMissRow row, string userId)
        {
            if (row.Status == "RECEIVED" && row.TxId.HasValue)
            {
                try { new ScanAdjustService().CancelScan(row.TxId.Value, $"NOT DEDUCTED deleted (Coil {row.CoilNo})", userId); }
                catch (Exception ex) { return new ActionResult { Message = "ยกเลิกรายการสแกนไม่สำเร็จ\n" + ex.Message }; }
            }
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("DELETE FROM CIMS.DeductMisses WHERE MissID = @id", conn))
            {
                cmd.Parameters.AddWithValue("@id", row.MissId);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
            LogService.WriteLog(userId, "NOT_DEDUCTED_DELETE",
                $"Deleted {row.Status} | {row.StockCode} <- {row.SourceCode} | {row.PartCodeText} | Coil {row.CoilNo} | Mother {row.MotherCoil} | Label {row.LabelQtyText}" +
                (row.Status == "RECEIVED" ? $" | scan {row.TxId} cancelled (stock returned)" : "") + $" | {row.Barcode}", row.PartCode ?? "");
            return new ActionResult { Ok = true };
        }

        // 🗑 ลบหลายรายการ (แบบเดิม: เอาออกจากรายการเฉยๆ ไม่กระทบยอด) - ฐานที่ยังไม่รัน Update_20261016b.sql
        public int Delete(IEnumerable<int> ids)
        {
            var idList = ids.Distinct().ToList();
            if (idList.Count == 0) return 0;
            int n = 0;
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var tr = conn.BeginTransaction())
                {
                    foreach (var chunk in idList.Select((id, i) => (id, i)).GroupBy(x => x.i / 500, x => x.id))
                        using (var cmd = new SqlCommand("DELETE FROM CIMS.DeductMisses WHERE MissID IN (" + string.Join(",", chunk.Select(i => i.ToString())) + ")", conn, tr))
                            n += cmd.ExecuteNonQuery();
                    tr.Commit();
                }
            }
            return n;
        }

        // ===== Export (รูปแบบเดียวกับไฟล์ Export ทั้งระบบ) =====

        public string ExportDeducted(IList<DeductMissRow> rows)
        {
            string path = ImportTemplateService.NewPath(ImportTemplateService.Systems.Scanner, "DEDUCTED");
            using (var wb = new ClosedXML.Excel.XLWorkbook())
            {
                var ws = wb.Worksheets.Add("DEDUCTED");
                string[] heads = { "NO", "SCAN DATE", "PD CODE", "PRODUCT NAME", "MOTHER COIL", "COIL NO", "CUT FROM SOURCE (KG)", "SOURCE PD CODE", "SOURCE PRODUCT NAME", "MOTHER COIL BEFORE (KG)", "SOURCE BALANCE", "USER", "STOCK", "BARCODE" };
                ImportTemplateService.Header(ws, heads, new double[] { 6.4, 19, 18, 34, 15, 18, 16, 18, 34, 18, 16, 10, 26, 70 });
                int row = 2;
                foreach (var x in rows)
                {
                    ws.Cell(row, 1).Value = row - 1;
                    ws.Cell(row, 2).Value = x.ScanDate;
                    ws.Cell(row, 2).Style.DateFormat.Format = "dd/MM/yyyy HH:mm:ss";
                    ws.Cell(row, 3).Value = x.PartCode;
                    ws.Cell(row, 4).Value = x.PartName;
                    ws.Cell(row, 5).Value = x.MotherCoil;
                    ws.Cell(row, 6).Value = x.CoilNo;
                    ws.Cell(row, 7).Value = x.Deducted;
                    ws.Cell(row, 8).Value = x.SourcePartCode;
                    ws.Cell(row, 9).Value = x.SourcePartName;
                    if (x.MotherBefore.HasValue) ws.Cell(row, 10).Value = x.MotherBefore.Value;
                    if (x.SourceAfter.HasValue) ws.Cell(row, 11).Value = x.SourceAfter.Value;
                    ws.Cell(row, 12).Value = x.UserId;
                    ws.Cell(row, 13).Value = x.StockText;
                    ws.Cell(row, 14).Value = x.Barcode;
                    row++;
                }
                ImportTemplateService.Body(ws, row - 1, heads.Length);
                ImportTemplateService.LeftAlign(ws, row - 1, 4, 4);
                ImportTemplateService.LeftAlign(ws, row - 1, 9, 9);
                ImportTemplateService.LeftAlign(ws, row - 1, 14, 14);
                if (row > 2) { ws.Range(2, 7, row - 1, 7).Style.NumberFormat.Format = "#,##0.00#"; ws.Range(2, 10, row - 1, 11).Style.NumberFormat.Format = "#,##0.00#"; }
                wb.SaveAs(path);
            }
            return path;
        }

        public string Export(IList<DeductMissRow> rows)
        {
            string path = ImportTemplateService.NewPath(ImportTemplateService.Systems.Scanner, "NOT_DEDUCTED");
            using (var wb = new ClosedXML.Excel.XLWorkbook())
            {
                var ws = wb.Worksheets.Add("NOT DEDUCTED");
                string[] heads = { "NO", "SCAN DATE", "PD CODE", "PRODUCT NAME", "MOTHER COIL", "COIL NO", "SCANNED QTY (KG)", "STATUS", "REASON", "MOTHER COIL NOW", "USER", "STOCK", "BARCODE" };
                ImportTemplateService.Header(ws, heads, new double[] { 6.4, 19, 18, 34, 15, 18, 16, 34, 38, 20, 10, 26, 70 });
                int row = 2;
                foreach (var x in rows)
                {
                    ws.Cell(row, 1).Value = row - 1;
                    ws.Cell(row, 2).Value = x.ScanDate;
                    ws.Cell(row, 2).Style.DateFormat.Format = "dd/MM/yyyy HH:mm:ss";
                    ws.Cell(row, 3).Value = x.PartCodeText;
                    ws.Cell(row, 4).Value = x.PartNameText;
                    ws.Cell(row, 5).Value = x.MotherCoil;
                    ws.Cell(row, 6).Value = x.CoilNo;
                    ws.Cell(row, 7).Value = x.LabelQty;
                    ws.Cell(row, 8).Value = x.StatusText;
                    ws.Cell(row, 9).Value = x.ReasonText;
                    ws.Cell(row, 10).Value = x.MotherNowText;
                    ws.Cell(row, 11).Value = x.UserId;
                    ws.Cell(row, 12).Value = x.StockText;
                    ws.Cell(row, 13).Value = x.Barcode;
                    row++;
                }
                ImportTemplateService.Body(ws, row - 1, heads.Length);
                ImportTemplateService.LeftAlign(ws, row - 1, 4, 4);
                ImportTemplateService.LeftAlign(ws, row - 1, 8, 9);
                ImportTemplateService.LeftAlign(ws, row - 1, 13, 13);
                if (row > 2) ws.Range(2, 7, row - 1, 7).Style.NumberFormat.Format = "#,##0.00#";
                wb.SaveAs(path);
            }
            return path;
        }
    }
}
