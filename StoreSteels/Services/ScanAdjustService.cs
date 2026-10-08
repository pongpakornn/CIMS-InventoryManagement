using Microsoft.Data.SqlClient;
using CIMS.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Runtime.CompilerServices;

namespace CIMS.Services
{
    // 1 แถวในหน้าต่าง ADJUST SCAN
    public class ScanAdjustRow : INotifyPropertyChanged
    {
        public int TxId { get; set; }
        public DateTime TxDate { get; set; }
        public string StockCode { get; set; }
        public string TxType { get; set; }
        public string PartCode { get; set; }
        public string PartName { get; set; }
        public decimal Qty { get; set; }
        public bool AllowDecimal { get; set; }   // คลังของรายการนี้เปิด DECIMAL QTY
        public string QtyText => CIMS.Helpers.Qty.Plain(Qty);
        public string UserText { get; set; }
        public bool IsCancel { get; set; }
        public bool CanSelect => !IsCancel;
        public string AdjNote { get; set; }
        public string PrNo { get; set; }
        public string PrStatus { get; set; }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

        public string DateText => TxDate.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        public string StatusText => IsCancel ? "CANCELLED" : (string.IsNullOrEmpty(AdjNote) ? "" : "EDITED");
        public string PrText => string.IsNullOrEmpty(PrNo) ? "" : $"{PrNo} ({PrStatus})";

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // ผลการปรับ 1 รายการ (ยอดคงเหลือหลังปรับ + PR ที่ผูกกับรายการสแกนออก ให้ผู้มีสิทธิ์ PR ไปจัดการเอง)
    public class ScanAdjustResult
    {
        public int TxId { get; set; }
        public decimal BalanceAfter { get; set; }
        public string StockCode { get; set; }
        public string PrNo { get; set; }
        public string PrStatus { get; set; }
        public string SourceNote { get; set; }
    }

    // ✏️ ADJUST SCAN (Level 1): แก้จำนวน / ยกเลิกรายการสแกน แล้วปรับยอดคลังย้อนให้ใน Transaction เดียว
    //   IN / RETURN = เคยบวกยอด -> ยกเลิกแล้วหักออก / OUT = เคยลดยอด -> ยกเลิกแล้วคืนยอด
    //   กล่อง (คลังที่ไม่ใช่คลังหลัก) ย้อนตาม BoxChange ที่บันทึกไว้ / รายการเก่าที่ไม่มี BoxChange: IN +1, OUT -1, RETURN คิดจากชิ้น
    //   แก้จำนวน = ปรับเฉพาะชิ้นตามส่วนต่าง (จำนวนกล่องไม่เปลี่ยน) / ยอดติดลบไม่ได้ / ทุกครั้งเก็บลง CIMS.ScanAdjustments พร้อมเหตุผล
    public class ScanAdjustService
    {
        private readonly string _cs = GlobalConfig.ConnStr;

