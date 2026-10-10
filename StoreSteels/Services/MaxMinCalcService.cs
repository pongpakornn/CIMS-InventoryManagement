using ClosedXML.Excel;
using Microsoft.Data.SqlClient;
using CIMS.Core;
using CIMS.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CIMS.Services
{
    // Max-Min Calculator (ย้ายมาจาก StorePC แล้วขยายให้รองรับหลายคลัง) - ตาราง/SP อยู่ใน Database/MaxMinCalc.sql
    public class MaxMinCalcService
    {
        private readonly string _cs = GlobalConfig.ConnStr;

        #region === [ Rows / Config ] ===

        // สินค้าที่แสดงในหน้า Store (Max-Min) ของคลัง + MAX / MIN (DAYS) (ไม่มีค่าของตัวเอง = ค่าเริ่มต้นของสูตร)
        public List<MaxMinCalcRow> GetRows(StockModel stock, MaxMinFormula formula, string keyword)
        {
            var list = new List<MaxMinCalcRow>();
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"
                SELECT v.PartID, ISNULL(p.Customer, '') AS CUST, p.PartCode, ISNULL(p.PartA, '') AS PARTA, ISNULL(p.PartNumber, '') AS PARTNO,
                       p.Description, ISNULL(p.PackSize, 0) AS PSZ, c.MaxDays, c.MinDays, ISNULL(v.[Max], 0) AS QMAX, ISNULL(v.[Min], 0) AS QMIN
                FROM CIMS.vw_StockMonitoring v
                JOIN CIMS.Parts p ON p.PartID = v.PartID
                LEFT JOIN CIMS.MaxMinPartConfigs c ON c.StockID = v.StkId AND c.PartID = v.PartID
                WHERE v.StkId = @stk
                  AND (@key = '' OR p.PartCode LIKE '%' + @key + '%' OR p.Description LIKE '%' + @key + '%'
                       OR ISNULL(p.PartNumber, '') LIKE '%' + @key + '%' OR ISNULL(p.PartA, '') LIKE '%' + @key + '%'
                       OR ISNULL(p.Customer, '') LIKE '%' + @key + '%')
                ORDER BY ISNULL(NULLIF(p.Customer, ''), '-'), ISNULL(NULLIF(p.PartNumber, ''), p.PartCode)", conn))
            {
                cmd.Parameters.AddWithValue("@stk", stock.StkId);
                cmd.Parameters.AddWithValue("@key", (keyword ?? "").Trim());
                conn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        bool own = r["MaxDays"] != DBNull.Value || r["MinDays"] != DBNull.Value;
                        list.Add(new MaxMinCalcRow
                        {
                            PtId = Convert.ToInt32(r["PartID"]),
                            Customer = r["CUST"].ToString().Trim(),
                            PartCode = r["PartCode"].ToString(),
                            PartA = r["PARTA"].ToString(),
                            PartNoRaw = r["PARTNO"].ToString(),
                            PartName = r["Description"].ToString(),
                            PackSize = Convert.ToInt32(r["PSZ"]),
                            HasOwnDays = own,
                            DayMax = r["MaxDays"] != DBNull.Value ? Convert.ToInt32(r["MaxDays"]) : formula.DefDayMax,
                            DayMin = r["MinDays"] != DBNull.Value ? Convert.ToInt32(r["MinDays"]) : formula.DefDayMin,
                            QtyMax = CIMS.Helpers.Qty.Read(r["QMAX"]),
                            QtyMin = CIMS.Helpers.Qty.Read(r["QMIN"])
                        });
                    }
                }
            }
            return list;
        }

        // บันทึก MAX / MIN (DAYS) ของสินค้าในคลังนี้
        public void SaveDays(StockModel stock, MaxMinCalcRow row, int dayMax, int dayMin, string userId)
        {
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                UpsertDays(conn, null, stock.StkId, row.PtId, row.Customer, string.IsNullOrWhiteSpace(row.PartA) ? row.PartCode : row.PartA, dayMax, dayMin, userId);
            }
        }

        private static void UpsertDays(SqlConnection conn, SqlTransaction tr, int stkId, int ptId, string cust, string partCode, int dayMax, int dayMin, string userId)
        {
            using (var cmd = new SqlCommand(@"
                MERGE CIMS.MaxMinPartConfigs AS t
                USING (SELECT @s AS StockID, @p AS PartID) AS s ON t.StockID = s.StockID AND t.PartID = s.PartID
                WHEN MATCHED THEN UPDATE SET MaxDays = @dmax, MinDays = @dmin, CustomerCode = @c, UpdatedBy = @u, UpdatedDate = GETDATE()
                WHEN NOT MATCHED THEN INSERT (StockID, PartID, CustomerCode, PartACode, MaxDays, MinDays, UpdatedBy, UpdatedDate)
                                      VALUES (@s, @p, @c, @a, @dmax, @dmin, @u, GETDATE());", conn, tr))
            {
                cmd.Parameters.AddWithValue("@s", stkId);
                cmd.Parameters.AddWithValue("@p", ptId);
                cmd.Parameters.AddWithValue("@c", string.IsNullOrWhiteSpace(cust) ? "-" : cust);
                cmd.Parameters.AddWithValue("@a", partCode ?? "");
                cmd.Parameters.AddWithValue("@dmax", dayMax);
                cmd.Parameters.AddWithValue("@dmin", dayMin);
                cmd.Parameters.AddWithValue("@u", (object)userId ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        // RESET: ล้างค่า MAX / MIN ปัจจุบันในหน้า Store (Max-Min) ของสินค้านี้ (แสดงเป็น "-")
        public void ResetMaxMin(StockModel stock, int ptId)
        {
            string sql = stock.IsMain
                ? "UPDATE CIMS.Parts SET MaxQuantity = 0, MinQuantity = 0 WHERE PartID = @p"
                : "UPDATE CIMS.PartStocks SET MaxQuantity = 0, MinQuantity = 0, UpdatedDate = GETDATE() WHERE StockID = @s AND PartID = @p";
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@p", ptId);
                cmd.Parameters.AddWithValue("@s", stock.StkId);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        #endregion

        #region === [ Formula list / stock setting ] ===

        public List<MaxMinFormula> GetFormulas()
        {
            var list = new List<MaxMinFormula>();
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("SELECT * FROM CIMS.MaxMinFormulas ORDER BY IsDefault DESC, FormulaName", conn))
            {
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new MaxMinFormula
                        {
                            FormulaId = Convert.ToInt32(r["FormulaID"]),
                            Name = r["FormulaName"].ToString(),
                            IsDefault = Convert.ToBoolean(r["IsDefault"]),
                            QtySource = r["QuantitySource"].ToString(),
                            RoundMode = r["RoundMode"].ToString(),
                            DefDayMax = Convert.ToInt32(r["DefaultMaxDays"]),
                            DefDayMin = Convert.ToInt32(r["DefaultMinDays"]),
                            DefWorkdays = Convert.ToInt32(r["DefaultWorkdays"])
                        });
            }
            return list;
        }

        public bool IsFormulaNameTaken(string name, int exceptId)
        {
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM CIMS.MaxMinFormulas WHERE FormulaName = @n AND FormulaID <> @id", conn))
            {
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@id", exceptId);
                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        // เพิ่ม (FormulaId = 0) / แก้ไขสูตร -> คืน FormulaID
        public int SaveFormula(MaxMinFormula f, string userId)
        {
            string sql = f.FormulaId == 0
                ? @"INSERT INTO CIMS.MaxMinFormulas (FormulaName, QuantitySource, RoundMode, DefaultMaxDays, DefaultMinDays, DefaultWorkdays, IsDefault, UpdatedBy, UpdatedDate)
                    VALUES (@n, @q, @rm, @dmax, @dmin, @wd, 0, @u, GETDATE()); SELECT CAST(SCOPE_IDENTITY() AS INT);"
                : @"UPDATE CIMS.MaxMinFormulas SET FormulaName = @n, QuantitySource = @q, RoundMode = @rm, DefaultMaxDays = @dmax, DefaultMinDays = @dmin,
                           DefaultWorkdays = @wd, UpdatedBy = @u, UpdatedDate = GETDATE() WHERE FormulaID = @id; SELECT @id;";
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@id", f.FormulaId);
                cmd.Parameters.AddWithValue("@n", f.Name);
                cmd.Parameters.AddWithValue("@q", f.QtySource);
                cmd.Parameters.AddWithValue("@rm", f.RoundMode);
                cmd.Parameters.AddWithValue("@dmax", f.DefDayMax);
                cmd.Parameters.AddWithValue("@dmin", f.DefDayMin);
                cmd.Parameters.AddWithValue("@wd", f.DefWorkdays);
                cmd.Parameters.AddWithValue("@u", (object)userId ?? DBNull.Value);
                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        // ลบสูตร: สูตร DEFAULT ลบไม่ได้ / มีคลังใช้อยู่ -> คืนรายชื่อคลัง (ไม่ลบ)
        public List<string> DeleteFormula(int formulaId)
        {
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                var used = new List<string>();
                using (var cmd = new SqlCommand("SELECT s.StockCode FROM CIMS.MaxMinStockSettings c JOIN CIMS.Stocks s ON s.StockID = c.StockID WHERE c.FormulaID = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@id", formulaId);
                    using (var r = cmd.ExecuteReader()) while (r.Read()) used.Add(r[0].ToString());
                }
                if (used.Count > 0) return used;
                using (var cmd = new SqlCommand("DELETE FROM CIMS.MaxMinFormulas WHERE FormulaID = @id AND IsDefault = 0", conn))
                {
                    cmd.Parameters.AddWithValue("@id", formulaId);
                    cmd.ExecuteNonQuery();
                }
                return used;
            }
        }

        public StockCalcSetting GetStockSetting(int stkId)
        {
            var s = new StockCalcSetting { StkId = stkId };
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("SELECT FormulaID, AutoCalc, LastCalc FROM CIMS.MaxMinStockSettings WHERE StockID = @s", conn))
            {
                cmd.Parameters.AddWithValue("@s", stkId);
                conn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    if (r.Read())
                    {
                        s.FormulaId = r["FormulaID"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["FormulaID"]);
                        s.AutoCalc = Convert.ToBoolean(r["AutoCalc"]);
                        s.LastCalc = r["LastCalc"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["LastCalc"]);
                    }
                }
            }
            return s;
        }

        // เลือกสูตร + AUTO CALC ของคลัง (กด SAVE ที่หน้า Max-Min Calculator)
        public void SaveStockSetting(int stkId, int formulaId, bool autoCalc, string userId)
        {
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"
                MERGE CIMS.MaxMinStockSettings AS t USING (SELECT @s AS StockID) AS s ON t.StockID = s.StockID
                WHEN MATCHED THEN UPDATE SET FormulaID = @f, AutoCalc = @a, UpdatedBy = @u, UpdatedDate = GETDATE()
                WHEN NOT MATCHED THEN INSERT (StockID, FormulaID, AutoCalc, UpdatedBy, UpdatedDate) VALUES (@s, @f, @a, @u, GETDATE());", conn))
            {
                cmd.Parameters.AddWithValue("@s", stkId);
                cmd.Parameters.AddWithValue("@f", formulaId);
                cmd.Parameters.AddWithValue("@a", autoCalc);
                cmd.Parameters.AddWithValue("@u", (object)userId ?? DBNull.Value);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        // 📤 Export รูปแบบ Max-MinCal: NO / PART NO / PRODUCT NAME / MAX(DAY) / MIN(DAY) / MAX(BOX) / MIN(BOX) / FORECAST / ORDER / DELIVERY
        //    ใส่ข้อมูลแค่ NO (เลขรัน) / PART NO / PRODUCT NAME / MAX(DAY) / MIN(DAY) ที่เหลือว่างไว้ - กรอกแล้ว Import กลับได้เลย
        //    มีไฟล์แม่แบบ Templates\MaxMinCal_Export.xlsx ข้างโปรแกรม -> ใช้ไฟล์นั้น (หัวตาราง/สี/ความกว้างตามแม่แบบ) ไม่มี -> สร้างตามแบบเดียวกัน
        public static readonly string[] TemplateHeads = { "NO", "PART NO", "PRODUCT NAME", "MAX(DAY)", "MIN(DAY)", "MAX(BOX)", "MIN(BOX)", "FORECAST", "ORDER", "DELIVERY" };
        public static string ExportTemplatePath => Path.Combine(AppContext.BaseDirectory, "Templates", "MaxMinCal_Export.xlsx");

        public void ExportDays(IEnumerable<MaxMinCalcRow> rows, string path)
        {
            XLWorkbook wb = File.Exists(ExportTemplatePath) ? OpenBook(ExportTemplatePath) : null;
            IXLWorksheet ws; int header = 0; var col = new Dictionary<string, int>();
            if (wb != null)
            {
                ws = wb.Worksheet(1);
                header = 0;
                foreach (var row in ws.RowsUsed().Take(10))
                {
                    col.Clear();
                    var noCols = new List<int>();
                    foreach (var cell in row.CellsUsed())
                    {
                        string h = Norm(cell.GetString());
                        if (h == "NO") { noCols.Add(cell.Address.ColumnNumber); continue; }
                        foreach (var t in TemplateHeads) if (Norm(t) == h && !col.ContainsKey(t)) col[t] = cell.Address.ColumnNumber;
                    }
                    if (col.ContainsKey("PART NO"))
                    {
                        // แม่แบบมี NO 2 ช่อง (A และ E) -> ใช้ช่อง NO ที่อยู่ติดก่อน PART NO
                        var near = noCols.Where(c => c < col["PART NO"]).DefaultIfEmpty(0).Max();
                        if (near > 0) col["NO"] = near;
                        header = row.RowNumber(); break;
                    }
                }
                if (header == 0) { wb.Dispose(); wb = null; col.Clear(); }
            }
            if (wb == null)
            {
                wb = new XLWorkbook();
                ws = wb.Worksheets.Add("Max-MinCal");
                // ไม่มีไฟล์แม่แบบ -> สร้างตามแบบเดียวกัน: A-D (NO / SUPPLIER / CUSTOMER / CATEGORY ว่างไว้) แล้ว E-N
                header = 1;
                string[] lead = { "NO", "SUPPLIER", "CUSTOMER", "CATEGORY" };
                for (int c = 0; c < lead.Length; c++) ws.Cell(1, c + 1).Value = lead[c];
                for (int c = 0; c < TemplateHeads.Length; c++) { ws.Cell(1, lead.Length + c + 1).Value = TemplateHeads[c]; col[TemplateHeads[c]] = lead.Length + c + 1; }
                int heads = lead.Length + TemplateHeads.Length;
                var h = ws.Range(1, 1, 1, heads);
                h.Style.Fill.BackgroundColor = XLColor.FromHtml("#002060");
                h.Style.Font.FontColor = XLColor.White;
                h.Style.Font.Bold = true;
                ws.Row(1).Height = 34.5;
                double[] widths = { 6.4, 9.7, 14, 14.2, 8.3, 31, 44.4, 14.9, 14.9, 14.9, 14.9, 14.9, 14.9, 14.9 };
                for (int c = 0; c < widths.Length; c++) ws.Column(c + 1).Width = widths[c];
                ws.SheetView.FreezeRows(1);
            }
            else ws = wb.Worksheet(1);

            int first = header + 1, r = first, no = 1;
            // ตารางทั้งแผ่นตามแม่แบบ (รวม A-D: NO / SUPPLIER / CUSTOMER / CATEGORY ที่ปล่อยว่าง)
            int lastCol = Math.Max(col.Values.Max(), ws.Row(header).LastCellUsed()?.Address.ColumnNumber ?? 0);
            int firstCol = 1;
            // ล้างแถวตัวอย่างในแม่แบบก่อน (เก็บรูปแบบเซลล์ไว้)
            int oldLast = ws.LastRowUsed()?.RowNumber() ?? header;
            if (oldLast > header) ws.Range(first, firstCol, oldLast, lastCol).Clear(XLClearOptions.Contents);
            foreach (var x in rows)
            {
                if (col.TryGetValue("NO", out int cNo)) ws.Cell(r, cNo).Value = no;
                ws.Cell(r, col["PART NO"]).Value = x.PartNo;
                if (col.TryGetValue("PRODUCT NAME", out int cName)) ws.Cell(r, cName).Value = x.PartName;
                if (col.TryGetValue("MAX(DAY)", out int cMax)) ws.Cell(r, cMax).Value = x.DayMax;
                if (col.TryGetValue("MIN(DAY)", out int cMin)) ws.Cell(r, cMin).Value = x.DayMin;
                r++; no++;
            }
            int last = Math.Max(first, r - 1);
            // แถวเกินจากแม่แบบเดิม -> ล้างรูปแบบเก่าออก
            if (oldLast > last) ws.Range(last + 1, firstCol, oldLast, lastCol).Clear(XLClearOptions.All);
            var data = ws.Range(first, firstCol, last, lastCol);
            data.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            data.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            data.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            data.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            var hdr = ws.Range(header, firstCol, header, lastCol);
            hdr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            hdr.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            if (col.TryGetValue("NO", out int cBold)) ws.Range(first, cBold, last, cBold).Style.Font.Bold = true;
            ws.Rows(first, last).Height = 20.25;
            wb.SaveAs(path);
            wb.Dispose();
        }
        // คำนวณ (CIMS.sp_MaxMin_Calculate): stkId null = ทุกคลังที่เปิด AUTO CALC / ptId = เฉพาะสินค้านั้น
        public (int Updated, int Skipped, int Stocks) Calculate(int? stkId, int? ptId, string userId)
        {
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("CIMS.sp_MaxMin_Calculate", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 300 })
            {
                cmd.Parameters.AddWithValue("@StkId", (object)stkId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@PtId", (object)ptId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@UserId", (object)userId ?? DBNull.Value);
                conn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    if (r.Read()) return (Convert.ToInt32(r["Updated"]), Convert.ToInt32(r["Skipped"]), Convert.ToInt32(r["Stocks"]));
                }
            }
            return (0, 0, 0);
        }

        // เดือนล่าสุดที่มี Order / Forecast นำเข้า (แสดงบนหน้าจอว่าคำนวณจากเดือนไหน)
        public DateTime? GetLatestImportMonth()
        {
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("SELECT MAX(TargetDate) FROM CIMS.ForecastOrderImports WHERE TargetDate < DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1))", conn))
            {
                conn.Open();
                object v = cmd.ExecuteScalar();
                return v == null || v == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(v);
            }
        }

        #endregion

        #region === [ Customer workdays ] ===

        // ลูกค้าทั้งหมดในระบบ (จากสินค้า + นำเข้า Forecast + ปฏิทินที่ตั้งไว้) พร้อมจำนวนวันทำงานของปีนั้น
        public List<WorkdayCustomer> GetWorkdayCustomers(int year)
        {
            var list = new List<WorkdayCustomer>();
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"
                WITH c AS (
                    SELECT LTRIM(RTRIM(Customer)) AS CUST FROM CIMS.Parts WHERE IsActive = 1 AND ISNULL(LTRIM(RTRIM(Customer)), '') <> ''
                    UNION SELECT LTRIM(RTRIM(CustomerCode)) FROM CIMS.ForecastOrderImports WHERE ISNULL(LTRIM(RTRIM(CustomerCode)), '') <> ''
                    UNION SELECT CustomerCode FROM CIMS.CustomerWorkdays)
                SELECT c.CUST,
                       (SELECT COUNT(*) FROM CIMS.CustomerWorkdays w WHERE w.CustomerCode = c.CUST AND YEAR(w.WorkDate) = @y) AS DAYS_,
                       (SELECT COUNT(DISTINCT MONTH(w.WorkDate)) FROM CIMS.CustomerWorkdays w WHERE w.CustomerCode = c.CUST AND YEAR(w.WorkDate) = @y) AS MONTHS_
                FROM c ORDER BY c.CUST", conn))
            {
                cmd.Parameters.AddWithValue("@y", year);
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new WorkdayCustomer { Customer = r["CUST"].ToString(), YearDays = Convert.ToInt32(r["DAYS_"]), MonthsSet = Convert.ToInt32(r["MONTHS_"]) });
            }
            return list;
        }

        public HashSet<DateTime> GetWorkdays(string customer, int year)
        {
            var set = new HashSet<DateTime>();
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("SELECT WorkDate FROM CIMS.CustomerWorkdays WHERE CustomerCode = @c AND YEAR(WorkDate) = @y", conn))
            {
                cmd.Parameters.AddWithValue("@c", customer);
                cmd.Parameters.AddWithValue("@y", year);
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) set.Add(Convert.ToDateTime(r[0]).Date);
            }
            return set;
        }

        // กดวันในปฏิทิน: ครั้งแรก = วันทำงาน / กดซ้ำ = ยกเลิก (บันทึกทันที)
        public void SetWorkdays(string customer, IEnumerable<DateTime> dates, bool working, string userId)
        {
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var tr = conn.BeginTransaction())
                {
                    foreach (var d in dates.Select(x => x.Date).Distinct())
                    {
                        string sql = working
                            ? @"IF NOT EXISTS (SELECT 1 FROM CIMS.CustomerWorkdays WHERE CustomerCode = @c AND WorkDate = @d)
                                    INSERT INTO CIMS.CustomerWorkdays (CustomerCode, WorkDate, UpdatedBy) VALUES (@c, @d, @u)"
                            : "DELETE FROM CIMS.CustomerWorkdays WHERE CustomerCode = @c AND WorkDate = @d";
                        using (var cmd = new SqlCommand(sql, conn, tr))
                        {
                            cmd.Parameters.AddWithValue("@c", customer);
                            cmd.Parameters.Add("@d", SqlDbType.Date).Value = d;
                            cmd.Parameters.AddWithValue("@u", (object)userId ?? DBNull.Value);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    tr.Commit();
                }
            }
        }

        #endregion

        #region === [ Excel import ] ===
        // แบบฟอร์มจริงยังรอผู้ใช้ส่งมา -> หาหัวคอลัมน์เองจาก 10 แถวแรก (ไม่สนตัวพิมพ์ ช่องว่าง จุด วงเล็บ)

        private static readonly string[] CustH = { "CUSTOMER", "CUST", "CUSTOMERCODE", "CUSTCODE", "ลูกค้า" };
        private static readonly string[] PartH = { "PARTA", "PARTNO", "PARTNUMBER", "PDCODE", "PARTCODE", "PRODUCTCODE", "CODE", "ITEMCODE", "รหัสสินค้า" };
        private static readonly string[] MonthH = { "MONTH", "TARGETDATE", "PERIOD", "DATE", "เดือน" };
        private static readonly string[] YearH = { "YEAR", "ปี" };
        private static readonly string[] FcH = { "FORECAST", "FORECASTQTY", "FC" };
        private static readonly string[] OrdH = { "ORDER", "ORDERQTY", "PO" };
        private static readonly string[] DelH = { "DELIVERY", "DELIVERYQTY", "DLV" };
        private static readonly string[] WdH = { "WORKDAYS", "WORKDAY", "WORKINGDAYS", "WD", "วันทำงาน" };
        private static readonly string[] DMaxH = { "MAXDAYS", "MAXDAY", "SETMAXDAYS", "DAYMAX", "MAX" };
        private static readonly string[] DMinH = { "MINDAYS", "MINDAY", "SETMINDAYS", "DAYMIN", "MIN" };
        private static readonly string[] DateH = { "DATE", "WORKDATE", "WORKINGDATE", "วันที่" };

        private static string Norm(string h) =>
            new string((h ?? "").Where(c => !char.IsWhiteSpace(c) && c != '.' && c != '(' && c != ')' && c != '_' && c != '-' && c != '/').ToArray()).ToUpperInvariant();

        // หาแถวหัวตาราง: ต้องมีคอลัมน์ที่บังคับครบ
        private static (IXLWorksheet ws, int header, Dictionary<string, int> cols) OpenSheet(string path, Dictionary<string, string[]> want, string[] required, XLWorkbook wb)
        {
            var ws = wb.Worksheet(1);
            foreach (var row in ws.RowsUsed().Take(10))
            {
                var cols = new Dictionary<string, int>();
                foreach (var cell in row.CellsUsed())
                {
                    string h = Norm(cell.GetString());
                    foreach (var kv in want)
                        if (!cols.ContainsKey(kv.Key) && kv.Value.Contains(h)) { cols[kv.Key] = cell.Address.ColumnNumber; break; }
                }
                if (required.All(cols.ContainsKey)) return (ws, row.RowNumber(), cols);
            }
            throw new InvalidDataException("ไม่พบหัวคอลัมน์ในไฟล์ Excel\nต้องมีคอลัมน์: " + string.Join(", ", required) + " (อยู่ใน 10 แถวแรกของชีทแรก)");
        }

        private static XLWorkbook OpenBook(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("ไม่พบไฟล์ที่เลือก", path);
            // เปิดแบบแชร์ได้ (ไฟล์ยังเปิดค้างใน Excel ก็อ่านได้) แล้วปิดไฟล์ทันที
            var ms = new MemoryStream();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) fs.CopyTo(ms);
            ms.Position = 0;
            return new XLWorkbook(ms);
        }

        private static bool TryNumber(IXLCell c, out decimal v)
        {
            v = 0;
            if (c.IsEmpty()) return true;
            if (c.DataType == XLDataType.Number) { v = (decimal)c.GetDouble(); return true; }
            string s = c.GetString().Trim().Replace(",", "");
            return s.Length == 0 || decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out v);
        }

        // เดือน: ช่องวันที่ / 2026-09 / 09/2026 / Sep 2026 / เลข 1-12 (+ คอลัมน์ YEAR) - ว่าง = เดือนปัจจุบัน
        private static bool TryMonth(IXLCell c, int? year, out DateTime month)
        {
            month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            if (c == null || c.IsEmpty()) return true;
            if (c.DataType == XLDataType.DateTime) { var d = c.GetDateTime(); month = new DateTime(d.Year, d.Month, 1); return true; }
            string s = c.GetString().Trim();
            if (int.TryParse(s, out int m) && m >= 1 && m <= 12) { month = new DateTime(year ?? DateTime.Today.Year, m, 1); return true; }
            string[] fmts = { "yyyy-MM", "yyyy-M", "MM/yyyy", "M/yyyy", "MMM yyyy", "MMMM yyyy", "MMM-yy", "MMM-yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" };
            if (DateTime.TryParseExact(s, fmts, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) { month = new DateTime(dt.Year, dt.Month, 1); return true; }
            return false;
        }

        private static bool TryDate(IXLCell c, out DateTime date)
        {
            date = DateTime.MinValue;
            if (c == null || c.IsEmpty()) return false;
            if (c.DataType == XLDataType.DateTime) { date = c.GetDateTime().Date; return true; }
            string[] fmts = { "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy", "dd-MM-yyyy", "d MMM yyyy" };
            return DateTime.TryParseExact(c.GetString().Trim(), fmts, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        // 📊 FORECAST / ORDER / DELIVERY: CUSTOMER + (PART) + MONTH + FORECAST / ORDER / DELIVERY (+ WORK DAYS)
        public List<CalcImportRow> ReadForecastExcel(string path)
        {
            var rows = new List<CalcImportRow>();
            using (var wb = OpenBook(path))
            {
                var want = new Dictionary<string, string[]> { ["CUST"] = CustH, ["PART"] = PartH, ["MONTH"] = MonthH, ["YEAR"] = YearH, ["FC"] = FcH, ["ORD"] = OrdH, ["DLV"] = DelH, ["WD"] = WdH };
                var (ws, header, cols) = OpenSheet(path, want, new[] { "CUST" }, wb);
                if (!cols.ContainsKey("FC") && !cols.ContainsKey("ORD") && !cols.ContainsKey("DLV"))
                    throw new InvalidDataException("ไม่พบคอลัมน์ FORECAST / ORDER / DELIVERY ในไฟล์ Excel");

                int last = ws.LastRowUsed()?.RowNumber() ?? header;
                for (int r = header + 1; r <= last; r++)
                {
                    IXLCell Cell(string k) => cols.TryGetValue(k, out int c) ? ws.Cell(r, c) : null;
                    string cust = Cell("CUST")?.GetString().Trim() ?? "";
                    if (cust.Length == 0 && ws.Row(r).IsEmpty()) continue;
                    // แถวว่างที่มีแค่เลข NO (ไฟล์ Template) -> ข้าม ไม่นับเป็นแถวผิด
                    if (cust.Length == 0 && new[] { "PART", "FC", "ORD", "DLV" }.All(k => Cell(k) == null || Cell(k).IsEmpty())) continue;

                    var item = new CalcImportRow { RowNumber = r, Customer = cust, Part = Cell("PART")?.GetString().Trim() ?? "" };
                    int? year = null;
                    if (Cell("YEAR") != null && int.TryParse(Cell("YEAR").GetString().Trim(), out int y)) year = y;

                    if (cust.Length == 0) item.Error = "ไม่มีชื่อลูกค้า";
                    else if (!TryMonth(Cell("MONTH"), year, out DateTime mon)) item.Error = $"เดือนไม่ถูกต้อง ({Cell("MONTH")?.GetString()})";
                    else
                    {
                        item.Month = mon;
                        bool ok = true;
                        if (Cell("FC") != null) { ok &= TryNumber(Cell("FC"), out decimal v); item.Forecast = v; }
                        if (Cell("ORD") != null) { ok &= TryNumber(Cell("ORD"), out decimal v); item.Order = v; }
                        if (Cell("DLV") != null) { ok &= TryNumber(Cell("DLV"), out decimal v); item.Delivery = v; }
                        if (Cell("WD") != null && !Cell("WD").IsEmpty()) { if (TryNumber(Cell("WD"), out decimal w) && w > 0) item.Workdays = (int)w; else ok = false; }
                        if (!ok) item.Error = "ตัวเลขไม่ถูกต้อง";
                        else if (item.Forecast < 0 || item.Order < 0 || item.Delivery < 0) item.Error = "จำนวนติดลบ";
                    }
                    rows.Add(item);
                }
            }
            return rows;
        }

        // บันทึก: ลูกค้า + สินค้า + เดือนเดียวกันที่นำเข้าซ้ำ -> แทนที่ของเดิม
        public int ApplyForecast(List<CalcImportRow> rows, string userId)
        {
            var valid = rows.Where(r => r.IsValid).ToList();
            if (valid.Count == 0) return 0;
            Guid batch = Guid.NewGuid();
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var tr = conn.BeginTransaction())
                {
                    foreach (var key in valid.Select(v => new { v.Customer, Part = v.Part ?? "", v.Month }).Distinct())
                    {
                        using (var cmd = new SqlCommand(@"DELETE FROM CIMS.ForecastOrderImports
                                                          WHERE LTRIM(RTRIM(ISNULL(CustomerCode, ''))) = @c AND LTRIM(RTRIM(ISNULL(PartACode, ''))) = @p
                                                            AND TargetDate >= @m AND TargetDate < DATEADD(MONTH, 1, @m)", conn, tr))
                        {
                            cmd.Parameters.AddWithValue("@c", key.Customer);
                            cmd.Parameters.AddWithValue("@p", key.Part);
                            cmd.Parameters.Add("@m", SqlDbType.Date).Value = key.Month;
                            cmd.ExecuteNonQuery();
                        }
                    }
                    foreach (var v in valid)
                    {
                        using (var cmd = new SqlCommand(@"INSERT INTO CIMS.ForecastOrderImports (CustomerCode, PartACode, ForecastQuantity, OrderQuantity, DeliveryQuantity, WorkDays, TargetDate, RunGuid, CreatedAt, CreatedBy)
                                                          VALUES (@c, @p, @f, @o, @d, @w, @m, @g, GETDATE(), @u)", conn, tr))
                        {
                            cmd.Parameters.AddWithValue("@c", v.Customer);
                            cmd.Parameters.AddWithValue("@p", string.IsNullOrWhiteSpace(v.Part) ? (object)DBNull.Value : v.Part);
                            cmd.Parameters.AddWithValue("@f", v.Forecast);
                            cmd.Parameters.AddWithValue("@o", v.Order);
                            cmd.Parameters.AddWithValue("@d", v.Delivery);
                            cmd.Parameters.AddWithValue("@w", (object)v.Workdays ?? DBNull.Value);
                            cmd.Parameters.Add("@m", SqlDbType.Date).Value = v.Month;
                            cmd.Parameters.AddWithValue("@g", batch);
                            cmd.Parameters.AddWithValue("@u", (object)userId ?? DBNull.Value);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    tr.Commit();
                }
            }
            return valid.Count;
        }

        // 📥 SET MAX MIN: PART (PART A / PART NO / PD CODE) + MAX DAYS + MIN DAYS -> สินค้าในคลังที่เลือก
        public List<CalcImportRow> ReadDaysExcel(string path, StockModel stock, List<MaxMinCalcRow> stockRows)
        {
            var rows = new List<CalcImportRow>();
            using (var wb = OpenBook(path))
            {
                var want = new Dictionary<string, string[]> { ["CUST"] = CustH, ["PART"] = PartH, ["DMAX"] = DMaxH, ["DMIN"] = DMinH };
                var (ws, header, cols) = OpenSheet(path, want, new[] { "PART", "DMAX", "DMIN" }, wb);
                int last = ws.LastRowUsed()?.RowNumber() ?? header;
                for (int r = header + 1; r <= last; r++)
                {
                    string part = ws.Cell(r, cols["PART"]).GetString().Trim();
                    string cust = cols.TryGetValue("CUST", out int cc) ? ws.Cell(r, cc).GetString().Trim() : "";
                    if (part.Length == 0 && ws.Row(r).IsEmpty()) continue;
                    var item = new CalcImportRow { RowNumber = r, Part = part, Customer = cust };
                    if (part.Length == 0) { item.Error = "ไม่มีรหัสสินค้า"; rows.Add(item); continue; }

                    if (!TryNumber(ws.Cell(r, cols["DMAX"]), out decimal dmax) || !TryNumber(ws.Cell(r, cols["DMIN"]), out decimal dmin))
                        item.Error = "จำนวนวันไม่ถูกต้อง";
                    else if (dmax < 0 || dmin < 0 || dmax != Math.Floor(dmax) || dmin != Math.Floor(dmin)) item.Error = "จำนวนวันต้องเป็นจำนวนเต็มไม่ติดลบ";
                    else if (dmin > dmax) item.Error = "MIN DAYS มากกว่า MAX DAYS";
                    else
                    {
                        item.DayMax = (int)dmax; item.DayMin = (int)dmin;
                        var match = stockRows.FirstOrDefault(x => (cust.Length == 0 || string.Equals(x.Customer, cust, StringComparison.OrdinalIgnoreCase)) &&
                                     (string.Equals(x.PartA, part, StringComparison.OrdinalIgnoreCase) || string.Equals(x.PartNoRaw, part, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(x.PartCode, part, StringComparison.OrdinalIgnoreCase)));
                        if (match == null) item.Error = $"ไม่พบสินค้านี้ในคลัง {stock.Code}";
                        else { item.PtId = match.PtId; item.Customer = match.Customer; item.Part = string.IsNullOrWhiteSpace(match.PartA) ? match.PartCode : match.PartA; }
                    }
                    rows.Add(item);
                }
            }
            return rows;
        }

        public int ApplyDays(StockModel stock, List<CalcImportRow> rows, string userId)
        {
            var valid = rows.Where(r => r.IsValid).GroupBy(r => r.PtId).Select(g => g.Last()).ToList();
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var tr = conn.BeginTransaction())
                {
                    foreach (var v in valid) UpsertDays(conn, tr, stock.StkId, v.PtId, v.Customer, v.Part, v.DayMax, v.DayMin, userId);
                    tr.Commit();
                }
            }
            return valid.Count;
        }

        // 📥 ไฟล์รูปแบบ Max-MinCal: หัวตารางอยู่คอลัมน์ไหนก็ได้ (มี SUPPLIER / CUSTOMER / CATEGORY / MODEL ข้างหน้าก็ได้)
        //    หาสินค้าในคลังจาก PART NO (Part A / Part No / PD Code) ไม่เจอ -> PRODUCT NAME
        //    ช่องไหนมีข้อมูลก็นำเข้าช่องนั้น: MAX/MIN(DAY) = จำนวนวัน / MAX/MIN(BOX) = MAX / MIN ในหน้า Store / FORECAST ORDER DELIVERY = เดือนนี้
        //    คืน null = ไม่ใช่ไฟล์รูปแบบนี้ (ไม่มีหัว PART NO / PRODUCT NAME + ช่องข้อมูล)
        public List<CalcImportRow> ReadTemplateExcel(string path, StockModel stock, List<MaxMinCalcRow> stockRows)
        {
            using (var wb = OpenBook(path))
            {
                var ws = wb.Worksheet(1);
                int header = 0; var cols = new Dictionary<string, int>();
                var want = new Dictionary<string, string[]>
                {
                    ["PART"] = new[] { "PARTNO", "PARTA", "PARTNUMBER", "PDCODE", "PARTCODE", "PRODUCTCODE" },
                    ["NAME"] = new[] { "PRODUCTNAME", "PARTNAME", "NAME", "DESCRIPTION" },
                    ["CUST"] = CustH,
                    ["DMAX"] = new[] { "MAXDAY", "MAXDAYS" }, ["DMIN"] = new[] { "MINDAY", "MINDAYS" },
                    ["BMAX"] = new[] { "MAXBOX" }, ["BMIN"] = new[] { "MINBOX" },
                    ["FC"] = FcH, ["ORD"] = OrdH, ["DLV"] = DelH
                };
                string[] dataKeys = { "DMAX", "DMIN", "BMAX", "BMIN", "FC", "ORD", "DLV" };
                foreach (var row in ws.RowsUsed().Take(10))
                {
                    cols.Clear();
                    foreach (var cell in row.CellsUsed())
                    {
                        string h = Norm(cell.GetString());
                        foreach (var kv in want)
                            if (!cols.ContainsKey(kv.Key) && kv.Value.Contains(h)) { cols[kv.Key] = cell.Address.ColumnNumber; break; }
                    }
                    if ((cols.ContainsKey("PART") || cols.ContainsKey("NAME")) && dataKeys.Any(cols.ContainsKey)) { header = row.RowNumber(); break; }
                }
                if (header == 0) return null;

                var rows = new List<CalcImportRow>();
                int last = ws.LastRowUsed()?.RowNumber() ?? header;
                for (int r = header + 1; r <= last; r++)
                {
                    IXLCell Cell(string k) => cols.TryGetValue(k, out int c) ? ws.Cell(r, c) : null;
                    string part = Cell("PART")?.GetString().Trim() ?? "";
                    string name = Cell("NAME")?.GetString().Trim() ?? "";
                    string cust = Cell("CUST")?.GetString().Trim() ?? "";
                    bool anyData = dataKeys.Any(k => Cell(k) != null && !Cell(k).IsEmpty());
                    if (part.Length == 0 && name.Length == 0) continue;   // แถวว่าง / แถวที่มีแค่เลข NO
                    var item = new CalcImportRow { RowNumber = r, Part = part, PartName = name, Customer = cust };
                    if (!anyData) continue;                               // ไม่ได้กรอกอะไร = ไม่เปลี่ยน

                    int? Int(string k, string label)
                    {
                        var c = Cell(k);
                        if (c == null || c.IsEmpty()) return null;
                        if (!TryNumber(c, out decimal v) || v < 0 || v != Math.Floor(v)) { item.Error ??= $"{label} ต้องเป็นจำนวนเต็มไม่ติดลบ ({c.GetString()})"; return null; }
                        return (int)v;
                    }
                    decimal? Dec(string k, string label)
                    {
                        var c = Cell(k);
                        if (c == null || c.IsEmpty()) return null;
                        if (!TryNumber(c, out decimal v) || v < 0) { item.Error ??= $"{label} ไม่ถูกต้อง ({c.GetString()})"; return null; }
                        return v;
                    }
                    item.TDayMax = Int("DMAX", "MAX(DAY)"); item.TDayMin = Int("DMIN", "MIN(DAY)");
                    item.TBoxMax = Int("BMAX", "MAX(BOX)"); item.TBoxMin = Int("BMIN", "MIN(BOX)");
                    item.TForecast = Dec("FC", "FORECAST"); item.TOrder = Dec("ORD", "ORDER"); item.TDelivery = Dec("DLV", "DELIVERY");

                    if (item.Error == null)
                    {
                        bool Same(string a, string b) => string.Equals((a ?? "").Trim(), b, StringComparison.OrdinalIgnoreCase);
                        var cand = stockRows.Where(x => cust.Length == 0 || Same(x.Customer, cust)).ToList();
                        var match = part.Length > 0 ? cand.FirstOrDefault(x => Same(x.PartA, part) || Same(x.PartNoRaw, part) || Same(x.PartCode, part)) : null;
                        if (match == null && name.Length > 0) match = cand.FirstOrDefault(x => Same(x.PartName, name));
                        if (match == null) item.Error = $"ไม่พบสินค้า {(part.Length > 0 ? part : name)} ในคลัง {stock.Code}";
                        else
                        {
                            item.PtId = match.PtId;
                            item.Customer = match.Customer;
                            item.Part = string.IsNullOrWhiteSpace(match.PartA) ? match.PartCode : match.PartA;
                            // วัน: กรอกช่องเดียว -> อีกช่องใช้ค่าปัจจุบัน
                            int dmax = item.TDayMax ?? match.DayMax, dmin = item.TDayMin ?? match.DayMin;
                            if (item.HasDays && dmin > dmax) item.Error = $"MIN(DAY) {dmin} มากกว่า MAX(DAY) {dmax}";
                            else { item.DayMax = dmax; item.DayMin = dmin; }
                            if (item.Error == null && item.TBoxMax.HasValue && item.TBoxMin.HasValue && item.TBoxMin > item.TBoxMax)
                                item.Error = $"MIN(BOX) {item.TBoxMin} มากกว่า MAX(BOX) {item.TBoxMax}";
                        }
                    }
                    rows.Add(item);
                }
                return rows;
            }
        }

        public CalcTemplateResult ApplyTemplate(StockModel stock, List<CalcImportRow> rows, string userId)
        {
            var res = new CalcTemplateResult();
            var valid = rows.Where(r => r.IsValid).GroupBy(r => r.PtId).Select(g => g.Last()).ToList();
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var tr = conn.BeginTransaction())
                {
                    foreach (var v in valid.Where(x => x.HasDays))
                    { UpsertDays(conn, tr, stock.StkId, v.PtId, v.Customer, v.Part, v.DayMax, v.DayMin, userId); res.Days++; }

                    // MAX / MIN (BOX): คลังที่เทียบ MAX/MIN เป็นกล่อง เก็บเป็นกล่อง / คลังอื่นแปลงเป็นหน่วยของคลัง (x Pack Size)
                    string boxSql = (stock.IsMain
                        ? "UPDATE p SET MaxQuantity = ISNULL(@mx * {0}, p.MaxQuantity), MinQuantity = ISNULL(@mn * {0}, p.MinQuantity) FROM CIMS.Parts p WHERE p.PartID = @p"
                        : "UPDATE ps SET MaxQuantity = ISNULL(@mx * {0}, ps.MaxQuantity), MinQuantity = ISNULL(@mn * {0}, ps.MinQuantity), UpdatedDate = GETDATE() FROM CIMS.PartStocks ps JOIN CIMS.Parts p ON p.PartID = ps.PartID WHERE ps.StockID = @s AND ps.PartID = @p");
                    string mult = stock.MaxMinInBox || stock.MaxMinInCoil ? "1" : "CASE WHEN ISNULL(p.PackSize, 0) > 0 THEN p.PackSize ELSE 1 END";   // BOX / COIL เก็บตามไฟล์
                    foreach (var v in valid.Where(x => x.HasBox))
                    {
                        using (var cmd = new SqlCommand(string.Format(boxSql, mult), conn, tr))
                        {
                            cmd.Parameters.Add("@mx", SqlDbType.Int).Value = (object)v.TBoxMax ?? DBNull.Value;
                            cmd.Parameters.Add("@mn", SqlDbType.Int).Value = (object)v.TBoxMin ?? DBNull.Value;
                            cmd.Parameters.AddWithValue("@p", v.PtId);
                            cmd.Parameters.AddWithValue("@s", stock.StkId);
                            cmd.ExecuteNonQuery();
                        }
                        res.Box++;
                    }
                    tr.Commit();
                }
            }

            // FORECAST / ORDER / DELIVERY -> เดือนปัจจุบัน ของลูกค้า + สินค้านั้น (ซ้ำเดือนเดิม = แทนที่)
            var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var fc = valid.Where(x => x.HasForecast).Select(x => new CalcImportRow
            {
                RowNumber = x.RowNumber, Customer = string.IsNullOrWhiteSpace(x.Customer) ? "-" : x.Customer, Part = x.Part, Month = month,
                Forecast = x.TForecast ?? 0, Order = x.TOrder ?? 0, Delivery = x.TDelivery ?? 0
            }).ToList();
            if (fc.Count > 0) res.Forecast = ApplyForecast(fc, userId);
            return res;
        }

        // 📅 วันทำงาน: CUSTOMER + DATE (1 แถว = 1 วันทำงาน) -> เดือนที่อยู่ในไฟล์ของลูกค้านั้นถูกแทนที่ทั้งเดือน
        // 📄 TEMPLATE ของ IMPORT WORKDAYS: CUSTOMER / DATE 1 แถว = 1 วันทำงาน + วันทำงานของปีนี้ที่มีอยู่แล้วใส่ไว้ให้
        //    customer = null -> ทุกลูกค้า / ลูกค้ายังไม่มีวันทำงาน -> ใส่ จ.-ศ. ของเดือนนี้เป็นตัวอย่าง
        public string WriteWorkdayTemplate(int year, string customer)
        {
            string path = ImportTemplateService.NewPath(ImportTemplateService.Systems.MaxMinCalc, $"Workdays_{(customer ?? "ALL")}_{year}_Template");
            var custs = customer != null ? new List<string> { customer } : GetWorkdayCustomers(year).Select(c => c.Customer).ToList();
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("WORKDAYS");
                string[] heads = { "NO", "CUSTOMER", "DATE", "DAY" };
                ImportTemplateService.Header(ws, heads, new double[] { 6.4, 20, 14, 12 });
                int r = 2;
                foreach (var c in custs)
                {
                    var days = GetWorkdays(c, year).OrderBy(d => d).ToList();
                    if (days.Count == 0)
                    {
                        var m = new DateTime(DateTime.Today.Year == year ? DateTime.Today.Year : year, DateTime.Today.Year == year ? DateTime.Today.Month : 1, 1);
                        days = Enumerable.Range(0, DateTime.DaysInMonth(m.Year, m.Month)).Select(i => m.AddDays(i))
                                         .Where(d => d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday).ToList();
                    }
                    foreach (var d in days)
                    {
                        ws.Cell(r, 1).Value = r - 1;
                        ws.Cell(r, 2).Value = c;
                        ws.Cell(r, 3).Value = d;
                        ws.Cell(r, 3).Style.DateFormat.Format = "dd/MM/yyyy";
                        ws.Cell(r, 4).Value = d.ToString("ddd", CultureInfo.InvariantCulture).ToUpperInvariant();
                        r++;
                    }
                }
                int last = Math.Max(r - 1, 31);
                ImportTemplateService.Body(ws, last, heads.Length);
                ImportTemplateService.Guide(wb, "CIMS - Import Customer Workdays (Max-Min Calculator)", new[]
                {
                    ("CUSTOMER *", "รหัสลูกค้า (บังคับ) - ตรงกับ CUSTOMER ของสินค้าในระบบ"),
                    ("DATE *", "วันทำงาน 1 แถว = 1 วัน เช่น 01/10/2026 (วัน/เดือน/ปี ค.ศ.)"),
                    ("DAY", "ไว้ดูเท่านั้น - ระบบไม่ได้อ่าน"),
                    ("", "ลูกค้า + เดือนที่อยู่ในไฟล์ จะถูกแทนที่ด้วยวันในไฟล์ทั้งเดือน (เดือนที่ไม่อยู่ในไฟล์ไม่เปลี่ยน)"),
                    ("", "ไฟล์นี้มีวันทำงานของปีที่เลือกใส่ไว้ให้แล้ว (ลูกค้าที่ยังไม่มี = จ.-ศ. ของเดือนนี้เป็นตัวอย่าง) แก้แล้วกด IMPORT WORKDAYS"),
                });
                wb.Worksheet(1).SetTabActive();
                wb.SaveAs(path);
            }
            return path;
        }

        public List<CalcImportRow> ReadWorkdayExcel(string path)
        {
            var rows = new List<CalcImportRow>();
            using (var wb = OpenBook(path))
            {
                var want = new Dictionary<string, string[]> { ["CUST"] = CustH, ["DATE"] = DateH };
                var (ws, header, cols) = OpenSheet(path, want, new[] { "CUST", "DATE" }, wb);
                int last = ws.LastRowUsed()?.RowNumber() ?? header;
                for (int r = header + 1; r <= last; r++)
                {
                    string cust = ws.Cell(r, cols["CUST"]).GetString().Trim();
                    if (cust.Length == 0 && ws.Row(r).IsEmpty()) continue;
                    var item = new CalcImportRow { RowNumber = r, Customer = cust };
                    if (cust.Length == 0) item.Error = "ไม่มีชื่อลูกค้า";
                    else if (!TryDate(ws.Cell(r, cols["DATE"]), out DateTime d)) item.Error = $"วันที่ไม่ถูกต้อง ({ws.Cell(r, cols["DATE"]).GetString()})";
                    else { item.Date = d; item.Month = new DateTime(d.Year, d.Month, 1); }
                    rows.Add(item);
                }
            }
            return rows;
        }

        public (int Days, int Months) ApplyWorkdays(List<CalcImportRow> rows, string userId)
        {
            var valid = rows.Where(r => r.IsValid).ToList();
            var months = valid.Select(v => new { v.Customer, v.Month }).Distinct().ToList();
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var tr = conn.BeginTransaction())
                {
                    foreach (var m in months)
                    {
                        using (var cmd = new SqlCommand("DELETE FROM CIMS.CustomerWorkdays WHERE CustomerCode = @c AND WorkDate >= @m AND WorkDate < DATEADD(MONTH, 1, @m)", conn, tr))
                        {
                            cmd.Parameters.AddWithValue("@c", m.Customer);
                            cmd.Parameters.Add("@m", SqlDbType.Date).Value = m.Month;
                            cmd.ExecuteNonQuery();
                        }
                    }
                    foreach (var d in valid.Select(v => new { v.Customer, v.Date }).Distinct())
                    {
                        using (var cmd = new SqlCommand("INSERT INTO CIMS.CustomerWorkdays (CustomerCode, WorkDate, UpdatedBy) VALUES (@c, @d, @u)", conn, tr))
                        {
                            cmd.Parameters.AddWithValue("@c", d.Customer);
                            cmd.Parameters.Add("@d", SqlDbType.Date).Value = d.Date;
                            cmd.Parameters.AddWithValue("@u", (object)userId ?? DBNull.Value);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    tr.Commit();
                }
            }
            return (valid.Select(v => new { v.Customer, v.Date }).Distinct().Count(), months.Count);
        }

        #endregion
    }
}
