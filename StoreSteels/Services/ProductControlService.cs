using Dapper;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Data.SqlClient;
using CIMS.Core;
using CIMS.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace CIMS.Services
{
    public class ProductControlService
    {
        private readonly string _connectionString = GlobalConfig.ConnStr;

        // ดึงข้อมูลผ่าน Stored Procedure
        // schema ใหม่ตัด PartACode/Model/PT_NO ออกจาก CIMS.Parts แล้ว PartCode เป็นตัวระบุหลักตัวเดียว
        // PartACode ในหน้าจอนี้เลยแมปมาจาก PartCode เหมือนกัน (คงโครงสร้าง ProductControlModel/View เดิมไว้
        // เพราะไม่ได้อยู่ในสโคปที่ขอให้แก้รอบนี้) ส่วน ModelCode/PartNo ไม่มีคอลัมน์รองรับแล้วจึงเป็นค่าว่าง
        // stkId: แสดงเฉพาะสินค้าในคลังนั้น (null = ทุกคลัง)
        public List<ProductControlModel> GetInventoryForQR(string searchText, int? stkId = null)
        {
            var items = new List<ProductControlModel>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                using (SqlCommand cmd = new SqlCommand("CIMS.sp_GetPartForQR", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@SearchText", searchText ?? "");
                    if (stkId.HasValue) cmd.Parameters.AddWithValue("@StkId", stkId.Value);
                    conn.Open();
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            string code = rdr["PartCode"].ToString();
                            items.Add(new ProductControlModel
                            {
                                PtId = Convert.ToInt32(rdr["PartID"]),
                                PartCode = code,
                                PartName = rdr["Description"].ToString(),
                                PackSize = rdr["PackSize"].ToString(),
                                Category = rdr["Category"].ToString(),
                                Location = rdr["Bin"].ToString(),
                                QRCodeData = rdr["PT_QR_DISPLAY"].ToString(),
                                // ยอดเป็นทศนิยมได้ (DECIMAL 18,3) - ตัดศูนย์ท้ายออก (613.000 -> 613)
                                Max = rdr["MaxQuantity"] == DBNull.Value ? "" : CIMS.Helpers.Qty.Plain(Convert.ToDecimal(rdr["MaxQuantity"])),
                                Min = rdr["MinQuantity"] == DBNull.Value ? "" : CIMS.Helpers.Qty.Plain(Convert.ToDecimal(rdr["MinQuantity"])),
                                Stock = rdr["StockQuantity"] == DBNull.Value ? "" : CIMS.Helpers.Qty.Plain(Convert.ToDecimal(rdr["StockQuantity"])),

                                CustomerCode = rdr["Supplier"].ToString(),
                                ModelCode = "",
                                PartACode = code, // 🎯 ตัวระบุหลักไม่ซ้ำ (มิเรอร์จาก PartCode)
                                PartNo = rdr["PartNumber"]?.ToString() ?? "",
                                Customer = rdr["Customer"]?.ToString() ?? "",
                                PartA = rdr["PartA"]?.ToString() ?? "",
                                Model = rdr["Model"]?.ToString() ?? "",

                                ImageFileName = rdr["ImageFileName"] != DBNull.Value ? rdr["ImageFileName"].ToString() : null,
                                // เลือกคลังอื่นในช่องกรอง = SHOW/HIDE ของคลังนั้น / ทุกคลัง หรือคลังหลัก = แสดงในคลังหลัก
                                IsShow = Convert.ToBoolean(HasColumn(rdr, "IS_SHOW_VIEW") ? rdr["IS_SHOW_VIEW"] : rdr["IsShowInMaster"]),
                                IsShowMain = Convert.ToBoolean(rdr["IsShowInMaster"]),
                                IsActive = Convert.ToBoolean(rdr["IsActive"])
                            });
                        }
                    }
                }
            }
            return items;
        }

        // 🎯 ✅ แก้ไขระบบ UPDATE: เพิ่มคอลัมน์ CustomerCode, ModelCode, PartNo ให้แก้ไขค่าลงฐานข้อมูลได้ครบถ้วน
        //public async Task<bool> UpdateExistingPartAsync(
        //    string oldACode,
        //    string newACode,
        //    string code,
        //    string name,
        //    int psz,
        //    string qrData,
        //    int max,
        //    int min,
        //    string category,
        //    string uid,
        //    string imageFileName,
        //    string customerCode, // 👈 เพิ่มพารามิเตอร์รองรับ 1
        //    string modelCode,    // 👈 เพิ่มพารามิเตอร์รองรับ 2
        //    string partNo        // 👈 เพิ่มพารามิเตอร์รองรับ 3
        //)
        //{
        //    using (SqlConnection conn = new SqlConnection(_connectionString))
        //    {
        //        await conn.OpenAsync().ConfigureAwait(false);
        //        using (SqlTransaction trans = conn.BeginTransaction())
        //        {
        //            try
        //            {
        //                // 🎯 เพิ่มการ SET ค่าฟิลด์ Customer, Model, PT_NO ลงใน SQL Script
        //                string updateSql = @"UPDATE CIMS.Parts SET 
        //                                     PartACode = @NewACode,
        //                                     PartCode = @Code, 
        //                                     Description = @Name, 
        //                                     PackSize = @Psz, 
        //                                     QRCode = @QR,
        //                                     MaxQuantity = @Max, 
        //                                     MinQuantity = @Min, 
        //                                     Category = @Cat,
        //                                     ImageFileName = @ImageFileName,
        //                                     Customer = @CustomerCode,
        //                                     Model = @ModelCode,
        //                                     PT_NO = @PartNo
        //                                     WHERE PartACode = @OldACode";

        //                using (SqlCommand cmd = new SqlCommand(updateSql, conn, trans))
        //                {
        //                    cmd.Parameters.AddWithValue("@NewACode", newACode);
        //                    cmd.Parameters.AddWithValue("@Code", code ?? "");
        //                    cmd.Parameters.AddWithValue("@Name", name ?? "");
        //                    cmd.Parameters.AddWithValue("@Psz", psz);
        //                    cmd.Parameters.AddWithValue("@QR", qrData ?? "");
        //                    cmd.Parameters.AddWithValue("@Max", max);
        //                    cmd.Parameters.AddWithValue("@Min", min);
        //                    cmd.Parameters.AddWithValue("@Cat", category ?? "GENERAL");
        //                    cmd.Parameters.AddWithValue("@OldACode", oldACode);
        //                    cmd.Parameters.AddWithValue("@ImageFileName", (object)imageFileName ?? DBNull.Value);

        //                    // 👈 ผูก Parameter ข้อมูลกลุ่มใหม่เพิ่มเติม
        //                    cmd.Parameters.AddWithValue("@CustomerCode", (object)customerCode ?? DBNull.Value);
        //                    cmd.Parameters.AddWithValue("@ModelCode", (object)modelCode ?? DBNull.Value);
        //                    cmd.Parameters.AddWithValue("@PartNo", (object)partNo ?? DBNull.Value);

        //                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        //                }

        //                trans.Commit();
        //                return true;
        //            }
        //            catch (Exception ex)
        //            {
        //                trans.Rollback();
        //                System.Diagnostics.Debug.WriteLine($"Update Error: {ex.Message}");
        //                throw;
        //            }
        //        }
        //    }
        //}
        public async Task<bool> UpdateExistingPartAsync(
    string oldACode,
    string newACode,
    string code,
    string name,
    int psz,
    string qrData,
    string category,        // 👈 แก้ลำดับให้ถูกถ้าจำเป็น
    string uid,
    string imageFileName,
    string customerCode,
    string location,
    string customer = null,
    string partA = null,
    string partNo = null,
    string model = null,
    int ptId = 0          // แถวที่แก้ (PRODUCT CODE ซ้ำได้ถ้า PART A ต่างกัน) - 0 = หาจากรหัสเดิมแบบเดิม
)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (SqlTransaction trans = conn.BeginTransaction())
                {
                    try
                    {
                        // schema ใหม่ตัด PartACode/Model/PT_NO ออกจาก CIMS.Parts แล้ว PartCode เป็นตัวระบุหลัก
                        // ตัวเดียว (ค่าที่ ViewModel ส่งมาเป็น "newACode"/"oldACode" คือค่า PartACode ซึ่งมิเรอร์
                        // มาจาก PartCode ตอนโหลดข้อมูล จึงยังคงอ้างอิง/แก้ไข PartCode ผ่านค่านี้ได้ถูกต้อง)
                        string updateSql = @"UPDATE CIMS.Parts SET
                                     PartCode = @NewACode,
                                     Description = @Name,
                                     PackSize = @Psz,
                                     QRCode = @QR,
                                     Category = @Cat,
                                     ImageFileName = @ImageFileName,
                                     Supplier = @CustomerCode,
                                     Bin = @Location,
                                     Customer = @Cust,
                                     PartA = @PartA,
                                     PartNumber = @PartNo,
                                     Model = @Model
                                     WHERE (@PtId > 0 AND PartID = @PtId) OR (@PtId = 0 AND PartCode = @OldACode)";

                        using (SqlCommand cmd = new SqlCommand(updateSql, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@NewACode", newACode);
                            cmd.Parameters.AddWithValue("@Name", name ?? "");
                            cmd.Parameters.AddWithValue("@Psz", psz);
                            cmd.Parameters.AddWithValue("@QR", qrData ?? "");
                            cmd.Parameters.AddWithValue("@Cat", category ?? "GENERAL");
                            cmd.Parameters.AddWithValue("@OldACode", oldACode);
                            cmd.Parameters.AddWithValue("@PtId", ptId);
                            cmd.Parameters.AddWithValue("@ImageFileName", (object)imageFileName ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@CustomerCode", (object)customerCode ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Location", (object)location ?? DBNull.Value);
                            AddOptional(cmd, "@Cust", customer);
                            AddOptional(cmd, "@PartA", partA);
                            AddOptional(cmd, "@PartNo", partNo);
                            AddOptional(cmd, "@Model", model);

                            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }

                        trans.Commit();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        System.Diagnostics.Debug.WriteLine($"Update Error: {ex.Message}");
                        throw;
                    }
                }
            }
        }

        // เพิ่มข้อมูลใหม่ (INSERT)
        // คืน PartID ของสินค้าใหม่ (0 = ไม่สำเร็จ)
        public async Task<int> InsertNewPartAsync(string code, string name, int psz, string qrContent, decimal max, decimal min, string category, string imageFileName, string customerCode, string location,
                                                   string customer = null, string partA = null, string partNo = null, string model = null)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (SqlTransaction trans = conn.BeginTransaction())
                {
                    try
                    {
                        // schema ใหม่ตัด Model/PartACode/PT_NO ออกจาก CIMS.Parts แล้ว - ใช้ค่า @Code (model.PartCode)
                        // เป็นค่า PartCode เพียงตัวเดียว, PT_LOC เปลี่ยนชื่อเป็น Bin, QTY_STK เปลี่ยนเป็น StockQuantity
                        string insertSql = @"INSERT INTO CIMS.Parts (
                                        PartCode, Description, PackSize, QRCode, MaxQuantity, MinQuantity, Category,
                                        Bin, StockQuantity, IsActive, IsShowInMaster, ImageFileName,
                                        Supplier, Customer, PartA, PartNumber, Model
                                     )
                                     VALUES (
                                        @Code, @Name, @Psz, @QR, @Max, @Min, @Cat,
                                        @Location, 0, 1, 1, @ImageFileName,
                                        @CustomerCode, @Cust, @PartA, @PartNo, @Model
                                     ); SELECT CAST(SCOPE_IDENTITY() AS INT);";

                        using (SqlCommand cmd = new SqlCommand(insertSql, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@Code", code ?? "");
                            cmd.Parameters.AddWithValue("@Name", name ?? "");
                            cmd.Parameters.AddWithValue("@Psz", psz);
                            cmd.Parameters.AddWithValue("@QR", qrContent ?? "");
                            cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@Max", max));
                            cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@Min", min));
                            cmd.Parameters.AddWithValue("@Cat", category ?? "GENERAL");
                            cmd.Parameters.AddWithValue("@ImageFileName", (object)imageFileName ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@CustomerCode", (object)customerCode ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@Location", string.IsNullOrWhiteSpace(location) ? "N/A" : location);
                            AddOptional(cmd, "@Cust", customer);
                            AddOptional(cmd, "@PartA", partA);
                            AddOptional(cmd, "@PartNo", partNo);
                            AddOptional(cmd, "@Model", model);

                            int newId = Convert.ToInt32(await cmd.ExecuteScalarAsync().ConfigureAwait(false));
                            trans.Commit();
                            return newId;
                        }
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        System.Diagnostics.Debug.WriteLine($"Insert Error: {ex.Message}");
                        return 0;
                    }
                }
            }
        }

        // ช่องว่าง -> NULL
        private static void AddOptional(SqlCommand cmd, string name, string value) =>
            cmd.Parameters.Add(name, SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value.Trim();

        #region === [ Multi-Stock : สินค้านี้อยู่คลังไหนบ้าง ] ===
        // Stock-CHR = IsShowInMaster (แสดงในตารางคลังหลัก - ปุ่ม SHOW/HIDE เดิม) / คลังอื่น = มีแถวใน CIMS.PartStocks

        public List<int> GetPartStockIds(string partCode, int ptId = 0)
        {
            var ids = new List<int>();
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(@"SELECT ps.StockID FROM CIMS.PartStocks ps JOIN CIMS.Parts p ON p.PartID = ps.PartID
                                              WHERE (@pt > 0 AND p.PartID = @pt) OR (@pt = 0 AND p.PartCode = @code)", conn))
            {
                cmd.Parameters.AddWithValue("@code", partCode ?? "");
                cmd.Parameters.AddWithValue("@pt", ptId);
                conn.Open();
                using (var rdr = cmd.ExecuteReader())
                    while (rdr.Read()) ids.Add(Convert.ToInt32(rdr[0]));
            }
            return ids;
        }

        // ตั้งคลังของสินค้า: เพิ่มคลังที่ติ๊ก (ยอดเริ่ม 0) / เอาคลังที่เลิกติ๊กออก เฉพาะที่ยอดเป็น 0
        // คืนรายชื่อคลังที่เอาออกไม่ได้เพราะยังมียอดคงเหลืออยู่ (กันยอดหายเงียบๆ)
        public List<string> SyncPartStocks(string partCode, bool showInMain, IEnumerable<int> otherStkIds, int ptIdKnown = 0)
        {
            var keep = new HashSet<int>(otherStkIds ?? Array.Empty<int>());
            var blocked = new List<string>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    int ptId;
                    using (var cmd = new SqlCommand("SELECT TOP 1 PartID FROM CIMS.Parts WHERE (@pt > 0 AND PartID = @pt) OR (@pt = 0 AND PartCode = @code)", conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@code", partCode ?? "");
                        cmd.Parameters.AddWithValue("@pt", ptIdKnown);
                        object r = cmd.ExecuteScalar();
                        if (r == null) { trans.Rollback(); return blocked; }
                        ptId = Convert.ToInt32(r);
                    }

                    using (var cmd = new SqlCommand("UPDATE CIMS.Parts SET IsShowInMaster = @show WHERE PartID = @pt", conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@show", showInMain ? 1 : 0);
                        cmd.Parameters.AddWithValue("@pt", ptId);
                        cmd.ExecuteNonQuery();
                    }

                    // คลังที่มีอยู่ตอนนี้
                    var current = new List<(int StkId, string Code, int Qty)>();
                    using (var cmd = new SqlCommand(@"SELECT ps.StockID, s.StockCode, ps.Quantity FROM CIMS.PartStocks ps
                                                      JOIN CIMS.Stocks s ON s.StockID = ps.StockID WHERE ps.PartID = @pt", conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@pt", ptId);
                        using (var rdr = cmd.ExecuteReader())
                            while (rdr.Read()) current.Add((Convert.ToInt32(rdr[0]), rdr[1].ToString(), Convert.ToInt32(rdr[2])));
                    }

                    foreach (var c in current.Where(c => !keep.Contains(c.StkId)))
                    {
                        if (c.Qty > 0) { blocked.Add($"{c.Code} (คงเหลือ {c.Qty:N0})"); continue; }
                        using (var cmd = new SqlCommand("DELETE FROM CIMS.PartStocks WHERE StockID = @s AND PartID = @pt", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@s", c.StkId);
                            cmd.Parameters.AddWithValue("@pt", ptId);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    foreach (int stkId in keep.Where(id => !current.Any(c => c.StkId == id)))
                    {
                        using (var cmd = new SqlCommand(@"INSERT INTO CIMS.PartStocks (StockID, PartID, Quantity)
                                                          SELECT @s, @pt, 0 WHERE EXISTS (SELECT 1 FROM CIMS.Stocks WHERE StockID = @s AND IsMain = 0)", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@s", stkId);
                            cmd.Parameters.AddWithValue("@pt", ptId);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    trans.Commit();
                }
            }
            return blocked;
        }

        #endregion

        // schema ใหม่ตัดคอลัมน์ LAST_GEN_QR ออกจาก CIMS.Parts แล้ว (ไม่ได้ track เวลาที่ generate QR อีกต่อไป)
        // คงฟังก์ชันนี้ไว้เป็น no-op คืนค่า true เพื่อไม่ให้ ViewModel/View ที่เรียกอยู่พัง
        public bool UpdateQRCodeStatus(string partACode)
        {
            return true;
        }

        // 🎯 [แก้ไขจุดบั๊กหลัก] อัปเดต Show/Hide เฉพาะแถวโดยระบุเงื่อนไขด้วย PartCode
        // (schema ใหม่ตัด PartACode ออกแล้ว - partACode ที่รับเข้ามาเป็นค่ามิเรอร์จาก PartCode)
        private static bool HasColumn(SqlDataReader r, string name)
        {
            for (int i = 0; i < r.FieldCount; i++) if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // stkId = คลังที่เลือกในช่องกรอง: คลังอื่น -> SHOW/HIDE ในตารางคลังนั้น / null หรือคลังหลัก -> แสดงในคลังหลัก (เดิม)
        public bool UpdateShowStatus(string partACode, bool isShow, string uid, int? stkId = null, int ptId = 0)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (SqlTransaction trans = conn.BeginTransaction())
                {
                    try
                    {
                        string sql = @"IF @stk IS NOT NULL AND EXISTS (SELECT 1 FROM CIMS.Stocks WHERE StockID = @stk AND IsMain = 0)
                                           UPDATE ps SET IsShow = @IsShow, UpdatedDate = GETDATE() FROM CIMS.PartStocks ps JOIN CIMS.Parts p ON p.PartID = ps.PartID
                                           WHERE ps.StockID = @stk AND ((@PtId > 0 AND p.PartID = @PtId) OR (@PtId = 0 AND p.PartCode = @PartACode))
                                       ELSE
                                           UPDATE CIMS.Parts SET IsShowInMaster = @IsShow WHERE (@PtId > 0 AND PartID = @PtId) OR (@PtId = 0 AND PartCode = @PartACode)";
                        using (SqlCommand cmd = new SqlCommand(sql, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@IsShow", isShow ? 1 : 0);
                            cmd.Parameters.AddWithValue("@PartACode", partACode);
                            cmd.Parameters.AddWithValue("@stk", (object)stkId ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@PtId", ptId);
                            cmd.ExecuteNonQuery();
                        }
                        trans.Commit();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"UpdateShowStatus Error: {ex.Message}");
                        trans.Rollback();
                        return false;
                    }
                }
            }
        }

        // 🎯 ลบข้อมูลโดยอ้างอิงผ่าน PartCode (partACode มิเรอร์จาก PartCode)
        public bool DeletePart(string partACode, string uid) => DeletePart(partACode, uid, out _);
        public bool DeletePart(string partACode, string uid, out string error) => DeletePart(partACode, uid, out error, 0);

        // ลบสินค้า: ถ้ายังอยู่ในคลังอื่น (CIMS.PartStocks) และยอดเป็น 0 -> เอาออกจากคลังนั้นให้ในรายการเดียวกัน
        // ถ้าคลังไหนยังมียอดคงเหลือ -> ไม่ลบ และคืนข้อความบอกว่าติดคลังไหน (กันยอดหายเงียบๆ)
        // (ประวัติแสกน CIMS.ScanTransactions / CIMS.MaxMinPartConfigs ฐานข้อมูลลบตามให้เองอยู่แล้ว - FK แบบ CASCADE)
        // ptId > 0 = ลบเฉพาะแถวนั้น (PRODUCT CODE ซ้ำได้) / 0 = ตามรหัสแบบเดิม
        public bool DeletePart(string partACode, string uid, out string error, int ptId)
        {
            error = null;
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (SqlTransaction trans = conn.BeginTransaction())
                {
                    try
                    {
                        var withBalance = new List<string>();
                        using (SqlCommand cmd = new SqlCommand(@"SELECT s.StockCode, ps.Quantity FROM CIMS.PartStocks ps
                                                                 JOIN CIMS.Stocks s ON s.StockID = ps.StockID
                                                                 JOIN CIMS.Parts p ON p.PartID = ps.PartID
                                                                 WHERE ((@PtId > 0 AND p.PartID = @PtId) OR (@PtId = 0 AND p.PartCode = @PartACode)) AND ps.Quantity > 0", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@PartACode", partACode);
                            cmd.Parameters.AddWithValue("@PtId", ptId);
                            using (SqlDataReader rdr = cmd.ExecuteReader())
                                while (rdr.Read()) withBalance.Add($"{rdr[0]} (คงเหลือ {Convert.ToInt32(rdr[1]):N0})");
                        }

                        if (withBalance.Count > 0)
                        {
                            trans.Rollback();
                            error = "สินค้านี้ยังมียอดคงเหลืออยู่ในคลังต่อไปนี้\n\n• " + string.Join("\n• ", withBalance) +
                                    "\n\nกรุณาโอนย้ายหรือตัดยอดให้เป็น 0 ก่อนลบ";
                            return false;
                        }

                        string sql = @"DELETE ps FROM CIMS.PartStocks ps JOIN CIMS.Parts p ON p.PartID = ps.PartID
                                       WHERE (@PtId > 0 AND p.PartID = @PtId) OR (@PtId = 0 AND p.PartCode = @PartACode);
                                       DELETE FROM CIMS.Parts WHERE (@PtId > 0 AND PartID = @PtId) OR (@PtId = 0 AND PartCode = @PartACode);";
                        using (SqlCommand cmd = new SqlCommand(sql, conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@PartACode", partACode);
                            cmd.Parameters.AddWithValue("@PtId", ptId);
                            cmd.ExecuteNonQuery();
                        }
                        trans.Commit();
                        return true;
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        error = ex.Message;
                        return false;
                    }
                }
            }
        }

        // 🗑 ADMIN DELETE (Level 1): จำนวนสินค้าในแต่ละคลัง (คลังหลัก = แสดงในคลังหลัก IsShowInMaster)
        public Dictionary<int, int> GetStockPartCounts(IEnumerable<StockModel> stocks)
        {
            var map = new Dictionary<int, int>();
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                foreach (var s in stocks)
                {
                    using (var cmd = new SqlCommand(s.IsMain
                        ? "SELECT COUNT(*) FROM CIMS.Parts WHERE IsShowInMaster = 1"
                        : "SELECT COUNT(*) FROM CIMS.PartStocks WHERE StockID = @s", conn))
                    {
                        cmd.Parameters.AddWithValue("@s", s.StkId);
                        map[s.StkId] = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                }
            }
            return map;
        }

        // 🗑 ADMIN DELETE: ลบสินค้าออกจากระบบทั้งตัว (ทุกคลัง + ประวัติสแกน / Max-Min ของสินค้านั้น) แม้ยังมียอดคงเหลือ
        //   stocks = คลังที่เลือก / ptIds = รายการที่ติ๊ก (null = ทุกสินค้าในคลังที่เลือก)
        //   ลบเฉพาะสินค้าที่อยู่ในคลังที่เลือกเท่านั้น - ทั้งหมดอยู่ใน Transaction เดียว คืนรายการที่ลบ (รหัส | PART A)
        public List<string> AdminDeleteParts(IEnumerable<StockModel> stocks, IEnumerable<int> ptIds)
        {
            var stockList = stocks.ToList();
            var deleted = new List<string>();
            if (stockList.Count == 0) return deleted;

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        // สินค้าที่อยู่ในคลังที่เลือก
                        var inStocks = new HashSet<int>();
                        foreach (var s in stockList)
                        {
                            using (var cmd = new SqlCommand(s.IsMain
                                ? "SELECT PartID FROM CIMS.Parts WHERE IsShowInMaster = 1"
                                : "SELECT PartID FROM CIMS.PartStocks WHERE StockID = @s", conn, trans))
                            {
                                cmd.Parameters.AddWithValue("@s", s.StkId);
                                using (var r = cmd.ExecuteReader()) while (r.Read()) inStocks.Add(r.GetInt32(0));
                            }
                        }
                        var targets = ptIds == null ? inStocks.ToList() : ptIds.Where(inStocks.Contains).Distinct().ToList();
                        if (targets.Count == 0) { trans.Rollback(); return deleted; }

                        using (var cmd = new SqlCommand("CREATE TABLE #del (PartID INT PRIMARY KEY)", conn, trans)) cmd.ExecuteNonQuery();
                        foreach (var chunk in targets.Select((id, i) => new { id, i }).GroupBy(x => x.i / 900))
                        {
                            using (var cmd = new SqlCommand("INSERT INTO #del (PartID) VALUES " + string.Join(",", chunk.Select(x => $"({x.id})")), conn, trans))
                                cmd.ExecuteNonQuery();
                        }
                        using (var cmd = new SqlCommand("SELECT p.PartCode, ISNULL(p.PartA, '') FROM CIMS.Parts p JOIN #del d ON d.PartID = p.PartID ORDER BY p.PartCode", conn, trans))
                        using (var r = cmd.ExecuteReader())
                            while (r.Read()) deleted.Add(r.GetString(1).Length > 0 ? $"{r.GetString(0)} | {r.GetString(1)}" : r.GetString(0));

                        using (var cmd = new SqlCommand(@"
                            DELETE ps FROM CIMS.PartStocks ps JOIN #del d ON d.PartID = ps.PartID;
                            DELETE p FROM CIMS.Parts p JOIN #del d ON d.PartID = p.PartID;
                            DROP TABLE #del;", conn, trans) { CommandTimeout = 300 })
                            cmd.ExecuteNonQuery();

                        trans.Commit();
                    }
                    catch
                    {
                        trans.Rollback();
                        throw;
                    }
                }
            }
            return deleted;
        }

        // 🎯 ตรวจสอบค่าซ้ำในระบบผ่าน PartCode (partACode มิเรอร์จาก PartCode)
        // PRODUCT CODE ซ้ำได้ถ้า PART A ต่างกัน -> ซ้ำจริง = PRODUCT CODE + PART A เหมือนกัน (ไม่นับแถว exceptPtId ที่กำลังแก้)
        public async Task<bool> CheckDuplicateCodeAsync(string partACode, string partA = null, int exceptPtId = 0)
        {
            using (IDbConnection db = new SqlConnection(_connectionString))
            {
                string sql = @"SELECT COUNT(1) FROM CIMS.Parts WHERE PartCode = @PartACode
                               AND ISNULL(PartA, '') = ISNULL(@PartA, '') AND PartID <> @Except";
                int count = await db.ExecuteScalarAsync<int>(sql, new { PartACode = partACode ?? "", PartA = string.IsNullOrWhiteSpace(partA) ? null : partA.Trim(), Except = exceptPtId }).ConfigureAwait(false);
                return count > 0;
            }
        }
    }
}