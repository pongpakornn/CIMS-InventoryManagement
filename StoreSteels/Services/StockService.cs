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
    // คลังสินค้า (MST_STOCK), รูปแบบบาร์โค้ด Supplier (MST_BARCODE_FMT), โอนย้าย และ Import Excel
    // ตาราง/SP ทั้งหมดอยู่ใน Database/MultiStock.sql
    public class StockService
    {
        private readonly string _connectionString = GlobalConfig.ConnStr;

        #region === [ Stocks ] ===

        private const string StockColumns = @"
            s.STK_ID, s.STK_CODE, s.STK_NAME, s.STK_UNIT, s.IS_MAIN, s.USE_MAXMIN,
            s.COL_IMAGE, s.COL_CODE, s.COL_NAME, s.COL_QTY, s.COL_REMARK,
            s.IN_PICKLIST, s.IN_SUPPLIER, s.IN_SYSQR, s.IN_EXCEL,
            s.OUT_PICKLIST, s.OUT_SUPPLIER, s.OUT_SYSQR, s.SORT_NO,
            s.GROUP_BY, s.MAXMIN_BASIS, s.COL_CUSTOMER, s.COL_PARTA, s.COL_PARTNO, s.COL_STK_BOX, s.COL_STK_PCS,
            s.COL_NO, s.COL_MODEL, s.OUT_EXCEL";

        // คลังทั้งหมด (คลังหลักอยู่บนสุด) พร้อมจำนวนรายการสินค้าในแต่ละคลัง
        public List<StockModel> GetStocks()
        {
            var list = new List<StockModel>();
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                string sql = $@"SELECT {StockColumns}, ISNULL(v.ItemCount, 0) AS ItemCount
                                FROM MST_STOCK s
                                LEFT JOIN VW_StockSummary v ON v.STK_ID = s.STK_ID
                                ORDER BY s.IS_MAIN DESC, s.SORT_NO, s.STK_CODE";
                using (var cmd = new SqlCommand(sql, conn))
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        var s = ReadStock(rdr);
                        s.ItemCount = Convert.ToInt32(rdr["ItemCount"]);
                        list.Add(s);
                    }
                }
                LoadFormatIds(conn, list);
            }
            return list;
        }

        public StockModel GetStock(int stkId) => GetStocks().FirstOrDefault(s => s.StkId == stkId);

        // 🧾 PR SETTINGS: คลังที่แสกนออกแล้วสร้าง PR อัตโนมัติ (MST_STOCK.PR_AUTO - Database/PRView.sql)
        public Dictionary<int, bool> GetPrAutoMap()
        {
            var map = new Dictionary<int, bool>();
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var chk = new SqlCommand("SELECT COL_LENGTH('dbo.MST_STOCK', 'PR_AUTO')", conn))
                    if (chk.ExecuteScalar() == DBNull.Value)
                        throw new InvalidOperationException("ฐานข้อมูลยังไม่รองรับการตั้งค่า PR อัตโนมัติ กรุณารัน Database/PRView.sql");

                using (var cmd = new SqlCommand("SELECT STK_ID, PR_AUTO FROM MST_STOCK", conn))
                using (var rdr = cmd.ExecuteReader())
                    while (rdr.Read()) map[Convert.ToInt32(rdr[0])] = Convert.ToBoolean(rdr[1]);
            }
            return map;
        }

        public void SetPrAuto(Dictionary<int, bool> map, string userId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    foreach (var kv in map)
                    {
                        using (var cmd = new SqlCommand("UPDATE MST_STOCK SET PR_AUTO = @on, UPDATED_BY = @u, UPDATED_DATE = GETDATE() WHERE STK_ID = @id", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@on", kv.Value);
                            cmd.Parameters.AddWithValue("@u", (object)userId ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@id", kv.Key);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    trans.Commit();
                }
            }
        }

        public StockModel GetMainStock() => GetStocks().FirstOrDefault(s => s.IsMain);

        private static StockModel ReadStock(SqlDataReader rdr)
        {
            return new StockModel
            {
                StkId = Convert.ToInt32(rdr["STK_ID"]),
                Code = rdr["STK_CODE"].ToString(),
                Name = rdr["STK_NAME"].ToString(),
                Unit = rdr["STK_UNIT"].ToString(),
                IsMain = Convert.ToBoolean(rdr["IS_MAIN"]),
                UseMaxMin = Convert.ToBoolean(rdr["USE_MAXMIN"]),
                ColImage = Convert.ToBoolean(rdr["COL_IMAGE"]),
                ColCode = Convert.ToBoolean(rdr["COL_CODE"]),
                ColName = Convert.ToBoolean(rdr["COL_NAME"]),
                ColQty = Convert.ToBoolean(rdr["COL_QTY"]),
                ColRemark = Convert.ToBoolean(rdr["COL_REMARK"]),
                InPickList = Convert.ToBoolean(rdr["IN_PICKLIST"]),
                InSupplier = Convert.ToBoolean(rdr["IN_SUPPLIER"]),
                InSysQr = Convert.ToBoolean(rdr["IN_SYSQR"]),
                InExcel = Convert.ToBoolean(rdr["IN_EXCEL"]),
                OutPickList = Convert.ToBoolean(rdr["OUT_PICKLIST"]),
                OutSupplier = Convert.ToBoolean(rdr["OUT_SUPPLIER"]),
                OutSysQr = Convert.ToBoolean(rdr["OUT_SYSQR"]),
                SortNo = Convert.ToInt32(rdr["SORT_NO"]),
                GroupBy = rdr["GROUP_BY"].ToString(),
                MaxMinBasis = rdr["MAXMIN_BASIS"].ToString(),
                ColCustomer = Convert.ToBoolean(rdr["COL_CUSTOMER"]),
                ColPartA = Convert.ToBoolean(rdr["COL_PARTA"]),
                ColPartNo = Convert.ToBoolean(rdr["COL_PARTNO"]),
                ColStockBox = Convert.ToBoolean(rdr["COL_STK_BOX"]),
                ColStockPcs = Convert.ToBoolean(rdr["COL_STK_PCS"]),
                ColNo = Convert.ToBoolean(rdr["COL_NO"]),
                ColModel = Convert.ToBoolean(rdr["COL_MODEL"]),
                OutExcel = Convert.ToBoolean(rdr["OUT_EXCEL"])
            };
        }

        private static void LoadFormatIds(SqlConnection conn, List<StockModel> stocks)
        {
            using (var cmd = new SqlCommand("SELECT STK_ID, FMT_ID FROM MST_STOCK_FMT", conn))
            using (var rdr = cmd.ExecuteReader())
            {
                var byId = stocks.ToDictionary(s => s.StkId);
                while (rdr.Read())
                {
                    if (byId.TryGetValue(Convert.ToInt32(rdr["STK_ID"]), out var s))
                        s.FormatIds.Add(Convert.ToInt32(rdr["FMT_ID"]));
                }
            }
        }

        public bool IsStockCodeTaken(string code, int exceptStkId)
        {
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand("SELECT COUNT(1) FROM MST_STOCK WHERE STK_CODE = @code AND STK_ID <> @id", conn))
            {
                cmd.Parameters.AddWithValue("@code", code.Trim());
                cmd.Parameters.AddWithValue("@id", exceptStkId);
                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        // สร้างคลังใหม่ + ผูกรูปแบบบาร์โค้ด + ให้สิทธิ์ VIEW ผู้ใช้ทุกคน (sp_Stock_GrantViewAll) ใน Transaction เดียว
        public int CreateStock(StockModel s, string userId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    string sql = @"
                        INSERT INTO MST_STOCK (STK_CODE, STK_NAME, STK_UNIT, IS_MAIN, USE_MAXMIN,
                            COL_IMAGE, COL_CODE, COL_NAME, COL_QTY, COL_REMARK,
                            IN_PICKLIST, IN_SUPPLIER, IN_SYSQR, IN_EXCEL, OUT_PICKLIST, OUT_SUPPLIER, OUT_SYSQR,
                            GROUP_BY, MAXMIN_BASIS, COL_CUSTOMER, COL_PARTA, COL_PARTNO, COL_STK_BOX, COL_STK_PCS, COL_NO, COL_MODEL, OUT_EXCEL,
                            SORT_NO, CREATED_BY)
                        VALUES (@code, @name, @unit, 0, @maxmin,
                            @cimg, @ccode, @cname, @cqty, @crmk,
                            @ipl, @isup, @isys, @ixls, @opl, @osup, @osys,
                            @grp, @mmb, @ccust, @cparta, @cpartno, @cbox, @cpcs, @cno, @cmodel, @oxls,
                            (SELECT ISNULL(MAX(SORT_NO), 0) + 1 FROM MST_STOCK), @uid);
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

                    int newId;
                    using (var cmd = new SqlCommand(sql, conn, trans))
                    {
                        AddStockParams(cmd, s);
                        cmd.Parameters.AddWithValue("@uid", userId);
                        newId = (int)cmd.ExecuteScalar();
                    }

                    SaveFormatLinks(conn, trans, newId, s.FormatIds);

                    using (var cmd = new SqlCommand("sp_Stock_GrantViewAll", conn, trans) { CommandType = CommandType.StoredProcedure })
                    {
                        cmd.Parameters.AddWithValue("@StkId", newId);
                        cmd.ExecuteNonQuery();
                    }

                    trans.Commit();
                    s.StkId = newId;
                    // โฟลเดอร์รูปของคลังใหม่ (1. Image Stock\<รหัสคลัง>) - Network ไม่พร้อมก็ไม่กระทบการสร้างคลัง (สร้างอีกทีตอนอัปโหลดรูป)
                    try { CIMS.Helpers.ImagePaths.EnsureStockFolder(s.Code); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"EnsureStockFolder: {ex.Message}"); }
                    return newId;
                }
            }
        }

        public void UpdateStock(StockModel s, string userId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    // รหัสคลังเดิม (ก่อนแก้) ใช้เปลี่ยนชื่อสิทธิ์
                    string oldCode;
                    using (var cmd = new SqlCommand("SELECT STK_CODE FROM MST_STOCK WHERE STK_ID = @id", conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@id", s.StkId);
                        oldCode = cmd.ExecuteScalar()?.ToString();
                    }

                    string sql = @"
                        UPDATE MST_STOCK SET
                            STK_CODE = @code, STK_NAME = @name, STK_UNIT = @unit, USE_MAXMIN = @maxmin,
                            COL_IMAGE = @cimg, COL_CODE = @ccode, COL_NAME = @cname, COL_QTY = @cqty, COL_REMARK = @crmk,
                            IN_PICKLIST = @ipl, IN_SUPPLIER = @isup, IN_SYSQR = @isys, IN_EXCEL = @ixls,
                            OUT_PICKLIST = @opl, OUT_SUPPLIER = @osup, OUT_SYSQR = @osys,
                            GROUP_BY = @grp, MAXMIN_BASIS = @mmb, COL_CUSTOMER = @ccust, COL_PARTA = @cparta,
                            COL_PARTNO = @cpartno, COL_STK_BOX = @cbox, COL_STK_PCS = @cpcs,
                                 COL_NO = @cno, COL_MODEL = @cmodel, OUT_EXCEL = @oxls,
                            UPDATED_BY = @uid, UPDATED_DATE = GETDATE()
                        WHERE STK_ID = @id";

                    using (var cmd = new SqlCommand(sql, conn, trans))
                    {
                        AddStockParams(cmd, s);
                        cmd.Parameters.AddWithValue("@uid", userId);
                        cmd.Parameters.AddWithValue("@id", s.StkId);
                        cmd.ExecuteNonQuery();
                    }

                    // สิทธิ์ของคลังใช้รหัสคลังเป็น SYS_ID -> เปลี่ยนรหัสคลังแล้วต้องเปลี่ยนชื่อสิทธิ์ตามใน Transaction เดียวกัน
                    if (!string.IsNullOrEmpty(oldCode) && !string.Equals(oldCode, s.Code.Trim(), StringComparison.Ordinal))
                    {
                        using (var cmd = new SqlCommand("UPDATE MST_PERM SET SYS_ID = @new WHERE SYS_ID = @old", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@new", s.Code.Trim());
                            cmd.Parameters.AddWithValue("@old", oldCode);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    using (var cmd = new SqlCommand("DELETE FROM MST_STOCK_FMT WHERE STK_ID = @id", conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@id", s.StkId);
                        cmd.ExecuteNonQuery();
                    }
                    SaveFormatLinks(conn, trans, s.StkId, s.FormatIds);

                    trans.Commit();
                }
            }
        }

        private static void AddStockParams(SqlCommand cmd, StockModel s)
        {
            cmd.Parameters.AddWithValue("@code", s.Code.Trim());
            cmd.Parameters.AddWithValue("@name", s.Name.Trim());
            cmd.Parameters.AddWithValue("@unit", string.IsNullOrWhiteSpace(s.Unit) ? "KG" : s.Unit.Trim());
            cmd.Parameters.AddWithValue("@maxmin", s.UseMaxMin);
            cmd.Parameters.AddWithValue("@cimg", s.ColImage);
            cmd.Parameters.AddWithValue("@ccode", s.ColCode);
            cmd.Parameters.AddWithValue("@cname", s.ColName);
            cmd.Parameters.AddWithValue("@cqty", s.ColQty);
            cmd.Parameters.AddWithValue("@crmk", s.ColRemark);
            cmd.Parameters.AddWithValue("@ipl", s.InPickList);
            cmd.Parameters.AddWithValue("@isup", s.InSupplier);
            cmd.Parameters.AddWithValue("@isys", s.InSysQr);
            cmd.Parameters.AddWithValue("@ixls", s.InExcel);
            cmd.Parameters.AddWithValue("@opl", s.OutPickList);
            cmd.Parameters.AddWithValue("@osup", s.OutSupplier);
            cmd.Parameters.AddWithValue("@osys", s.OutSysQr);
            cmd.Parameters.AddWithValue("@grp", s.GroupByCustomer ? "CUSTOMER" : "CATEGORY");
            cmd.Parameters.AddWithValue("@mmb", s.MaxMinInBox ? "BOX" : "UNIT");
            cmd.Parameters.AddWithValue("@ccust", s.ColCustomer);
            cmd.Parameters.AddWithValue("@cparta", s.ColPartA);
            cmd.Parameters.AddWithValue("@cpartno", s.ColPartNo);
            cmd.Parameters.AddWithValue("@cbox", s.ColStockBox);
            cmd.Parameters.AddWithValue("@cpcs", s.ColStockPcs);
            cmd.Parameters.AddWithValue("@cno", s.ColNo);
            cmd.Parameters.AddWithValue("@cmodel", s.ColModel);
            cmd.Parameters.AddWithValue("@oxls", s.OutExcel);
        }

        private static void SaveFormatLinks(SqlConnection conn, SqlTransaction trans, int stkId, IEnumerable<int> fmtIds)
        {
            foreach (int fmtId in (fmtIds ?? Enumerable.Empty<int>()).Distinct())
            {
                using (var cmd = new SqlCommand("INSERT INTO MST_STOCK_FMT (STK_ID, FMT_ID) VALUES (@s, @f)", conn, trans))
                {
                    cmd.Parameters.AddWithValue("@s", stkId);
                    cmd.Parameters.AddWithValue("@f", fmtId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // ลบคลังและข้อมูลทั้งหมดของคลังนั้น (sp_Stock_Delete บังคับเหตุผล + เก็บหลักฐานใน MST_STOCK_DEL_LOG)
        public void DeleteStock(int stkId, string reason, string userId)
        {
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand("sp_Stock_Delete", conn) { CommandType = CommandType.StoredProcedure })
            {
                cmd.Parameters.AddWithValue("@StkId", stkId);
                cmd.Parameters.AddWithValue("@Reason", reason ?? "");
                cmd.Parameters.AddWithValue("@UserId", userId);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        #endregion

        #region === [ Barcode Formats ] ===

        public List<BarcodeFormatModel> GetFormats(bool activeOnly = false)
        {
            var list = new List<BarcodeFormatModel>();
            using (var conn = new SqlConnection(_connectionString))
            {
                string sql = @"SELECT FMT_ID, FMT_NAME, DELIMITER, CODE_POS, ALT_CODE_POS, QTY_POS, MIN_FIELDS, SAMPLE_TEXT, IS_ACTIVE, SOURCE_STK_ID,
                                      DELIM_MODE, TRIM_CHARS, CODE_PREFIX, CODE_CUT, MATCH_START, MATCH_END
                               FROM MST_BARCODE_FMT" + (activeOnly ? " WHERE IS_ACTIVE = 1" : "") + " ORDER BY FMT_NAME";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    conn.Open();
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            list.Add(new BarcodeFormatModel
                            {
                                FmtId = Convert.ToInt32(rdr["FMT_ID"]),
                                Name = rdr["FMT_NAME"].ToString(),
                                Delimiter = rdr["DELIMITER"].ToString(),
                                CodePos = Convert.ToInt32(rdr["CODE_POS"]),
                                AltCodePos = rdr["ALT_CODE_POS"] == DBNull.Value ? (int?)null : Convert.ToInt32(rdr["ALT_CODE_POS"]),
                                QtyPos = rdr["QTY_POS"] == DBNull.Value ? (int?)null : Convert.ToInt32(rdr["QTY_POS"]),
                                MinFields = Convert.ToInt32(rdr["MIN_FIELDS"]),
                                SampleText = rdr["SAMPLE_TEXT"] == DBNull.Value ? "" : rdr["SAMPLE_TEXT"].ToString(),
                                IsActive = Convert.ToBoolean(rdr["IS_ACTIVE"]),
                                SourceStkId = rdr["SOURCE_STK_ID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rdr["SOURCE_STK_ID"]),
                                DelimMode = rdr["DELIM_MODE"].ToString(),
                                TrimChars = rdr["TRIM_CHARS"] == DBNull.Value ? null : rdr["TRIM_CHARS"].ToString(),
                                CodePrefix = rdr["CODE_PREFIX"].ToString(),
                                CodeCut = rdr["CODE_CUT"] == DBNull.Value ? (int?)null : Convert.ToInt32(rdr["CODE_CUT"]),
                                MatchStart = rdr["MATCH_START"] == DBNull.Value ? null : rdr["MATCH_START"].ToString(),
                                MatchEnd = rdr["MATCH_END"] == DBNull.Value ? null : rdr["MATCH_END"].ToString()
                            });
                        }
                    }
                }
            }
            return list;
        }

        public List<BarcodeFormatModel> GetFormatsForStock(StockModel stock)
        {
            if (stock == null || stock.FormatIds.Count == 0) return new List<BarcodeFormatModel>();
            return GetFormats(activeOnly: true).Where(f => stock.FormatIds.Contains(f.FmtId)).ToList();
        }

        public int SaveFormat(BarcodeFormatModel f, string userId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                string sql = f.FmtId == 0
                    ? @"INSERT INTO MST_BARCODE_FMT (FMT_NAME, DELIMITER, CODE_POS, ALT_CODE_POS, QTY_POS, MIN_FIELDS, SAMPLE_TEXT, IS_ACTIVE, SOURCE_STK_ID, CREATED_BY,
                                                     DELIM_MODE, TRIM_CHARS, CODE_PREFIX, CODE_CUT, MATCH_START, MATCH_END)
                        VALUES (@name, @delim, @code, @alt, @qty, @min, @sample, @active, @src, @uid, @dmode, @trim, @cpre, @ccut, @mstart, @mend);
                        SELECT CAST(SCOPE_IDENTITY() AS INT);"
                    : @"UPDATE MST_BARCODE_FMT SET FMT_NAME = @name, DELIMITER = @delim, CODE_POS = @code, ALT_CODE_POS = @alt,
                            QTY_POS = @qty, MIN_FIELDS = @min, SAMPLE_TEXT = @sample, IS_ACTIVE = @active, SOURCE_STK_ID = @src,
                            DELIM_MODE = @dmode, TRIM_CHARS = @trim, CODE_PREFIX = @cpre, CODE_CUT = @ccut, MATCH_START = @mstart, MATCH_END = @mend,
                            UPDATED_BY = @uid, UPDATED_DATE = GETDATE()
                        WHERE FMT_ID = @id;
                        SELECT @id;";

                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@id", f.FmtId);
                    cmd.Parameters.AddWithValue("@name", f.Name.Trim());
                    cmd.Parameters.AddWithValue("@delim", f.Delimiter);
                    cmd.Parameters.AddWithValue("@code", f.CodePos);
                    cmd.Parameters.AddWithValue("@alt", (object)f.AltCodePos ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@qty", (object)f.QtyPos ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@min", f.MinFields);
                    cmd.Parameters.AddWithValue("@sample", (object)f.SampleText ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@active", f.IsActive);
                    cmd.Parameters.AddWithValue("@src", (object)f.SourceStkId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@dmode", string.IsNullOrEmpty(f.DelimMode) ? "CHAR" : f.DelimMode);
                    cmd.Parameters.AddWithValue("@trim", string.IsNullOrEmpty(f.TrimChars) ? (object)DBNull.Value : f.TrimChars);
                    cmd.Parameters.AddWithValue("@cpre", string.IsNullOrEmpty(f.CodePrefix) ? "NONE" : f.CodePrefix);
                    cmd.Parameters.AddWithValue("@ccut", (object)f.CodeCut ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@mstart", string.IsNullOrWhiteSpace(f.MatchStart) ? (object)DBNull.Value : f.MatchStart.Trim());
                    cmd.Parameters.AddWithValue("@mend", string.IsNullOrWhiteSpace(f.MatchEnd) ? (object)DBNull.Value : f.MatchEnd.Trim());
                    cmd.Parameters.AddWithValue("@uid", userId);
                    conn.Open();
                    f.FmtId = Convert.ToInt32(cmd.ExecuteScalar());
                    return f.FmtId;
                }
            }
        }

        public void DeleteFormat(int fmtId)
        {
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand("DELETE FROM MST_BARCODE_FMT WHERE FMT_ID = @id", conn))
            {
                cmd.Parameters.AddWithValue("@id", fmtId);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        #endregion

        #region === [ Import Excel ] ===

        // หัวคอลัมน์ที่รองรับ (ไม่สนตัวพิมพ์เล็ก/ใหญ่ ช่องว่าง จุด วงเล็บ) - แบบฟอร์มจริงยังรอสรุป
        // จึงค้นหาหัวตารางเองจาก 10 แถวแรก แทนการล็อกตำแหน่งคอลัมน์ตายตัว
        private static readonly string[] CodeHeaders = { "PDCODE", "PARTCODE", "PRODUCTCODE", "CODE", "PTCODE", "ITEMCODE", "รหัสสินค้า", "รหัส" };
        private static readonly string[] QtyHeaders = { "QTY", "QUANTITY", "QTYKG", "QTYPCS", "KG", "จำนวน", "ยอด", "น้ำหนัก" };

        private static string NormalizeHeader(string h) =>
            new string((h ?? "").Where(c => !char.IsWhiteSpace(c) && c != '.' && c != '(' && c != ')' && c != '_' && c != '-').ToArray()).ToUpperInvariant();

        public List<StockImportRow> ReadImportExcel(string filePath)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException("ไม่พบไฟล์ที่เลือก", filePath);

            var rows = new List<StockImportRow>();
            // เปิดแบบแชร์ได้ กันกรณีไฟล์ยังเปิดค้างอยู่ใน Excel
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var wb = new XLWorkbook(fs))
            {
                var ws = wb.Worksheet(1);
                int codeCol = 0, qtyCol = 0, headerRow = 0;

                foreach (var row in ws.RowsUsed().Take(10))
                {
                    foreach (var cell in row.CellsUsed())
                    {
                        string h = NormalizeHeader(cell.GetString());
                        if (codeCol == 0 && CodeHeaders.Contains(h)) codeCol = cell.Address.ColumnNumber;
                        else if (qtyCol == 0 && QtyHeaders.Contains(h)) qtyCol = cell.Address.ColumnNumber;
                    }
                    if (codeCol > 0 && qtyCol > 0) { headerRow = row.RowNumber(); break; }
                    codeCol = qtyCol = 0;
                }

                if (headerRow == 0)
                    throw new InvalidDataException("ไม่พบหัวคอลัมน์ในไฟล์ Excel\nต้องมีคอลัมน์ \"PD CODE\" และ \"QTY\" (อยู่ใน 10 แถวแรกของชีทแรก)");

                int lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
                for (int r = headerRow + 1; r <= lastRow; r++)
                {
                    string code = ws.Cell(r, codeCol).GetString().Trim();
                    string qtyText = ws.Cell(r, qtyCol).GetString().Trim();
                    if (string.IsNullOrEmpty(code) && string.IsNullOrEmpty(qtyText)) continue;

                    var item = new StockImportRow { RowNumber = r, Code = code };
                    if (string.IsNullOrEmpty(code))
                        item.Error = "ไม่มีรหัสสินค้า";
                    else if (!decimal.TryParse(qtyText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal q) &&
                             !decimal.TryParse(qtyText, NumberStyles.Number, CultureInfo.CurrentCulture, out q))
                        item.Error = $"จำนวนไม่ถูกต้อง ({qtyText})";
                    else if (q <= 0)
                        item.Error = "จำนวนต้องมากกว่า 0";
                    else
                        item.Qty = q;

                    rows.Add(item);
                }
            }

            ValidateImportRows(rows);
            return rows;
        }

        // จับคู่รหัสในไฟล์กับ MST_PART (PT_CODE หรือช่อง QR Code ที่ลงทะเบียนไว้)
        private void ValidateImportRows(List<StockImportRow> rows)
        {
            var pending = rows.Where(r => string.IsNullOrEmpty(r.Error)).ToList();
            if (pending.Count == 0) return;

            var lookup = new Dictionary<string, (int Id, string Code)>(StringComparer.OrdinalIgnoreCase);
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand("SELECT PT_ID, LTRIM(RTRIM(PT_CODE)) AS PT_CODE, LTRIM(RTRIM(ISNULL(PT_QR, ''))) AS PT_QR FROM MST_PART WHERE IS_ACTIVE = 1", conn))
            {
                conn.Open();
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        int id = Convert.ToInt32(rdr["PT_ID"]);
                        string code = rdr["PT_CODE"].ToString();
                        string qr = rdr["PT_QR"].ToString();
                        lookup[code] = (id, code);
                        if (!string.IsNullOrEmpty(qr) && !lookup.ContainsKey(qr)) lookup[qr] = (id, code);
                    }
                }
            }

            foreach (var r in pending)
            {
                if (lookup.TryGetValue(r.Code, out var part)) { r.PartId = part.Id; r.PartCode = part.Code; }
                else r.Error = "ไม่พบรหัสสินค้านี้ในระบบ (ยังไม่ได้ลงทะเบียน)";
            }
        }

        // บวกจำนวนเพิ่มจากยอดเดิม (เดิม 100 + Import 100 = 200) ทุกแถวใน Transaction เดียว - พังแถวเดียวยกเลิกทั้งไฟล์
        // จำนวนเป็นจำนวนเต็มเหมือนยอดคลังเดิม (ทศนิยมปัดเป็นจำนวนเต็มที่ใกล้ที่สุด)
        public (int Items, int TotalQty) ApplyImport(int stkId, List<StockImportRow> rows, string fileName, string userId)
        {
            var valid = rows.Where(r => r.IsValid)
                            .GroupBy(r => r.PartId)
                            .Select(g => new { PartId = g.Key, PartCode = g.First().PartCode, Qty = (int)Math.Round(g.Sum(x => x.Qty), MidpointRounding.AwayFromZero) })
                            .Where(x => x.Qty > 0)
                            .ToList();
            if (valid.Count == 0) return (0, 0);

            Guid batch = Guid.NewGuid();
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    foreach (var v in valid)
                    {
                        int newBal;
                        using (var cmd = new SqlCommand("sp_Stock_AddQty", conn, trans) { CommandType = CommandType.StoredProcedure })
                        {
                            cmd.Parameters.AddWithValue("@StkId", stkId);
                            cmd.Parameters.AddWithValue("@PtId", v.PartId);
                            cmd.Parameters.AddWithValue("@Qty", v.Qty);
                            var outBal = cmd.Parameters.Add("@NewBal", SqlDbType.Int);
                            outBal.Direction = ParameterDirection.Output;
                            cmd.ExecuteNonQuery();
                            newBal = outBal.Value == DBNull.Value ? 0 : Convert.ToInt32(outBal.Value);
                        }

                        using (var cmd = new SqlCommand(@"INSERT INTO TRN_STOCK_IMPORT (BATCH_ID, STK_ID, PT_ID, PT_CODE, QTY, BAL_AFTER, FILE_NAME, USR_ID)
                                                          VALUES (@b, @s, @p, @c, @q, @bal, @f, @u)", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@b", batch);
                            cmd.Parameters.AddWithValue("@s", stkId);
                            cmd.Parameters.AddWithValue("@p", v.PartId);
                            cmd.Parameters.AddWithValue("@c", v.PartCode);
                            cmd.Parameters.AddWithValue("@q", v.Qty);
                            cmd.Parameters.AddWithValue("@bal", newBal);
                            cmd.Parameters.AddWithValue("@f", (object)fileName ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@u", userId);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    trans.Commit();
                }
            }
            return (valid.Count, valid.Sum(v => v.Qty));
        }

        #endregion
    }
}
