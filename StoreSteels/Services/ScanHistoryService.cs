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
    // 🕘 ประวัติการสแกน (CIMS.ScanTransactions) สำหรับหน้าต่าง HISTORY ในหน้า Multi-Scanner
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
        public int StkId { get; set; }
        public string PartA { get; set; }
        public string PartNumber { get; set; }
        public string Model { get; set; }
        // ค่าที่ตั้งให้แสดงแทนรหัสของคลังนั้น (ปุ่ม DISPLAY ในหน้า Multi-Scanner) - ไม่ได้ตั้ง = PRODUCT CODE
        public string DisplayCode { get; set; }
        public string CodeText => string.IsNullOrWhiteSpace(DisplayCode) ? PartCode : DisplayCode;
        public string DateText => TxDate.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        public string UserText => string.IsNullOrWhiteSpace(UserName) ? UserId : $"{UserId} - {UserName}";
    }

    public class ScanHistoryFilter
    {
        public string Keyword { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }      // รวมทั้งวัน
        public int? StkId { get; set; }       // null = ทุกคลัง
        public bool StockIsMain { get; set; } // แถวเก่าที่ไม่มี StockID นับเป็นของคลังหลัก
        public string TxType { get; set; }    // null = ทุกประเภท
    }

    public class ScanHistoryService
    {
        public const int PageSize = 50;

        // WHERE + ลำดับความใกล้เคียง: ตรงเป๊ะ > รหัสขึ้นต้นด้วยคำค้น > ชื่อขึ้นต้นด้วยคำค้น > มีคำค้นอยู่ข้างใน แล้วค่อยใหม่ -> เก่า
        private const string FromWhere = @"
            FROM CIMS.ScanTransactions t
            LEFT JOIN CIMS.Parts p  ON p.PartID = t.PartID
            LEFT JOIN CIMS.Stocks s ON s.StockID = t.StockID
            LEFT JOIN CIMS.Users u  ON u.UserID = t.UserID
            WHERE t.TransactionDate >= @from AND t.TransactionDate < @to
              AND (@stk IS NULL OR t.StockID = @stk OR (@isMain = 1 AND t.StockID IS NULL))
              AND (@type IS NULL OR t.TransactionType = @type)
              AND (@key = '' OR p.PartCode LIKE '%' + @key + '%' OR p.Description LIKE '%' + @key + '%'
                   OR ISNULL(p.PartA, '') LIKE '%' + @key + '%' OR ISNULL(p.PartNumber, '') LIKE '%' + @key + '%'
                   OR ISNULL(p.QRCode, '') LIKE '%' + @key + '%' OR ISNULL(t.PartACode, '') LIKE '%' + @key + '%'
                   OR ISNULL(t.ReferenceNo, '') LIKE '%' + @key + '%' OR t.UserID LIKE '%' + @key + '%' OR ISNULL(u.FullName, '') LIKE '%' + @key + '%')";

        private const string Rank = @"
            CASE WHEN @key = '' THEN 0
                 WHEN p.PartCode = @key OR ISNULL(p.PartA, '') = @key OR ISNULL(p.PartNumber, '') = @key OR ISNULL(p.QRCode, '') = @key THEN 0
                 WHEN p.PartCode LIKE @key + '%' OR ISNULL(p.PartA, '') LIKE @key + '%' OR ISNULL(p.PartNumber, '') LIKE @key + '%' THEN 1
                 WHEN p.Description LIKE @key + '%' THEN 2
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
                    SELECT t.TransactionDate, ISNULL(s.StockCode, CASE WHEN t.StockID IS NULL THEN 'MAIN' ELSE CAST(t.StockID AS varchar) END) AS StockCode,
                           CASE WHEN ISNULL(t.IsCancelled, 0) = 1 THEN t.TransactionType + ' (CANCELLED)' ELSE t.TransactionType END AS TransactionType, ISNULL(p.PartCode, ISNULL(t.PartACode, '')) AS PartCode, ISNULL(p.Description, '') AS Description,
                           t.Quantity, t.UserID, ISNULL(u.FullName, '') AS FullName, ISNULL(t.ReferenceNo, '') AS ReferenceNo,
                           ISNULL(t.StockID, 0) AS StockID, ISNULL(p.PartA, '') AS PartA, ISNULL(p.PartNumber, '') AS PartNumber, ISNULL(p.Model, '') AS Model
                    {FromWhere}
                    ORDER BY {Rank}, t.TransactionDate DESC, t.TransactionID DESC
                    OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", conn))
                {
                    AddParams(cmd, f);
                    cmd.Parameters.AddWithValue("@skip", Math.Max(0, page) * PageSize);
                    cmd.Parameters.AddWithValue("@take", PageSize);
                    using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                        while (await r.ReadAsync(ct).ConfigureAwait(false)) rows.Add(Read(r));
                }
            }
            ApplyDisplay(rows);
            return (rows, total);
        }

        // ค่าที่แสดงแทนรหัสตามการตั้งค่าของแต่ละคลัง
        private static void ApplyDisplay(List<ScanHistoryRow> rows)
        {
            try
            {
                var map = new ScanService().GetScanDisplayMap();
                if (map.Count == 0) return;
                var formats = map.Values.Any(v => v.StartsWith("FIELD:")) ? new StockService().GetFormats() : new List<CIMS.Models.BarcodeFormatModel>();
                foreach (var x in rows)
                    if (map.TryGetValue(x.StkId, out string key))
                        x.DisplayCode = CIMS.Helpers.ScanDisplay.Resolve(key, x.PartCode, x.PartA, x.PartNumber, x.Model, x.PartName, x.RefNo, formats);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"History display: {ex.Message}"); }
        }

        private static ScanHistoryRow Read(SqlDataReader r) => new ScanHistoryRow
        {
            TxDate = Convert.ToDateTime(r["TransactionDate"]),
            StockCode = r["StockCode"].ToString(),
            TxType = r["TransactionType"].ToString(),
            PartCode = r["PartCode"].ToString(),
            PartName = r["Description"].ToString(),
            Qty = r["Quantity"] == DBNull.Value ? 0 : Convert.ToInt32(r["Quantity"]),
            UserId = r["UserID"].ToString(),
            UserName = r["FullName"].ToString(),
            RefNo = r["ReferenceNo"].ToString(),
            StkId = Convert.ToInt32(r["StockID"]),
            PartA = r["PartA"].ToString(),
            PartNumber = r["PartNumber"].ToString(),
            Model = r["Model"].ToString()
        };

        // 📤 Export ทุกรายการตามตัวกรอง (ไม่จำกัดแค่หน้าที่แสดง)
        public int Export(ScanHistoryFilter f, string path)
        {
            var rows = new List<ScanHistoryRow>();
            using (var conn = new SqlConnection(GlobalConfig.ConnStr))
            using (var cmd = new SqlCommand($@"
                SELECT t.TransactionDate, ISNULL(s.StockCode, CASE WHEN t.StockID IS NULL THEN 'MAIN' ELSE CAST(t.StockID AS varchar) END) AS StockCode,
                       CASE WHEN ISNULL(t.IsCancelled, 0) = 1 THEN t.TransactionType + ' (CANCELLED)' ELSE t.TransactionType END AS TransactionType, ISNULL(p.PartCode, ISNULL(t.PartACode, '')) AS PartCode, ISNULL(p.Description, '') AS Description,
                       t.Quantity, t.UserID, ISNULL(u.FullName, '') AS FullName, ISNULL(t.ReferenceNo, '') AS ReferenceNo,
                           ISNULL(t.StockID, 0) AS StockID, ISNULL(p.PartA, '') AS PartA, ISNULL(p.PartNumber, '') AS PartNumber, ISNULL(p.Model, '') AS Model
                {FromWhere}
                ORDER BY {Rank}, t.TransactionDate DESC, t.TransactionID DESC", conn) { CommandTimeout = 300 })
            {
                AddParams(cmd, f);
                conn.Open();
                using (var r = cmd.ExecuteReader()) while (r.Read()) rows.Add(Read(r));
            }
            ApplyDisplay(rows);

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
                    ws.Cell(i, 4).Value = x.CodeText;
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
