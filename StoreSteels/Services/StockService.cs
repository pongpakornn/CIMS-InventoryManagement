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
    // คลังสินค้า (CIMS.Stocks), รูปแบบบาร์โค้ด Supplier (CIMS.BarcodeFormats), โอนย้าย และ Import Excel
    // ตาราง/SP ทั้งหมดอยู่ใน Database/MultiStock.sql
    public class StockService
    {
        private readonly string _connectionString = GlobalConfig.ConnStr;

        #region === [ Stocks ] ===

        private const string StockColumns = @"
            s.StockID, s.StockCode, s.StockName, s.Unit, s.IsMain, s.UseMaxMin,
            s.ShowColImage, s.ShowColCode, s.ShowColName, s.ShowColQuantity, s.ShowColRemark,
            s.InPickList, s.InSupplier, s.InSystemQR, s.InExcel,
            s.OutPickList, s.OutSupplier, s.OutSystemQR, s.SortNo,
            s.GroupBy, s.MaxMinBasis, s.ShowColCustomer, s.ShowColPartA, s.ShowColPartNumber, s.ShowColStockBox, s.ShowColStockPcs,
            s.ShowColNo, s.ShowColModel, s.OutExcel";

        // คลังทั้งหมด (คลังหลักอยู่บนสุด) พร้อมจำนวนรายการสินค้าในแต่ละคลัง
        public List<StockModel> GetStocks()
        {
            var list = new List<StockModel>();
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                string sql = $@"SELECT {StockColumns}, ISNULL(v.ItemCount, 0) AS ItemCount
                                FROM CIMS.Stocks s
                                LEFT JOIN CIMS.vw_StockSummary v ON v.StockID = s.StockID
                                ORDER BY s.IsMain DESC, s.SortNo, s.StockCode";
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

        // 🧾 PR SETTINGS: คลังที่แสกนออกแล้วสร้าง PR อัตโนมัติ (CIMS.Stocks.AutoPR - Database/PRView.sql)
        public Dictionary<int, bool> GetPrAutoMap()
        {
            var map = new Dictionary<int, bool>();
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var chk = new SqlCommand("SELECT COL_LENGTH('CIMS.Stocks', 'AutoPR')", conn))
                    if (chk.ExecuteScalar() == DBNull.Value)
                        throw new InvalidOperationException("ฐานข้อมูลยังไม่รองรับการตั้งค่า PR อัตโนมัติ กรุณารัน Database/PRView.sql");

                using (var cmd = new SqlCommand("SELECT StockID, AutoPR FROM CIMS.Stocks", conn))
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
                        using (var cmd = new SqlCommand("UPDATE CIMS.Stocks SET AutoPR = @on, UpdatedBy = @u, UpdatedDate = GETDATE() WHERE StockID = @id", conn, trans))
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
                StkId = Convert.ToInt32(rdr["StockID"]),
                Code = rdr["StockCode"].ToString(),
                Name = rdr["StockName"].ToString(),
                Unit = rdr["Unit"].ToString(),
                IsMain = Convert.ToBoolean(rdr["IsMain"]),
                UseMaxMin = Convert.ToBoolean(rdr["UseMaxMin"]),
                ColImage = Convert.ToBoolean(rdr["ShowColImage"]),
                ColCode = Convert.ToBoolean(rdr["ShowColCode"]),
                ColName = Convert.ToBoolean(rdr["ShowColName"]),
                ColQty = Convert.ToBoolean(rdr["ShowColQuantity"]),
                ColRemark = Convert.ToBoolean(rdr["ShowColRemark"]),
                InPickList = Convert.ToBoolean(rdr["InPickList"]),
                InSupplier = Convert.ToBoolean(rdr["InSupplier"]),
                InSysQr = Convert.ToBoolean(rdr["InSystemQR"]),
                InExcel = Convert.ToBoolean(rdr["InExcel"]),
                OutPickList = Convert.ToBoolean(rdr["OutPickList"]),
                OutSupplier = Convert.ToBoolean(rdr["OutSupplier"]),
                OutSysQr = Convert.ToBoolean(rdr["OutSystemQR"]),
                SortNo = Convert.ToInt32(rdr["SortNo"]),
                GroupBy = rdr["GroupBy"].ToString(),
                MaxMinBasis = rdr["MaxMinBasis"].ToString(),
                ColCustomer = Convert.ToBoolean(rdr["ShowColCustomer"]),
                ColPartA = Convert.ToBoolean(rdr["ShowColPartA"]),
                ColPartNo = Convert.ToBoolean(rdr["ShowColPartNumber"]),
                ColStockBox = Convert.ToBoolean(rdr["ShowColStockBox"]),
                ColStockPcs = Convert.ToBoolean(rdr["ShowColStockPcs"]),
                ColNo = Convert.ToBoolean(rdr["ShowColNo"]),
                ColModel = Convert.ToBoolean(rdr["ShowColModel"]),
                OutExcel = Convert.ToBoolean(rdr["OutExcel"])
            };
        }

        private static void LoadFormatIds(SqlConnection conn, List<StockModel> stocks)
        {
            using (var cmd = new SqlCommand("SELECT StockID, FormatID FROM CIMS.StockBarcodeFormats", conn))
            using (var rdr = cmd.ExecuteReader())
            {
                var byId = stocks.ToDictionary(s => s.StkId);
                while (rdr.Read())
                {
                    if (byId.TryGetValue(Convert.ToInt32(rdr["StockID"]), out var s))
                        s.FormatIds.Add(Convert.ToInt32(rdr["FormatID"]));
                }
            }
        }

        public bool IsStockCodeTaken(string code, int exceptStkId)
        {
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand("SELECT COUNT(1) FROM CIMS.Stocks WHERE StockCode = @code AND StockID <> @id", conn))
            {
                cmd.Parameters.AddWithValue("@code", code.Trim());
                cmd.Parameters.AddWithValue("@id", exceptStkId);
                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        // สร้างคลังใหม่ + ผูกรูปแบบบาร์โค้ด + ให้สิทธิ์ VIEW ผู้ใช้ทุกคน (CIMS.sp_Stock_GrantViewAll) ใน Transaction เดียว
        public int CreateStock(StockModel s, string userId)
        {
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    string sql = @"
                        INSERT INTO CIMS.Stocks (StockCode, StockName, Unit, IsMain, UseMaxMin,
                            ShowColImage, ShowColCode, ShowColName, ShowColQuantity, ShowColRemark,
                            InPickList, InSupplier, InSystemQR, InExcel, OutPickList, OutSupplier, OutSystemQR,
                            GroupBy, MaxMinBasis, ShowColCustomer, ShowColPartA, ShowColPartNumber, ShowColStockBox, ShowColStockPcs, ShowColNo, ShowColModel, OutExcel,
                            SortNo, CreatedBy)
                        VALUES (@code, @name, @unit, 0, @maxmin,
                            @cimg, @ccode, @cname, @cqty, @crmk,
                            @ipl, @isup, @isys, @ixls, @opl, @osup, @osys,
                            @grp, @mmb, @ccust, @cparta, @cpartno, @cbox, @cpcs, @cno, @cmodel, @oxls,
                            (SELECT ISNULL(MAX(SortNo), 0) + 1 FROM CIMS.Stocks), @uid);
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";

                    int newId;
                    using (var cmd = new SqlCommand(sql, conn, trans))
                    {
                        AddStockParams(cmd, s);
                        cmd.Parameters.AddWithValue("@uid", userId);
                        newId = (int)cmd.ExecuteScalar();
                    }

                    SaveFormatLinks(conn, trans, newId, s.FormatIds);

                    using (var cmd = new SqlCommand("CIMS.sp_Stock_GrantViewAll", conn, trans) { CommandType = CommandType.StoredProcedure })
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
                    using (var cmd = new SqlCommand("SELECT StockCode FROM CIMS.Stocks WHERE StockID = @id", conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@id", s.StkId);
                        oldCode = cmd.ExecuteScalar()?.ToString();
                    }

                    string sql = @"
                        UPDATE CIMS.Stocks SET
                            StockCode = @code, StockName = @name, Unit = @unit, UseMaxMin = @maxmin,
                            ShowColImage = @cimg, ShowColCode = @ccode, ShowColName = @cname, ShowColQuantity = @cqty, ShowColRemark = @crmk,
                            InPickList = @ipl, InSupplier = @isup, InSystemQR = @isys, InExcel = @ixls,
                            OutPickList = @opl, OutSupplier = @osup, OutSystemQR = @osys,
                            GroupBy = @grp, MaxMinBasis = @mmb, ShowColCustomer = @ccust, ShowColPartA = @cparta,
                            ShowColPartNumber = @cpartno, ShowColStockBox = @cbox, ShowColStockPcs = @cpcs,
                                 ShowColNo = @cno, ShowColModel = @cmodel, OutExcel = @oxls,
                            UpdatedBy = @uid, UpdatedDate = GETDATE()
                        WHERE StockID = @id";

                    using (var cmd = new SqlCommand(sql, conn, trans))
                    {
                        AddStockParams(cmd, s);
                        cmd.Parameters.AddWithValue("@uid", userId);
                        cmd.Parameters.AddWithValue("@id", s.StkId);
                        cmd.ExecuteNonQuery();
                    }

                    // สิทธิ์ของคลังใช้รหัสคลังเป็น SystemID -> เปลี่ยนรหัสคลังแล้วต้องเปลี่ยนชื่อสิทธิ์ตามใน Transaction เดียวกัน
                    if (!string.IsNullOrEmpty(oldCode) && !string.Equals(oldCode, s.Code.Trim(), StringComparison.Ordinal))
                    {
                        using (var cmd = new SqlCommand("UPDATE CIMS.Permissions SET SystemID = @new WHERE SystemID = @old", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@new", s.Code.Trim());
                            cmd.Parameters.AddWithValue("@old", oldCode);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    using (var cmd = new SqlCommand("DELETE FROM CIMS.StockBarcodeFormats WHERE StockID = @id", conn, trans))
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
                using (var cmd = new SqlCommand("INSERT INTO CIMS.StockBarcodeFormats (StockID, FormatID) VALUES (@s, @f)", conn, trans))
                {
                    cmd.Parameters.AddWithValue("@s", stkId);
                    cmd.Parameters.AddWithValue("@f", fmtId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // ลบคลังและข้อมูลทั้งหมดของคลังนั้น (CIMS.sp_Stock_Delete บังคับเหตุผล + เก็บหลักฐานใน CIMS.StockDeleteLogs)
        public void DeleteStock(int stkId, string reason, string userId)
        {
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand("CIMS.sp_Stock_Delete", conn) { CommandType = CommandType.StoredProcedure })
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
                string sql = @"SELECT FormatID, FormatName, Delimiter, CodePosition, AltCodePosition, QuantityPosition, MinFields, SampleText, IsActive, SourceStockID,
                                      DelimiterMode, TrimChars, CodePrefix, CodeCut, MatchStart, MatchEnd
                               FROM CIMS.BarcodeFormats" + (activeOnly ? " WHERE IsActive = 1" : "") + " ORDER BY FormatName";
                using (var cmd = new SqlCommand(sql, conn))
                {
                    conn.Open();
                    using (var rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            list.Add(new BarcodeFormatModel
                            {
                                FmtId = Convert.ToInt32(rdr["FormatID"]),
                                Name = rdr["FormatName"].ToString(),
                                Delimiter = rdr["Delimiter"].ToString(),
                                CodePos = Convert.ToInt32(rdr["CodePosition"]),
                                AltCodePos = rdr["AltCodePosition"] == DBNull.Value ? (int?)null : Convert.ToInt32(rdr["AltCodePosition"]),
                                QtyPos = rdr["QuantityPosition"] == DBNull.Value ? (int?)null : Convert.ToInt32(rdr["QuantityPosition"]),
                                MinFields = Convert.ToInt32(rdr["MinFields"]),
                                SampleText = rdr["SampleText"] == DBNull.Value ? "" : rdr["SampleText"].ToString(),
                                IsActive = Convert.ToBoolean(rdr["IsActive"]),
                                SourceStkId = rdr["SourceStockID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rdr["SourceStockID"]),
                                DelimMode = rdr["DelimiterMode"].ToString(),
                                TrimChars = rdr["TrimChars"] == DBNull.Value ? null : rdr["TrimChars"].ToString(),
                                CodePrefix = rdr["CodePrefix"].ToString(),
                                CodeCut = rdr["CodeCut"] == DBNull.Value ? (int?)null : Convert.ToInt32(rdr["CodeCut"]),
                                MatchStart = rdr["MatchStart"] == DBNull.Value ? null : rdr["MatchStart"].ToString(),
                                MatchEnd = rdr["MatchEnd"] == DBNull.Value ? null : rdr["MatchEnd"].ToString()
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
                    ? @"INSERT INTO CIMS.BarcodeFormats (FormatName, Delimiter, CodePosition, AltCodePosition, QuantityPosition, MinFields, SampleText, IsActive, SourceStockID, CreatedBy,
                                                     DelimiterMode, TrimChars, CodePrefix, CodeCut, MatchStart, MatchEnd)
                        VALUES (@name, @delim, @code, @alt, @qty, @min, @sample, @active, @src, @uid, @dmode, @trim, @cpre, @ccut, @mstart, @mend);
                        SELECT CAST(SCOPE_IDENTITY() AS INT);"
                    : @"UPDATE CIMS.BarcodeFormats SET FormatName = @name, Delimiter = @delim, CodePosition = @code, AltCodePosition = @alt,
                            QuantityPosition = @qty, MinFields = @min, SampleText = @sample, IsActive = @active, SourceStockID = @src,
                            DelimiterMode = @dmode, TrimChars = @trim, CodePrefix = @cpre, CodeCut = @ccut, MatchStart = @mstart, MatchEnd = @mend,
                            UpdatedBy = @uid, UpdatedDate = GETDATE()
                        WHERE FormatID = @id;
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
            using (var cmd = new SqlCommand("DELETE FROM CIMS.BarcodeFormats WHERE FormatID = @id", conn))
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

        // จับคู่รหัสในไฟล์กับ CIMS.Parts (PartCode หรือช่อง QR Code ที่ลงทะเบียนไว้)
        private void ValidateImportRows(List<StockImportRow> rows)
        {
            var pending = rows.Where(r => string.IsNullOrEmpty(r.Error)).ToList();
            if (pending.Count == 0) return;

            var lookup = new Dictionary<string, (int Id, string Code)>(StringComparer.OrdinalIgnoreCase);
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand("SELECT PartID, LTRIM(RTRIM(PartCode)) AS PartCode, LTRIM(RTRIM(ISNULL(QRCode, ''))) AS QRCode FROM CIMS.Parts WHERE IsActive = 1", conn))
            {
                conn.Open();
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        int id = Convert.ToInt32(rdr["PartID"]);
                        string code = rdr["PartCode"].ToString();
                        string qr = rdr["QRCode"].ToString();
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
                        using (var cmd = new SqlCommand("CIMS.sp_Stock_AddQty", conn, trans) { CommandType = CommandType.StoredProcedure })
                        {
                            cmd.Parameters.AddWithValue("@StkId", stkId);
                            cmd.Parameters.AddWithValue("@PtId", v.PartId);
                            cmd.Parameters.AddWithValue("@Qty", v.Qty);
                            var outBal = cmd.Parameters.Add("@NewBal", SqlDbType.Int);
                            outBal.Direction = ParameterDirection.Output;
                            cmd.ExecuteNonQuery();
                            newBal = outBal.Value == DBNull.Value ? 0 : Convert.ToInt32(outBal.Value);
                        }

                        using (var cmd = new SqlCommand(@"INSERT INTO CIMS.StockImports (BatchID, StockID, PartID, PartCode, Quantity, BalanceAfter, FileName, UserID)
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
