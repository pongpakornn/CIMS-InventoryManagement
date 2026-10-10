using CIMS.Core;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CIMS.Services
{
    // 📋 NOT DEDUCTED: ป้ายที่แสกนรับเข้าแล้ว แต่ตัดคลังต้นทาง (Coil แม่ใน STOCK-PANTA) ไม่ได้ / ได้ไม่ครบ
    //    ระบบเก็บให้เองตอนแสกน (CIMS.DeductMisses - Update_20261010b.sql) / รายการสแกนที่ถูกยกเลิก (ADJUST SCAN) ไม่แสดง
    public class DeductMissRow : INotifyPropertyChanged
    {
        public int MissId { get; set; }
        public int? TxId { get; set; }
        public DateTime ScanDate { get; set; }
        public string CoilNo { get; set; }
        public string MotherCoil { get; set; }
        public string PartCode { get; set; }
        public string PartName { get; set; }
        public string StockCode { get; set; }
        public string SourceCode { get; set; }
        public decimal LabelQty { get; set; }
        public decimal Deducted { get; set; }
        public string Reason { get; set; }
        public string UserId { get; set; }
        public string Barcode { get; set; }
        public decimal? MotherNow { get; set; }   // Coil แม่ในคลังต้นทางตอนนี้ (IN) - null = ยังไม่มี

        public decimal Missing => LabelQty - Deducted;
        public string DateText => ScanDate.ToString("dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        public string LabelQtyText => CIMS.Helpers.Qty.Plain(LabelQty);
        public string DeductedText => CIMS.Helpers.Qty.Plain(Deducted);
        public string MissingText => CIMS.Helpers.Qty.Plain(Missing);
        public string ReasonText => Reason == "SHORT" ? "MOTHER COIL SHORT" : "NO MOTHER COIL";
        public string MotherNowText => MotherNow.HasValue ? $"IN {CIMS.Helpers.Qty.Plain(MotherNow.Value)} KG" : "NOT IN " + SourceCode;
        public string StockText => $"{StockCode} ← {SourceCode}";

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class DeductMissService
    {
        private readonly string _cs = GlobalConfig.ConnStr;

        private const string Visible = "ISNULL(t.IsCancelled, 0) = 0";

        // จำนวนรายการทั้งหมด (ปุ่ม NOT DEDUCTED (n) หน้า Multi-Scanner)
        public int Count()
        {
            if (!CIMS.Helpers.DbSchema.HasDeductMisses) return 0;
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand($"SELECT COUNT(*) FROM CIMS.DeductMisses m LEFT JOIN CIMS.ScanTransactions t ON t.TransactionID = m.TransactionID WHERE {Visible}", conn))
            {
                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public List<DeductMissRow> GetRows(string keyword, DateTime from, DateTime to)
        {
            var list = new List<DeductMissRow>();
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand($@"
                SELECT m.MissID, m.TransactionID, m.ScanDate, m.CoilNo, m.MotherCoil, m.LabelQty, m.Deducted, m.Reason, m.UserID, m.Barcode,
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
                            LabelQty = CIMS.Helpers.Qty.Read(r["LabelQty"]),
                            Deducted = CIMS.Helpers.Qty.Read(r["Deducted"]),
                            Reason = r["Reason"].ToString(),
                            UserId = r["UserID"]?.ToString() ?? "",
                            Barcode = r["Barcode"]?.ToString() ?? "",
                            PartCode = r["PartCode"]?.ToString() ?? "",
                            PartName = r["Description"]?.ToString() ?? "",
                            StockCode = r["StockCode"]?.ToString() ?? "",
                            SourceCode = r["SourceCode"]?.ToString() ?? "",
                            MotherNow = r["MotherNow"] == DBNull.Value ? (decimal?)null : CIMS.Helpers.Qty.Read(r["MotherNow"])
                        });
            }
            return list;
        }

        // 🗑 ลบออกจากรายการ (ไม่กระทบยอดสต็อก / รายการสแกน)
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

        // 📤 Excel (รูปแบบเดียวกับไฟล์ Export ทั้งระบบ) -> path ของไฟล์
        public string Export(IList<DeductMissRow> rows)
        {
            string path = ImportTemplateService.NewPath("NOT_DEDUCTED");
            System.IO.Directory.CreateDirectory(ImportTemplateService.ExportFolder);
            using (var wb = new ClosedXML.Excel.XLWorkbook())
            {
                var ws = wb.Worksheets.Add("NOT DEDUCTED");
                string[] heads = { "NO", "SCAN DATE", "COIL NO", "MOTHER COIL", "PD CODE", "PRODUCT NAME", "STOCK", "LABEL QTY (KG)", "DEDUCTED (KG)", "NOT DEDUCTED (KG)", "REASON", "MOTHER COIL NOW", "USER", "BARCODE" };
                ImportTemplateService.Header(ws, heads, new double[] { 6.4, 19, 18, 15, 18, 34, 26, 14, 14, 16, 24, 20, 10, 70 });
                int row = 2;
                foreach (var x in rows)
                {
                    ws.Cell(row, 1).Value = row - 1;
                    ws.Cell(row, 2).Value = x.ScanDate;
                    ws.Cell(row, 2).Style.DateFormat.Format = "dd/MM/yyyy HH:mm:ss";
                    ws.Cell(row, 3).Value = x.CoilNo;
                    ws.Cell(row, 4).Value = x.MotherCoil;
                    ws.Cell(row, 5).Value = x.PartCode;
                    ws.Cell(row, 6).Value = x.PartName;
                    ws.Cell(row, 7).Value = x.StockText;
                    ws.Cell(row, 8).Value = x.LabelQty;
                    ws.Cell(row, 9).Value = x.Deducted;
                    ws.Cell(row, 10).Value = x.Missing;
                    ws.Cell(row, 11).Value = x.ReasonText;
                    ws.Cell(row, 12).Value = x.MotherNowText;
                    ws.Cell(row, 13).Value = x.UserId;
                    ws.Cell(row, 14).Value = x.Barcode;
                    row++;
                }
                ImportTemplateService.Body(ws, row - 1, heads.Length);
                ImportTemplateService.LeftAlign(ws, row - 1, 6, 14);
                if (row > 2) ws.Range(2, 8, row - 1, 10).Style.NumberFormat.Format = "#,##0.00#";
                wb.SaveAs(path);
            }
            return path;
        }
    }
}