        public List<ScanAdjustRow> GetRows(string keyword, DateTime from, DateTime to, int? stkId, bool stockIsMain, string txType)
        {
            var list = new List<ScanAdjustRow>();
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"
                SELECT TOP 500 t.TransactionID, t.TransactionDate, ISNULL(s.StockCode, 'MAIN') AS StockCode, t.TransactionType,
                       {DEC} AS AllowDecimal,
                       ISNULL(p.PartCode, ISNULL(t.PartACode, '')) AS PartCode, ISNULL(p.Description, '') AS Description, t.Quantity,
                       t.UserID, ISNULL(u.FullName, '') AS FullName, ISNULL(t.IsCancelled, 0) AS IsCancelled, t.AdjustNote,
                       pr.PRNumber, pr.Status
                FROM CIMS.ScanTransactions t
                LEFT JOIN CIMS.Parts p  ON p.PartID = t.PartID
                LEFT JOIN CIMS.Stocks s ON s.StockID = t.StockID
                LEFT JOIN CIMS.Users u  ON u.UserID = t.UserID
                OUTER APPLY (SELECT TOP 1 h.PRNumber, h.Status FROM CIMS.PRHeaders h WHERE h.ScanTransactionID = t.TransactionID ORDER BY h.PRNumber DESC) pr
                WHERE t.TransactionDate >= @from AND t.TransactionDate < @to
                  AND (@stk IS NULL OR t.StockID = @stk OR (@isMain = 1 AND t.StockID IS NULL))
                  AND (@type IS NULL OR t.TransactionType = @type)
                  AND (@key = '' OR p.PartCode LIKE '%' + @key + '%' OR p.Description LIKE '%' + @key + '%'
                       OR ISNULL(p.PartA, '') LIKE '%' + @key + '%' OR ISNULL(p.PartNumber, '') LIKE '%' + @key + '%'
                       OR ISNULL(t.PartACode, '') LIKE '%' + @key + '%' OR ISNULL(t.ReferenceNo, '') LIKE '%' + @key + '%'
                       OR t.UserID LIKE '%' + @key + '%' OR ISNULL(u.FullName, '') LIKE '%' + @key + '%')
                ORDER BY t.TransactionDate DESC, t.TransactionID DESC".Replace("{DEC}", CIMS.Helpers.DbSchema.HasAllowDecimal
                    ? "ISNULL(s.AllowDecimal, ISNULL((SELECT TOP 1 m.AllowDecimal FROM CIMS.Stocks m WHERE m.IsMain = 1), 0))"
                    : "CAST(0 AS BIT)"), conn))
            {
                cmd.Parameters.Add("@from", SqlDbType.DateTime).Value = from.Date;
                cmd.Parameters.Add("@to", SqlDbType.DateTime).Value = to.Date.AddDays(1);
                cmd.Parameters.Add("@stk", SqlDbType.Int).Value = (object)stkId ?? DBNull.Value;
                cmd.Parameters.Add("@isMain", SqlDbType.Bit).Value = stockIsMain;
                cmd.Parameters.Add("@type", SqlDbType.VarChar, 20).Value = (object)txType ?? DBNull.Value;
                cmd.Parameters.Add("@key", SqlDbType.NVarChar, 200).Value = (keyword ?? "").Trim();
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new ScanAdjustRow
                        {
                            TxId = Convert.ToInt32(r["TransactionID"]),
                            TxDate = Convert.ToDateTime(r["TransactionDate"]),
                            StockCode = r["StockCode"].ToString(),
                            TxType = r["TransactionType"].ToString(),
                            PartCode = r["PartCode"].ToString(),
                            PartName = r["Description"].ToString(),
                            Qty = CIMS.Helpers.Qty.Read(r["Quantity"]),
                            AllowDecimal = Convert.ToBoolean(r["AllowDecimal"]),
                            UserText = string.IsNullOrWhiteSpace(r["FullName"].ToString()) ? r["UserID"].ToString() : $"{r["UserID"]} - {r["FullName"]}",
                            IsCancel = Convert.ToBoolean(r["IsCancelled"]),
                            AdjNote = r["AdjustNote"] == DBNull.Value ? null : r["AdjustNote"].ToString(),
                            PrNo = r["PRNumber"] == DBNull.Value ? null : r["PRNumber"].ToString(),
                            PrStatus = r["Status"] == DBNull.Value ? null : r["Status"].ToString()
                        });
            }
            return list;
        }

        public ScanAdjustResult CancelScan(int txId, string reason, string userId) => Adjust(txId, null, reason, userId);
        public ScanAdjustResult EditQty(int txId, decimal newQty, string reason, string userId) => Adjust(txId, newQty, reason, userId);

        // newQty = null -> ยกเลิกรายการ / มีค่า -> แก้จำนวน
        private ScanAdjustResult Adjust(int txId, decimal? newQty, string reason, string userId)
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("กรุณาระบุหมายเหตุ / สาเหตุที่แก้ไข Stock");
            if (newQty.HasValue && newQty.Value <= 0) throw new InvalidOperationException("จำนวนต้องมากกว่า 0 (ถ้าต้องการลบรายการให้ใช้ DELETE)");

            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        // 1) รายการเดิม (ล็อกไว้กันคนอื่นแก้พร้อมกัน)
                        int ptId; decimal oldQty; string type; int? stkId, txBox; bool cancelled, isMain; string stkCode; DateTime txDate;
                        using (var cmd = new SqlCommand(@"
                            SELECT t.PartID, t.Quantity, t.TransactionType, t.StockID, t.BoxChange, ISNULL(t.IsCancelled, 0) AS IsCancelled, t.TransactionDate,
                                   ISNULL(s.IsMain, 1) AS IsMain, ISNULL(s.StockCode, 'MAIN') AS StockCode
                            FROM CIMS.ScanTransactions t WITH (UPDLOCK, HOLDLOCK)
                            LEFT JOIN CIMS.Stocks s ON s.StockID = t.StockID
                            WHERE t.TransactionID = @id", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@id", txId);
                            using (var r = cmd.ExecuteReader())
                            {
                                if (!r.Read()) throw new InvalidOperationException($"ไม่พบรายการสแกน #{txId} (อาจถูกลบไปแล้ว)");
                                ptId = Convert.ToInt32(r["PartID"]);
                                oldQty = CIMS.Helpers.Qty.Read(r["Quantity"]);
                                type = r["TransactionType"].ToString().Trim().ToUpperInvariant();
                                stkId = r["StockID"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["StockID"]);
                                txBox = r["BoxChange"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["BoxChange"]);
                                cancelled = Convert.ToBoolean(r["IsCancelled"]);
                                isMain = Convert.ToBoolean(r["IsMain"]);
                                stkCode = r["StockCode"].ToString();
                                txDate = Convert.ToDateTime(r["TransactionDate"]);
                            }
                        }
                        if (cancelled) throw new InvalidOperationException($"รายการ #{txId} ถูกยกเลิกไปแล้ว");
                        if (newQty.HasValue && newQty.Value == oldQty) throw new InvalidOperationException("จำนวนใหม่เท่ากับจำนวนเดิม");

                        // 2) ยอดที่ต้องปรับ: IN / RETURN บวกยอด, OUT ลดยอด
                        int sign = type == "OUT" ? -1 : 1;
                        decimal delta = newQty.HasValue ? sign * (newQty.Value - oldQty) : -sign * oldQty;

                        // กล่องที่ต้องย้อน (เฉพาะยกเลิก + คลังที่ไม่ใช่คลังหลัก) - null = ให้ระบบคิดกล่องจากชิ้น
                        int? boxDelta = null;
                        bool recalcBox = false;
                        if (!isMain)
                        {
                            if (newQty.HasValue) boxDelta = 0;
                            else
                            {
                                int? moved = txBox ?? (type == "IN" ? 1 : type == "OUT" ? -1 : (int?)null);
                                if (moved.HasValue) boxDelta = -moved.Value; else recalcBox = true;
                            }
                        }

                        // 3) ปรับยอดคลัง (ห้ามติดลบ)
                        decimal balance;
                        if (isMain)
                        {
                            using (var cmd = new SqlCommand("SELECT ISNULL(StockQuantity, 0) FROM CIMS.Parts WITH (UPDLOCK, HOLDLOCK) WHERE PartID = @p", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@p", ptId);
                                object v = cmd.ExecuteScalar();
                                if (v == null) throw new InvalidOperationException("ไม่พบสินค้านี้ในระบบแล้ว");
                                balance = CIMS.Helpers.Qty.Read(v);
                            }
                            if (balance + delta < 0)
                                throw new InvalidOperationException($"ยอดคงเหลือในคลัง {stkCode} ไม่พอ (คงเหลือ {CIMS.Helpers.Qty.Plain(balance)} ต้องปรับ {CIMS.Helpers.Qty.Plain(delta)})");
                            // คลังหลักที่นับ Coil: ยกเลิกแล้วย้อน Coil ตาม BoxChange ที่บันทึกไว้ (แก้จำนวน = Coil ไม่เปลี่ยน)
                            bool coilBack = !newQty.HasValue && txBox.HasValue && txBox.Value != 0 && CIMS.Helpers.DbSchema.HasCountCoil;
                            using (var cmd = new SqlCommand(coilBack
                                ? "UPDATE CIMS.Parts SET StockQuantity = ISNULL(StockQuantity, 0) + @d, CoilQuantity = CASE WHEN CoilQuantity - @c < 0 THEN 0 ELSE CoilQuantity - @c END WHERE PartID = @p"
                                : "UPDATE CIMS.Parts SET StockQuantity = ISNULL(StockQuantity, 0) + @d WHERE PartID = @p", conn, trans))
                            {
                                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@d", delta));
                                cmd.Parameters.AddWithValue("@p", ptId);
                                cmd.Parameters.AddWithValue("@c", txBox ?? 0);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            int box;
                            using (var cmd = new SqlCommand("SELECT Quantity, BoxQuantity FROM CIMS.PartStocks WITH (UPDLOCK, HOLDLOCK) WHERE StockID = @s AND PartID = @p", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@s", stkId.Value);
                                cmd.Parameters.AddWithValue("@p", ptId);
                                using (var r = cmd.ExecuteReader())
                                {
                                    if (!r.Read()) throw new InvalidOperationException($"สินค้านี้ไม่อยู่ในคลัง {stkCode} แล้ว ปรับยอดไม่ได้");
                                    balance = CIMS.Helpers.Qty.Read(r["Quantity"]);
                                    box = Convert.ToInt32(r["BoxQuantity"]);
                                }
                            }
                            if (balance + delta < 0)
                                throw new InvalidOperationException($"ยอดคงเหลือในคลัง {stkCode} ไม่พอ (คงเหลือ {CIMS.Helpers.Qty.Plain(balance)} ต้องปรับ {CIMS.Helpers.Qty.Plain(delta)})");
                            string sql = recalcBox
                                ? "UPDATE CIMS.PartStocks SET Quantity = Quantity + @d, UpdatedDate = GETDATE() WHERE StockID = @s AND PartID = @p"
                                : "UPDATE CIMS.PartStocks SET Quantity = Quantity + @d, BoxQuantity = @box, UpdatedDate = GETDATE() WHERE StockID = @s AND PartID = @p";
                            using (var cmd = new SqlCommand(sql, conn, trans))
                            {
                                cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@d", delta));
                                cmd.Parameters.AddWithValue("@s", stkId.Value);
                                cmd.Parameters.AddWithValue("@p", ptId);
                                if (!recalcBox) cmd.Parameters.AddWithValue("@box", Math.Max(0, box + (boxDelta ?? 0)));
                                cmd.ExecuteNonQuery();
                            }
                        }
                        decimal balAfter = balance + delta;

                        // 4) สแกนรับเข้าคลังหลักที่ตัดยอดคลังต้นทางอัตโนมัติ (รูปแบบป้ายที่ตั้ง "ตัดยอดจากคลัง") -> ยกเลิกแล้วคืนยอดคลังต้นทางด้วย
                        string sourceNote = null;
                        // รับเข้าได้ทุกคลัง / คลังต้นทางอาจเป็นสินค้าคนละรหัสที่ BIN เดียวกัน (SourcePartID)
                        if (!newQty.HasValue && type == "IN")
                        {
                            int trfId = 0, fromStk = 0, srcPt = ptId; decimal trfQty = 0; string fromCode = null;
                            using (var cmd = new SqlCommand(@"
                                SELECT TOP 1 TransferID, Quantity, FromStockID, FromStockCode, " + (CIMS.Helpers.DbSchema.HasCountCoil ? "ISNULL(SourcePartID, PartID)" : "PartID") + @" FROM CIMS.StockTransfers
                                WHERE TransferMode = 'SCAN' AND PartID = @p AND (ToStockID = @to OR @to IS NULL)
                                  AND TransferDate BETWEEN DATEADD(SECOND, -5, @d) AND DATEADD(SECOND, 5, @d)
                                ORDER BY ABS(DATEDIFF(MILLISECOND, TransferDate, @d))", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@p", ptId);
                                cmd.Parameters.Add("@to", SqlDbType.Int).Value = (object)stkId ?? DBNull.Value;
                                cmd.Parameters.AddWithValue("@d", txDate);
                                using (var r = cmd.ExecuteReader())
                                    if (r.Read()) { trfId = Convert.ToInt32(r[0]); trfQty = CIMS.Helpers.Qty.Read(r[1]); fromStk = Convert.ToInt32(r[2]); fromCode = r[3].ToString(); srcPt = Convert.ToInt32(r[4]); }
                            }
                            if (trfId > 0 && trfQty > 0)
                            {
                                // คลังต้นทางนับ Coil: คืน 1 Coil ด้วย (ตอนตัดยอดหัก 1 Coil)
                                bool srcCoil = false;
                                if (CIMS.Helpers.DbSchema.HasCountCoil)
                                    using (var cmd = new SqlCommand("SELECT CountCoil FROM CIMS.Stocks WHERE StockID = @s", conn, trans))
                                    {
                                        cmd.Parameters.AddWithValue("@s", fromStk);
                                        srcCoil = Convert.ToBoolean(cmd.ExecuteScalar() ?? false);
                                    }
                                using (var cmd = new SqlCommand(srcCoil
                                    ? "UPDATE CIMS.PartStocks SET Quantity = Quantity + @q, BoxQuantity = BoxQuantity + 1, UpdatedDate = GETDATE() WHERE StockID = @s AND PartID = @p"
                                    : "UPDATE CIMS.PartStocks SET Quantity = Quantity + @q, UpdatedDate = GETDATE() WHERE StockID = @s AND PartID = @p", conn, trans))
                                {
                                    cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@q", trfQty));
                                    cmd.Parameters.AddWithValue("@s", fromStk);
                                    cmd.Parameters.AddWithValue("@p", srcPt);
                                    if (cmd.ExecuteNonQuery() > 0) sourceNote = $"คืนยอดคลังต้นทาง {fromCode} +{CIMS.Helpers.Qty.Plain(trfQty)}";
                                }
                            }
                        }

                        // 4b) 🧲 ทะเบียน Coil ของรายการนี้: ยกเลิก = ย้อนกลับ (ลูกที่สร้างใหม่ = ลบ / ลูกที่ย้ายมา = คืนคลังเดิม / จ่ายออก = กลับเข้าคลัง)
                        //     แก้จำนวน = น้ำหนักของลูกนั้นตามจำนวนใหม่
                        if (CIMS.Helpers.DbSchema.HasCoilRegister)
                        {
                            if (newQty.HasValue)
                            {
                                using (var cmd = new SqlCommand(@"UPDATE c SET c.WeightKG = @w, c.UpdatedDate = GETDATE() FROM CIMS.Coils c
                                                                  WHERE c.Status = 'IN' AND c.CoilID IN (SELECT CoilID FROM CIMS.CoilMoves WHERE ScanTransactionID = @tx AND Undone = 0 AND ToStatus = 'IN')", conn, trans))
                                {
                                    cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@w", newQty.Value));
                                    cmd.Parameters.AddWithValue("@tx", txId);
                                    cmd.ExecuteNonQuery();
                                }
                            }
                            else
                            {
                                var moves = new List<(int MoveId, int CoilId, string Action, int? FromStock, int? FromPart, string FromStatus, decimal? FromWeight)>();
                                using (var cmd = new SqlCommand("SELECT MoveID, CoilID, Action, FromStockID, FromPartID, FromStatus, FromWeightKG FROM CIMS.CoilMoves WHERE ScanTransactionID = @tx AND Undone = 0 ORDER BY MoveID DESC", conn, trans))
                                {
                                    cmd.Parameters.AddWithValue("@tx", txId);
                                    using (var r = cmd.ExecuteReader())
                                        while (r.Read())
                                            moves.Add((r.GetInt32(0), r.GetInt32(1), r.GetString(2),
                                                       r.IsDBNull(3) ? (int?)null : r.GetInt32(3), r.IsDBNull(4) ? (int?)null : r.GetInt32(4),
                                                       r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? (decimal?)null : r.GetDecimal(6)));
                                }
                                foreach (var m in moves)
                                {
                                    string sql = m.Action == "CREATE"
                                        ? @"DELETE FROM CIMS.CoilMoves WHERE CoilID = @c; DELETE FROM CIMS.Coils WHERE CoilID = @c;"
                                        : @"UPDATE CIMS.Coils SET StockID = @fs, PartID = @fp, Status = @fst, WeightKG = ISNULL(@fw, WeightKG),
                                                   OutDate = CASE WHEN @fst = 'OUT' THEN OutDate ELSE NULL END, UpdatedDate = GETDATE() WHERE CoilID = @c;
                                           UPDATE CIMS.CoilMoves SET Undone = 1 WHERE MoveID = @m;";
                                    using (var cmd = new SqlCommand(sql, conn, trans))
                                    {
                                        cmd.Parameters.AddWithValue("@c", m.CoilId);
                                        cmd.Parameters.AddWithValue("@m", m.MoveId);
                                        cmd.Parameters.AddWithValue("@fs", (object)m.FromStock ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@fp", (object)m.FromPart ?? DBNull.Value);
                                        cmd.Parameters.AddWithValue("@fst", (object)m.FromStatus ?? "IN");
                                        cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@fw", m.FromWeight));
                                        cmd.ExecuteNonQuery();
                                    }
                                }
                                if (moves.Count > 0) sourceNote = (sourceNote == null ? "" : sourceNote + " | ") + $"ย้อนทะเบียน Coil {moves.Count} รายการ";
                            }
                        }

                        // 5) บันทึกรายการ + ประวัติการปรับ
                        string note = (newQty.HasValue ? $"EDIT QTY {CIMS.Helpers.Qty.Plain(oldQty)} -> {CIMS.Helpers.Qty.Plain(newQty.Value)}" : "CANCELLED")
                                      + $" by {userId} {DateTime.Now:dd/MM/yyyy HH:mm} : {reason.Trim()}";
                        if (note.Length > 300) note = note.Substring(0, 300);
                        using (var cmd = new SqlCommand(newQty.HasValue
                            ? "UPDATE CIMS.ScanTransactions SET Quantity = @q, AdjustNote = @n WHERE TransactionID = @id"
                            : "UPDATE CIMS.ScanTransactions SET IsCancelled = 1, AdjustNote = @n WHERE TransactionID = @id", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@id", txId);
                            cmd.Parameters.AddWithValue("@n", note);
                            if (newQty.HasValue) cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@q", newQty.Value));
                            cmd.ExecuteNonQuery();
                        }
                        using (var cmd = new SqlCommand(@"
                            INSERT INTO CIMS.ScanAdjustments (TransactionID, Action, TransactionType, StockID, PartID, OldQuantity, NewQuantity, StockChange, BalanceAfter, Reason, UserID)
                            VALUES (@id, @a, @t, @s, @p, @o, @n, @d, @b, @r, @u)", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@id", txId);
                            cmd.Parameters.AddWithValue("@a", newQty.HasValue ? "EDIT_QTY" : "CANCEL");
                            cmd.Parameters.AddWithValue("@t", type);
                            cmd.Parameters.Add("@s", SqlDbType.Int).Value = (object)stkId ?? DBNull.Value;
                            cmd.Parameters.AddWithValue("@p", ptId);
                            cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@o", oldQty));
                            cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@n", newQty ?? 0m));
                            cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@d", delta));
                            cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@b", balAfter));
                            cmd.Parameters.AddWithValue("@r", reason.Trim() + (sourceNote != null ? $" | {sourceNote}" : ""));
                            cmd.Parameters.AddWithValue("@u", userId ?? "");
                            cmd.ExecuteNonQuery();
                        }

                        // 6) PR อัตโนมัติจากการสแกนออก - ไม่แตะ ให้ผู้มีสิทธิ์ PR จัดการเอง (แจ้งเลข PR กลับไป)
                        var result = new ScanAdjustResult { TxId = txId, BalanceAfter = balAfter, StockCode = stkCode, SourceNote = sourceNote };
                        using (var cmd = new SqlCommand("SELECT TOP 1 PRNumber, Status FROM CIMS.PRHeaders WHERE ScanTransactionID = @id ORDER BY PRNumber DESC", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@id", txId);
                            using (var r = cmd.ExecuteReader())
                                if (r.Read()) { result.PrNo = r[0].ToString(); result.PrStatus = r[1].ToString(); }
                        }

                        trans.Commit();
                        return result;
                    }
                    catch
                    {
                        trans.Rollback();
                        throw;
                    }
                }
            }
        }
    }
}
