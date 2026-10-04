using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using CIMS.Core;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace CIMS.Services
{
    // 🕘 ประวัติการสแกน (TRN_SCAN) สำหรับหน้าต่าง HISTORY ในหน้า Multi-Scanner
    public class ScanHistoryRow
    {
        public DateTime TxDate { get; set; }
        public string StockCode { get; set; }
        public string TxType { get; set; }
        public string PartCode { get; set; }
        public string PartName { get; set; }
        public int Qty { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string RefNo { get; set; }
        public string DateText => TxDate.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        public string UserText => string.IsNullOrWhiteSpace(UserName) ? UserId : $"{UserId} - {UserName}";
    }

    public class ScanHistoryFilter
    {
        public string Keyword { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }      // รวมทั้งวัน
        public int? StkId { get; set; }       // null = ทุกคลัง
        public bool StockIsMain { get; set; } // แถวเก่าที่ไม่มี STK_ID นับเป็นของคลังหลัก
        public string TxType { get; set; }    // null = ทุกประเภท
    }

    public class ScanHistoryService
    {
        public const int PageSize = 50;

        // WHERE + ลำดับความใกล้เคียง: ตรงเป๊ะ > รหัสขึ้นต้นด้วยคำค้น > ชื่อขึ้นต้นด้วยคำค้น > มีคำค้นอยู่ข้างใน แล้วค่อยใหม่ -> เก่า
        private const string FromWhere = @"
            FROM TRN_SCAN t
            LEFT JOIN MST_PART p  ON p.PT_ID = t.PT_ID
            LEFT JOIN MST_STOCK s ON s.STK_ID = t.STK_ID
            LEFT JOIN MST_USER u  ON u.USR_ID = t.USR_ID
            WHERE t.TX_DATE >= @from AND t.TX_DATE < @to
              AND (@stk IS NULL OR t.STK_ID = @stk OR (@isMain = 1 AND t.STK_ID IS NULL))
              AND (@type IS NULL OR t.TX_TYPE = @type)
              AND (@key = '' OR p.PT_CODE LIKE '%' + @key + '%' OR p.PT_DESC LIKE '%' + @key + '%'
                   OR ISNULL(p.PT_PARTA, '') LIKE '%' + @key + '%' OR ISNULL(p.PT_PARTNO, '') LIKE '%' + @key + '%'
                   OR ISNULL(p.PT_QR, '') LIKE '%' + @key + '%' OR ISNULL(t.PT_ACODE, '') LIKE '%' + @key + '%'
                   OR ISNULL(t.REF_NO, '') LIKE '%' + @key + '%' OR t.USR_ID LIKE '%' + @key + '%' OR ISNULL(u.USR_NAME, '') LIKE '%' + @key + '%')";

        private const string Rank = @"
            CASE WHEN @key = '' THEN 0
                 WHEN p.PT_CODE = @key OR ISNULL(p.PT_PARTA, '') = @key OR ISNULL(p.PT_PARTNO, '') = @key OR ISNULL(p.PT_QR, '') = @key THEN 0
                 WHEN p.PT_CODE LIKE @key + '%' OR ISNULL(p.PT_PARTA, '') LIKE @key + '%' OR ISNULL(p.PT_PARTNO, '') LIKE @key + '%' THEN 1
                 WHEN p.PT_DESC LIKE @key + '%' THEN 2
                 ELSE 3 END";

        private static void AddParams(SqlCommand cmd, ScanHistoryFilter f)
        {
            cmd.Parameters.Add("@from", SqlDbType.DateTime).Value = f.From.Date;
            cmd.Parameters.Add("@to", SqlDbType.DateTime).Value = f.To.Date.AddDays(1);
            cmd.Parameters.Add("@stk", SqlDbType.Int).Value = (object)f.StkId ?? DBNull.Value;
            cmd.Parameters.Add("@isMain", SqlDbType.Bit).Value = f.StockIsMain;
            cmd.Parameters.Add("@type", SqlDbType.VarChar, 20).Value = (object)f.TxType ?? DBNull.Value;
            cmd.Parameters.Add("@key", SqlDbType.NVarChar, 200).Value = (f.Keyword ?? "").Trim();
        }

        // 1 หน้า (50 รายการ) + จำนวนทั้งหมด - ยกเลิกได้ (พิมพ์ค้นหาต่อเนื่องจะยกเลิกคำค้นเก่าไม่ให้ค้าง)
        public async Task<(List<ScanHistoryRow> Rows, int Total)> QueryAsync(ScanHistoryFilter f, int page, CancellationToken ct)
        {
            var rows = new List<ScanHistoryRow>();
            int total = 0;
            using (var conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                await conn.OpenAsync(ct).ConfigureAwait(false);
                using (var cmd = new SqlCommand("SELECT COUNT(*) " + FromWhere, conn))
                {
                    AddParams(cmd, f);
                    total = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false));
                }
                using (var cmd = new SqlCommand($@"
                    SELECT t.TX_DATE, ISNULL(s.STK_CODE, CASE WHEN t.STK_ID IS NULL THEN 'MAIN' ELSE CAST(t.STK_ID AS varchar) END) AS STK_CODE,
                           CASE WHEN ISNULL(t.IS_CANCEL, 0) = 1 THEN t.TX_TYPE + ' (CANCELLED)' ELSE t.TX_TYPE END AS TX_TYPE, ISNULL(p.PT_CODE, ISNULL(t.PT_ACODE, '')) AS PT_CODE, ISNULL(p.PT_DESC, '') AS PT_DESC,
                           t.TX_QTY, t.USR_ID, ISNULL(u.USR_NAME, '') AS USR_NAME, ISNULL(t.REF_NO, '') AS REF_NO
                    {FromWhere}
                    ORDER BY {Rank}, t.TX_DATE DESC, t.TX_ID DESC
                    OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", conn))
                {
                    AddParams(cmd, f);
                    cmd.Parameters.AddWithValue("@skip", Math.Max(0, page) * PageSize);
                    cmd.Parameters.AddWithValue("@take", PageSize);
                    using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                        while (await r.ReadAsync(ct).ConfigureAwait(false)) rows.Add(Read(r));
                }
            }
            return (rows, total);
        }

        private static ScanHistoryRow Read(SqlDataReader r) => new ScanHistoryRow
        {
            TxDate = Convert.ToDateTime(r["TX_DATE"]),
            StockCode = r["STK_CODE"].ToString(),
            TxType = r["TX_TYPE"].ToString(),
            PartCode = r["PT_CODE"].ToString(),
            PartName = r["PT_DESC"].ToString(),
            Qty = r["TX_QTY"] == DBNull.Value ? 0 : Convert.ToInt32(r["TX_QTY"]),
            UserId = r["USR_ID"].ToString(),
            UserName = r["USR_NAME"].ToString(),
            RefNo = r["REF_NO"].ToString()
        };

        // 📤 Export ทุกรายการตามตัวกรอง (ไม่จำกัดแค่หน้าที่แสดง)
        public int Export(ScanHistoryFilter f, string path)
        {
            var rows = new List<ScanHistoryRow>();
            using (var conn = new SqlConnection(GlobalConfig.ConnStr))
            using (var cmd = new SqlCommand($@"
                SELECT t.TX_DATE, ISNULL(s.STK_CODE, CASE WHEN t.STK_ID IS NULL THEN 'MAIN' ELSE CAST(t.STK_ID AS varchar) END) AS STK_CODE,
                       CASE WHEN ISNULL(t.IS_CANCEL, 0) = 1 THEN t.TX_TYPE + ' (CANCELLED)' ELSE t.TX_TYPE END AS TX_TYPE, ISNULL(p.PT_CODE, ISNULL(t.PT_ACODE, '')) AS PT_CODE, ISNULL(p.PT_DESC, '') AS PT_DESC,
                       t.TX_QTY, t.USR_ID, ISNULL(u.USR_NAME, '') AS USR_NAME, ISNULL(t.REF_NO, '') AS REF_NO
                {FromWhere}
                ORDER BY {Rank}, t.TX_DATE DESC, t.TX_ID DESC", conn) { CommandTimeout = 300 })
            {
                AddParams(cmd, f);
                conn.Open();
                using (var r = cmd.ExecuteReader()) while (r.Read()) rows.Add(Read(r));
            }

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Scan History");
                string[] head = { "DATE TIME", "STOCK", "TYPE", "PRODUCT CODE", "PRODUCT NAME", "QTY", "USER", "BARCODE" };
                for (int c = 0; c < head.Length; c++) ws.Cell(1, c + 1).Value = head[c];
                ws.Range(1, 1, 1, head.Length).Style.Font.Bold = true;
                int i = 2;
                foreach (var x in rows)
                {
                    ws.Cell(i, 1).Value = x.TxDate; ws.Cell(i, 1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                    ws.Cell(i, 2).Value = x.StockCode;
                    ws.Cell(i, 3).Value = x.TxType;
                    ws.Cell(i, 4).Value = x.PartCode;
                    ws.Cell(i, 5).Value = x.PartName;
                    ws.Cell(i, 6).Value = x.Qty;
                    ws.Cell(i, 7).Value = x.UserText;
                    ws.Cell(i, 8).Value = x.RefNo;
                    i++;
                }
                ws.Columns(1, head.Length - 1).AdjustToContents();
                ws.Column(head.Length).Width = 60;
                wb.SaveAs(path);
            }
            return rows.Count;
        }
    }
}
