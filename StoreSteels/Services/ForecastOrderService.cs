using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using CIMS.Core;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CIMS.Services
{
    // 1 แถวในตาราง Forecast / Order / Delivery (ต่อลูกค้า + สินค้า + เดือน)
    public class ForecastOrderRow
    {
        public int No { get; set; }                 // ลำดับในกลุ่มลูกค้า
        public string Customer { get; set; }
        public string PartA { get; set; }
        public string PartNo { get; set; }
        public string ProductName { get; set; }
        public decimal Forecast { get; set; }
        public decimal Order { get; set; }
        public decimal Delivery { get; set; }
        public string GroupKey => string.IsNullOrWhiteSpace(Customer) ? "-" : Customer;
    }

    // หลอด TOP 5 (สินค้าที่ลูกค้าเรียก Order มากที่สุดในเดือนนั้น)
    public class ForecastTopItem
    {
        public int Rank { get; set; }
        public string Title { get; set; }          // PART NO / ชื่อสินค้า
        public string Sub { get; set; }            // ลูกค้า • ชื่อสินค้า
        public decimal Order { get; set; }
        public decimal Forecast { get; set; }
        public double Percent { get; set; }        // เทียบกับอันดับ 1 (ความยาวหลอด)
        public double ShareOfMonth { get; set; }   // % ของ Order ทั้งเดือน
        public string OrderText => Order.ToString("N0", CultureInfo.InvariantCulture);
        public string ShareText => ShareOfMonth.ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    public class ForecastMonthTotal
    {
        public int Month { get; set; }
        public double Forecast { get; set; }
        public double Order { get; set; }
        public double Delivery { get; set; }
    }

    // 📊 หน้า Forecast / Order / Delivery: อ่านจาก CIMS.ForecastOrderImports (นำเข้าจาก IMPORT FORECAST & ORDER)
    //   สินค้าจับคู่กับ CIMS.Parts ด้วย ลูกค้าเดียวกัน + PART NO ตรงกับ PartA / PartNumber / PartCode (แบบเดียวกับการคำนวณ Max-Min)
    public class ForecastOrderService
    {
        private readonly string _cs = GlobalConfig.ConnStr;

        // แม่แบบ Export บน Shared Folder (หัวตาราง / สี / ความกว้างตามแม่แบบ)
        public const string TemplatePath = @"\\192.168.10.56\ProgramCHR\2. Store Only\1. CIMS - Inventory Management\0. Excel Template\1. Export\2. ForecastOrder.xlsx";

        // เดือนที่มีข้อมูล (ใหม่ -> เก่า)
        public List<DateTime> GetMonths()
        {
            var list = new List<DateTime>();
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"SELECT DISTINCT DATEFROMPARTS(YEAR(TargetDate), MONTH(TargetDate), 1) AS M
                                              FROM CIMS.ForecastOrderImports WHERE TargetDate IS NOT NULL ORDER BY M DESC", conn))
            {
                conn.Open();
                using (var r = cmd.ExecuteReader()) while (r.Read()) list.Add(r.GetDateTime(0));
            }
            return list;
        }

        // กราฟแท่งทั้งปี (Jan - Dec)
        public List<ForecastMonthTotal> GetYear(int year)
        {
            var list = new List<ForecastMonthTotal>();
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"SELECT MONTH(TargetDate), SUM(ISNULL(ForecastQuantity, 0)), SUM(ISNULL(OrderQuantity, 0)), SUM(ISNULL(DeliveryQuantity, 0))
                                              FROM CIMS.ForecastOrderImports
                                              WHERE TargetDate >= @from AND TargetDate < @to
                                              GROUP BY MONTH(TargetDate)", conn))
            {
                cmd.Parameters.Add("@from", SqlDbType.Date).Value = new DateTime(year, 1, 1);
                cmd.Parameters.Add("@to", SqlDbType.Date).Value = new DateTime(year + 1, 1, 1);
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new ForecastMonthTotal { Month = r.GetInt32(0), Forecast = Convert.ToDouble(r[1]), Order = Convert.ToDouble(r[2]), Delivery = Convert.ToDouble(r[3]) });
            }
            return list;
        }

        // แถวรวมต่อ ลูกค้า + สินค้า ของเดือน (ค้นหาได้) - จับคู่ข้อมูลสินค้าจาก CIMS.Parts
        private const string RowsSql = @"
            WITH agg AS (
                SELECT LTRIM(RTRIM(ISNULL(CustomerCode, ''))) AS Customer, LTRIM(RTRIM(ISNULL(PartACode, ''))) AS PartKey,
                       SUM(ISNULL(ForecastQuantity, 0)) AS F, SUM(ISNULL(OrderQuantity, 0)) AS O, SUM(ISNULL(DeliveryQuantity, 0)) AS D
                FROM CIMS.ForecastOrderImports
                WHERE TargetDate >= @from AND TargetDate < @to
                GROUP BY LTRIM(RTRIM(ISNULL(CustomerCode, ''))), LTRIM(RTRIM(ISNULL(PartACode, '')))
            )
            SELECT a.Customer, ISNULL(p.PartA, '') AS PartA, a.PartKey AS PartNo, ISNULL(p.Description, '') AS ProductName, a.F, a.O, a.D
            FROM agg a
            OUTER APPLY (SELECT TOP 1 x.PartA, x.Description FROM CIMS.Parts x
                         WHERE a.PartKey <> '' AND LTRIM(RTRIM(ISNULL(x.Customer, ''))) = a.Customer
                           AND a.PartKey IN (LTRIM(RTRIM(ISNULL(x.PartA, ''))), LTRIM(RTRIM(ISNULL(x.PartNumber, ''))), LTRIM(RTRIM(x.PartCode)))
                         ORDER BY x.PartID) p
            WHERE @key = '' OR a.Customer LIKE '%' + @key + '%' OR a.PartKey LIKE '%' + @key + '%'
               OR ISNULL(p.PartA, '') LIKE '%' + @key + '%' OR ISNULL(p.Description, '') LIKE '%' + @key + '%'";

        public List<ForecastOrderRow> GetRows(DateTime month, string keyword, int skip, int take)
        {
            var list = new List<ForecastOrderRow>();
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(RowsSql + " ORDER BY a.Customer, a.PartKey OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY OPTION (MAX_GRANT_PERCENT = 5)", conn))
            {
                AddMonth(cmd, month, keyword);
                cmd.Parameters.AddWithValue("@skip", skip);
                cmd.Parameters.AddWithValue("@take", take);
                conn.Open();
                using (var r = cmd.ExecuteReader()) while (r.Read()) list.Add(Read(r));
            }
            return list;
        }

        // TOP 5 Order ของเดือน + เปอร์เซ็นต์
        public List<ForecastTopItem> GetTop5(DateTime month)
        {
            var rows = new List<ForecastOrderRow>();
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(RowsSql + " ORDER BY a.O DESC, a.F DESC", conn))
            {
                AddMonth(cmd, month, "");
                conn.Open();
                using (var r = cmd.ExecuteReader()) while (r.Read()) rows.Add(Read(r));
            }
            decimal total = rows.Sum(x => x.Order);
            var top = rows.Where(x => x.Order > 0).Take(5).ToList();
            decimal first = top.Count > 0 ? top[0].Order : 0;
            return top.Select((x, i) => new ForecastTopItem
            {
                Rank = i + 1,
                Title = string.IsNullOrWhiteSpace(x.PartNo) ? x.Customer : x.PartNo,
                Sub = string.Join("  •  ", new[] { x.Customer, x.ProductName }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Order = x.Order,
                Forecast = x.Forecast,
                Percent = first > 0 ? (double)(x.Order / first * 100m) : 0,
                ShareOfMonth = total > 0 ? (double)(x.Order / total * 100m) : 0
            }).ToList();
        }

        private static void AddMonth(SqlCommand cmd, DateTime month, string keyword)
        {
            var m = new DateTime(month.Year, month.Month, 1);
            cmd.Parameters.Add("@from", SqlDbType.Date).Value = m;
            cmd.Parameters.Add("@to", SqlDbType.Date).Value = m.AddMonths(1);
            cmd.Parameters.Add("@key", SqlDbType.NVarChar, 200).Value = (keyword ?? "").Trim();
        }

        private static ForecastOrderRow Read(SqlDataReader r) => new ForecastOrderRow
        {
            Customer = r.GetString(0),
            PartA = r.GetString(1),
            PartNo = r.GetString(2),
            ProductName = r.GetString(3),
            Forecast = Convert.ToDecimal(r[4]),
            Order = Convert.ToDecimal(r[5]),
            Delivery = Convert.ToDecimal(r[6])
        };

        // 📤 Export ทุกรายการของเดือน (ตามคำค้นหา) -> Desktop\CIMS_Export\ForecastOrder_yyyy-MM_xxx.xlsx
        //   ใช้แม่แบบบน Shared Folder: หาคอลัมน์จากหัวตาราง (NO / PART A / PART NO / PRODUCT NAME / FORECAST / ORDER / DELIVERY)
        //   แม่แบบไม่มีคอลัมน์ครบ -> สร้างหัวตาราง 7 คอลัมน์ใหม่ด้วยรูปแบบเดียวกับแม่แบบ
        //   ข้อมูลเกินแถวในแม่แบบ -> สร้างแถวเพิ่มด้วยรูปแบบแถวแรกของแม่แบบ / ค่าเป็น Value จัดกึ่งกลาง / คั่นกลุ่มด้วยแถวชื่อลูกค้า
        public string Export(DateTime month, string keyword, out int count)
        {
            var rows = GetRows(month, keyword, 0, int.MaxValue / 2);
            count = rows.Count;
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "CIMS_Export");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "ForecastOrder_" + month.ToString("yyyy-MM", CultureInfo.InvariantCulture) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".xlsx");

            string[] heads = { "NO", "PART A", "PART NO", "PRODUCT NAME", "FORECAST", "ORDER", "DELIVERY" };
            XLWorkbook wb = null;
            try
            {
                if (File.Exists(TemplatePath))
                {
                    var ms = new MemoryStream();
                    using (var fs = new FileStream(TemplatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) fs.CopyTo(ms);
                    ms.Position = 0;
                    wb = new XLWorkbook(ms);
                }
            }
            catch { wb = null; }

            IXLWorksheet ws, original = null;
            int headerRow = 1;
            var col = new Dictionary<string, int>();
            IXLStyle headStyle = null, cellStyle = null;
            double headHeight = 34.5, rowHeight = 20.25;
            if (wb != null)
            {
                ws = wb.Worksheet(1);
                // หัวตาราง = แถวแรกที่มีคำว่า NO
                foreach (var row in ws.RowsUsed().Take(10))
                {
                    var map = new Dictionary<string, int>();
                    foreach (var c in row.CellsUsed())
                    {
                        string h = Norm(c.GetString());
                        foreach (var want in heads) if (!map.ContainsKey(want) && Norm(want) == h) map[want] = c.Address.ColumnNumber;
                    }
                    if (map.ContainsKey("NO")) { headerRow = row.RowNumber(); col = map; break; }
                }
                var first = ws.Cell(headerRow, col.TryGetValue("NO", out int nc) ? nc : 1);
                headStyle = first.Style; headHeight = ws.Row(headerRow).Height;
                cellStyle = ws.Cell(headerRow + 1, first.Address.ColumnNumber).Style;
                if (ws.Row(headerRow + 1).Height > 0) rowHeight = ws.Row(headerRow + 1).Height;

                if (heads.Any(h => !col.ContainsKey(h)))
                {
                    // แม่แบบยังไม่ใช่รูปแบบ Forecast -> สร้างชีทใหม่ 7 คอลัมน์ด้วยรูปแบบของแม่แบบ
                    var hs = headStyle; var cs = cellStyle;
                    var nws = wb.AddWorksheet("FORECAST ORDER");
                    for (int i = 0; i < heads.Length; i++)
                    {
                        var c = nws.Cell(1, i + 1); c.Value = heads[i]; c.Style = hs;
                        col[heads[i]] = i + 1;
                    }
                    original = wb.Worksheet(1);   // ลบชีทแม่แบบเดิมตอนท้าย (รูปแบบที่ยืมมายังอ้างอิงชีทนี้อยู่)
                    ws = nws; headerRow = 1; cellStyle = cs;
                    ws.Row(1).Height = headHeight;
                    double[] widths = { 6.4, 18, 24, 44.4, 14.9, 14.9, 14.9 };
                    for (int i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];
                }
                else
                {
                    // ล้างแถวตัวอย่างในแม่แบบ (เก็บรูปแบบไว้)
                    int lastUsed = ws.LastRowUsed()?.RowNumber() ?? headerRow;
                    if (lastUsed > headerRow) ws.Range(headerRow + 1, 1, lastUsed, ws.LastColumnUsed().ColumnNumber()).Clear(XLClearOptions.Contents);
                }
            }
            else
            {
                wb = new XLWorkbook();
                ws = wb.AddWorksheet("FORECAST ORDER");
                for (int i = 0; i < heads.Length; i++)
                {
                    var c = ws.Cell(1, i + 1); c.Value = heads[i];
                    c.Style.Fill.BackgroundColor = XLColor.FromHtml("#002060");
                    c.Style.Font.FontColor = XLColor.White; c.Style.Font.Bold = true;
                    c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    c.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    col[heads[i]] = i + 1;
                }
                ws.Row(1).Height = headHeight;
                double[] widths = { 6.4, 18, 24, 44.4, 14.9, 14.9, 14.9 };
                for (int i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];
            }

            int firstCol = col.Values.Min(), lastCol = col.Values.Max();
            int rowNo = headerRow + 1;
            foreach (var g in rows.GroupBy(x => x.GroupKey))
            {
                // แถวชื่อลูกค้า (คั่นกลุ่มเหมือนตารางในโปรแกรม)
                var grp = ws.Range(rowNo, firstCol, rowNo, lastCol);
                grp.Merge();
                grp.FirstCell().Value = $"CUSTOMER : {g.Key}   ({g.Count():N0} ITEMS)";
                grp.Style.Font.Bold = true;
                grp.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E1F2");
                grp.Style.Font.FontColor = XLColor.FromHtml("#002060");
                grp.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                grp.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                grp.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Row(rowNo).Height = rowHeight;
                rowNo++;

                int n = 0;
                foreach (var x in g)
                {
                    n++;
                    void Put(string head, XLCellValue v, bool number)
                    {
                        if (!col.TryGetValue(head, out int c)) return;
                        var cell = ws.Cell(rowNo, c);
                        if (cellStyle != null) cell.Style = cellStyle;
                        cell.Value = v;
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        cell.Style.Font.Bold = head == "NO";
                        if (number) cell.Style.NumberFormat.Format = v.IsNumber && v.GetNumber() % 1 != 0 ? "#,##0.00" : "#,##0";
                    }
                    Put("NO", n, false);
                    Put("PART A", x.PartA, false);
                    Put("PART NO", x.PartNo, false);
                    Put("PRODUCT NAME", x.ProductName, false);
                    Put("FORECAST", x.Forecast, true);
                    Put("ORDER", x.Order, true);
                    Put("DELIVERY", x.Delivery, true);
                    ws.Row(rowNo).Height = rowHeight;
                    rowNo++;
                }
            }
            ws.SheetView.FreezeRows(headerRow);
            if (original != null) { ws.Position = 1; original.Delete(); }
            wb.SaveAs(path);
            wb.Dispose();
            return path;
        }

        private static string Norm(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }
}
