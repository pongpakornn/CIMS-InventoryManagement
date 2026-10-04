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
        public int Qty { get; set; }
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
        public int BalanceAfter { get; set; }
        public string StockCode { get; set; }
        public string PrNo { get; set; }
        public string PrStatus { get; set; }
        public string SourceNote { get; set; }
    }

    // ✏️ ADJUST SCAN (Level 1): แก้จำนวน / ยกเลิกรายการสแกน แล้วปรับยอดคลังย้อนให้ใน Transaction เดียว
    //   IN / RETURN = เคยบวกยอด -> ยกเลิกแล้วหักออก / OUT = เคยลดยอด -> ยกเลิกแล้วคืนยอด
    //   กล่อง (คลังที่ไม่ใช่คลังหลัก) ย้อนตาม TX_BOX ที่บันทึกไว้ / รายการเก่าที่ไม่มี TX_BOX: IN +1, OUT -1, RETURN คิดจากชิ้น
    //   แก้จำนวน = ปรับเฉพาะชิ้นตามส่วนต่าง (จำนวนกล่องไม่เปลี่ยน) / ยอดติดลบไม่ได้ / ทุกครั้งเก็บลง TRN_SCAN_ADJ พร้อมเหตุผล
    public class ScanAdjustService
    {
        private readonly string _cs = GlobalConfig.ConnStr;

        public List<ScanAdjustRow> GetRows(string keyword, DateTime from, DateTime to, int? stkId, bool stockIsMain, string txType)
        {
            var list = new List<ScanAdjustRow>();
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"
                SELECT TOP 500 t.TX_ID, t.TX_DATE, ISNULL(s.STK_CODE, 'MAIN') AS STK_CODE, t.TX_TYPE,
                       ISNULL(p.PT_CODE, ISNULL(t.PT_ACODE, '')) AS PT_CODE, ISNULL(p.PT_DESC, '') AS PT_DESC, t.TX_QTY,
                       t.USR_ID, ISNULL(u.USR_NAME, '') AS USR_NAME, ISNULL(t.IS_CANCEL, 0) AS IS_CANCEL, t.ADJ_NOTE,
                       pr.PR_NO, pr.PR_STAT
                FROM TRN_SCAN t
                LEFT JOIN MST_PART p  ON p.PT_ID = t.PT_ID
                LEFT JOIN MST_STOCK s ON s.STK_ID = t.STK_ID
                LEFT JOIN MST_USER u  ON u.USR_ID = t.USR_ID
                OUTER APPLY (SELECT TOP 1 h.PR_NO, h.PR_STAT FROM TRN_PR_H h WHERE h.SCAN_TX_ID = t.TX_ID ORDER BY h.PR_NO DESC) pr
                WHERE t.TX_DATE >= @from AND t.TX_DATE < @to
                  AND (@stk IS NULL OR t.STK_ID = @stk OR (@isMain = 1 AND t.STK_ID IS NULL))
                  AND (@type IS NULL OR t.TX_TYPE = @type)
                  AND (@key = '' OR p.PT_CODE LIKE '%' + @key + '%' OR p.PT_DESC LIKE '%' + @key + '%'
                       OR ISNULL(p.PT_PARTA, '') LIKE '%' + @key + '%' OR ISNULL(p.PT_PARTNO, '') LIKE '%' + @key + '%'
                       OR ISNULL(t.PT_ACODE, '') LIKE '%' + @key + '%' OR ISNULL(t.REF_NO, '') LIKE '%' + @key + '%'
                       OR t.USR_ID LIKE '%' + @key + '%' OR ISNULL(u.USR_NAME, '') LIKE '%' + @key + '%')
                ORDER BY t.TX_DATE DESC, t.TX_ID DESC", conn))
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
                            TxId = Convert.ToInt32(r["TX_ID"]),
                            TxDate = Convert.ToDateTime(r["TX_DATE"]),
                            StockCode = r["STK_CODE"].ToString(),
                            TxType = r["TX_TYPE"].ToString(),
                            PartCode = r["PT_CODE"].ToString(),
                            PartName = r["PT_DESC"].ToString(),
                            Qty = r["TX_QTY"] == DBNull.Value ? 0 : Convert.ToInt32(r["TX_QTY"]),
                            UserText = string.IsNullOrWhiteSpace(r["USR_NAME"].ToString()) ? r["USR_ID"].ToString() : $"{r["USR_ID"]} - {r["USR_NAME"]}",
                            IsCancel = Convert.ToBoolean(r["IS_CANCEL"]),
                            AdjNote = r["ADJ_NOTE"] == DBNull.Value ? null : r["ADJ_NOTE"].ToString(),
                            PrNo = r["PR_NO"] == DBNull.Value ? null : r["PR_NO"].ToString(),
                            PrStatus = r["PR_STAT"] == DBNull.Value ? null : r["PR_STAT"].ToString()
                        });
            }
            return list;
        }

        public ScanAdjustResult CancelScan(int txId, string reason, string userId) => Adjust(txId, null, reason, userId);
        public ScanAdjustResult EditQty(int txId, int newQty, string reason, string userId) => Adjust(txId, newQty, reason, userId);

        // newQty = null -> ยกเลิกรายการ / มีค่า -> แก้จำนวน
        private ScanAdjustResult Adjust(int txId, int? newQty, string reason, string userId)
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
                        int ptId, oldQty; string type; int? stkId, txBox; bool cancelled, isMain; string stkCode; DateTime txDate;
                        using (var cmd = new SqlCommand(@"
                            SELECT t.PT_ID, t.TX_QTY, t.TX_TYPE, t.STK_ID, t.TX_BOX, ISNULL(t.IS_CANCEL, 0) AS IS_CANCEL, t.TX_DATE,
                                   ISNULL(s.IS_MAIN, 1) AS IS_MAIN, ISNULL(s.STK_CODE, 'MAIN') AS STK_CODE
                            FROM TRN_SCAN t WITH (UPDLOCK, HOLDLOCK)
                            LEFT JOIN MST_STOCK s ON s.STK_ID = t.STK_ID
                            WHERE t.TX_ID = @id", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@id", txId);
                            using (var r = cmd.ExecuteReader())
                            {
                                if (!r.Read()) throw new InvalidOperationException($"ไม่พบรายการสแกน #{txId} (อาจถูกลบไปแล้ว)");
                                ptId = Convert.ToInt32(r["PT_ID"]);
                                oldQty = r["TX_QTY"] == DBNull.Value ? 0 : Convert.ToInt32(r["TX_QTY"]);
                                type = r["TX_TYPE"].ToString().Trim().ToUpperInvariant();
                                stkId = r["STK_ID"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["STK_ID"]);
                                txBox = r["TX_BOX"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["TX_BOX"]);
                                cancelled = Convert.ToBoolean(r["IS_CANCEL"]);
                                isMain = Convert.ToBoolean(r["IS_MAIN"]);
                                stkCode = r["STK_CODE"].ToString();
                                txDate = Convert.ToDateTime(r["TX_DATE"]);
                            }
                        }
                        if (cancelled) throw new InvalidOperationException($"รายการ #{txId} ถูกยกเลิกไปแล้ว");
                        if (newQty.HasValue && newQty.Value == oldQty) throw new InvalidOperationException("จำนวนใหม่เท่ากับจำนวนเดิม");

                        // 2) ยอดที่ต้องปรับ: IN / RETURN บวกยอด, OUT ลดยอด
                        int sign = type == "OUT" ? -1 : 1;
                        int delta = newQty.HasValue ? sign * (newQty.Value - oldQty) : -sign * oldQty;

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
                        int balance;
                        if (isMain)
                        {
                            using (var cmd = new SqlCommand("SELECT ISNULL(QTY_STKB, 0) FROM MST_PART WITH (UPDLOCK, HOLDLOCK) WHERE PT_ID = @p", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@p", ptId);
                                object v = cmd.ExecuteScalar();
                                if (v == null) throw new InvalidOperationException("ไม่พบสินค้านี้ในระบบแล้ว");
                                balance = Convert.ToInt32(v);
                            }
                            if (balance + delta < 0)
                                throw new InvalidOperationException($"ยอดคงเหลือในคลัง {stkCode} ไม่พอ (คงเหลือ {balance:N0} ต้องปรับ {delta:N0})");
                            using (var cmd = new SqlCommand("UPDATE MST_PART SET QTY_STKB = ISNULL(QTY_STKB, 0) + @d WHERE PT_ID = @p", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@d", delta);
                                cmd.Parameters.AddWithValue("@p", ptId);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            int box;
                            using (var cmd = new SqlCommand("SELECT QTY, QTY_BOX FROM MST_PART_STOCK WITH (UPDLOCK, HOLDLOCK) WHERE STK_ID = @s AND PT_ID = @p", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@s", stkId.Value);
                                cmd.Parameters.AddWithValue("@p", ptId);
                                using (var r = cmd.ExecuteReader())
                                {
                                    if (!r.Read()) throw new InvalidOperationException($"สินค้านี้ไม่อยู่ในคลัง {stkCode} แล้ว ปรับยอดไม่ได้");
                                    balance = Convert.ToInt32(r["QTY"]);
                                    box = Convert.ToInt32(r["QTY_BOX"]);
                                }
                            }
                            if (balance + delta < 0)
                                throw new InvalidOperationException($"ยอดคงเหลือในคลัง {stkCode} ไม่พอ (คงเหลือ {balance:N0} ต้องปรับ {delta:N0})");
                            string sql = recalcBox
                                ? "UPDATE MST_PART_STOCK SET QTY = QTY + @d, UPDATED_DATE = GETDATE() WHERE STK_ID = @s AND PT_ID = @p"
                                : "UPDATE MST_PART_STOCK SET QTY = QTY + @d, QTY_BOX = @box, UPDATED_DATE = GETDATE() WHERE STK_ID = @s AND PT_ID = @p";
                            using (var cmd = new SqlCommand(sql, conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@d", delta);
                                cmd.Parameters.AddWithValue("@s", stkId.Value);
                                cmd.Parameters.AddWithValue("@p", ptId);
                                if (!recalcBox) cmd.Parameters.AddWithValue("@box", Math.Max(0, box + (boxDelta ?? 0)));
                                cmd.ExecuteNonQuery();
                            }
                        }
                        int balAfter = balance + delta;

                        // 4) สแกนรับเข้าคลังหลักที่ตัดยอดคลังต้นทางอัตโนมัติ (รูปแบบป้ายที่ตั้ง "ตัดยอดจากคลัง") -> ยกเลิกแล้วคืนยอดคลังต้นทางด้วย
                        string sourceNote = null;
                        if (!newQty.HasValue && type == "IN" && isMain)
                        {
                            int trfId = 0, trfQty = 0, fromStk = 0; string fromCode = null;
                            using (var cmd = new SqlCommand(@"
                                SELECT TOP 1 TRF_ID, QTY, FROM_STK_ID, FROM_STK_CODE FROM TRN_TRANSFER
                                WHERE TRF_MODE = 'SCAN' AND PT_ID = @p AND (TO_STK_ID = @to OR @to IS NULL)
                                  AND TRF_DATE BETWEEN DATEADD(SECOND, -5, @d) AND DATEADD(SECOND, 5, @d)
                                ORDER BY ABS(DATEDIFF(MILLISECOND, TRF_DATE, @d))", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@p", ptId);
                                cmd.Parameters.Add("@to", SqlDbType.Int).Value = (object)stkId ?? DBNull.Value;
                                cmd.Parameters.AddWithValue("@d", txDate);
                                using (var r = cmd.ExecuteReader())
                                    if (r.Read()) { trfId = Convert.ToInt32(r[0]); trfQty = Convert.ToInt32(r[1]); fromStk = Convert.ToInt32(r[2]); fromCode = r[3].ToString(); }
                            }
                            if (trfId > 0 && trfQty > 0)
                            {
                                using (var cmd = new SqlCommand("UPDATE MST_PART_STOCK SET QTY = QTY + @q, UPDATED_DATE = GETDATE() WHERE STK_ID = @s AND PT_ID = @p", conn, trans))
                                {
                                    cmd.Parameters.AddWithValue("@q", trfQty);
                                    cmd.Parameters.AddWithValue("@s", fromStk);
                                    cmd.Parameters.AddWithValue("@p", ptId);
                                    if (cmd.ExecuteNonQuery() > 0) sourceNote = $"คืนยอดคลังต้นทาง {fromCode} +{trfQty:N0}";
                                }
                            }
                        }

                        // 5) บันทึกรายการ + ประวัติการปรับ
                        string note = (newQty.HasValue ? $"EDIT QTY {oldQty:N0} -> {newQty.Value:N0}" : "CANCELLED")
                                      + $" by {userId} {DateTime.Now:dd/MM/yyyy HH:mm} : {reason.Trim()}";
                        if (note.Length > 300) note = note.Substring(0, 300);
                        using (var cmd = new SqlCommand(newQty.HasValue
                            ? "UPDATE TRN_SCAN SET TX_QTY = @q, ADJ_NOTE = @n WHERE TX_ID = @id"
                            : "UPDATE TRN_SCAN SET IS_CANCEL = 1, ADJ_NOTE = @n WHERE TX_ID = @id", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@id", txId);
                            cmd.Parameters.AddWithValue("@n", note);
                            if (newQty.HasValue) cmd.Parameters.AddWithValue("@q", newQty.Value);
                            cmd.ExecuteNonQuery();
                        }
                        using (var cmd = new SqlCommand(@"
                            INSERT INTO TRN_SCAN_ADJ (TX_ID, ADJ_ACTION, TX_TYPE, STK_ID, PT_ID, OLD_QTY, NEW_QTY, STOCK_DELTA, BAL_AFTER, REASON, USR_ID)
                            VALUES (@id, @a, @t, @s, @p, @o, @n, @d, @b, @r, @u)", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@id", txId);
                            cmd.Parameters.AddWithValue("@a", newQty.HasValue ? "EDIT_QTY" : "CANCEL");
                            cmd.Parameters.AddWithValue("@t", type);
                            cmd.Parameters.Add("@s", SqlDbType.Int).Value = (object)stkId ?? DBNull.Value;
                            cmd.Parameters.AddWithValue("@p", ptId);
                            cmd.Parameters.AddWithValue("@o", oldQty);
                            cmd.Parameters.Add("@n", SqlDbType.Int).Value = newQty.HasValue ? (object)newQty.Value : 0;
                            cmd.Parameters.AddWithValue("@d", delta);
                            cmd.Parameters.AddWithValue("@b", balAfter);
                            cmd.Parameters.AddWithValue("@r", reason.Trim() + (sourceNote != null ? $" | {sourceNote}" : ""));
                            cmd.Parameters.AddWithValue("@u", userId ?? "");
                            cmd.ExecuteNonQuery();
                        }

                        // 6) PR อัตโนมัติจากการสแกนออก - ไม่แตะ ให้ผู้มีสิทธิ์ PR จัดการเอง (แจ้งเลข PR กลับไป)
                        var result = new ScanAdjustResult { TxId = txId, BalanceAfter = balAfter, StockCode = stkCode, SourceNote = sourceNote };
                        using (var cmd = new SqlCommand("SELECT TOP 1 PR_NO, PR_STAT FROM TRN_PR_H WHERE SCAN_TX_ID = @id ORDER BY PR_NO DESC", conn, trans))
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
