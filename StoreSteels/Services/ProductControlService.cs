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
        // schema ใหม่ตัด PT_ACODE/PT_MODEL/PT_NO ออกจาก MST_PART แล้ว PT_CODE เป็นตัวระบุหลักตัวเดียว
        // PartACode ในหน้าจอนี้เลยแมปมาจาก PT_CODE เหมือนกัน (คงโครงสร้าง ProductControlModel/View เดิมไว้
        // เพราะไม่ได้อยู่ในสโคปที่ขอให้แก้รอบนี้) ส่วน ModelCode/PartNo ไม่มีคอลัมน์รองรับแล้วจึงเป็นค่าว่าง
        // stkId: แสดงเฉพาะสินค้าในคลังนั้น (null = ทุกคลัง)
        public List<ProductControlModel> GetInventoryForQR(string searchText, int? stkId = null)
        {
            var items = new List<ProductControlModel>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                using (SqlCommand cmd = new SqlCommand("sp_GetPartForQR", conn))
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.AddWithValue("@SearchText", searchText ?? "");
                    if (stkId.HasValue) cmd.Parameters.AddWithValue("@StkId", stkId.Value);
                    conn.Open();
                    using (SqlDataReader rdr = cmd.ExecuteReader())
                    {
                        while (rdr.Read())
                        {
                            string code = rdr["PT_CODE"].ToString();
                            items.Add(new ProductControlModel
                            {
                                PtId = Convert.ToInt32(rdr["PT_ID"]),
                                PartCode = code,
                                PartName = rdr["PT_DESC"].ToString(),
                                PackSize = rdr["PT_PSZ"].ToString(),
                                Category = rdr["PT_CAT"].ToString(),
                                Location = rdr["PT_BIN"].ToString(),
                                QRCodeData = rdr["PT_QR_DISPLAY"].ToString(),
                                Max = rdr["QTY_MAX"].ToString(),
                                Min = rdr["QTY_MIN"].ToString(),
                                Stock = rdr["QTY_STKB"].ToString(),

                                CustomerCode = rdr["PT_SUPPLIER"].ToString(),
                                ModelCode = "",
                                PartACode = code, // 🎯 ตัวระบุหลักไม่ซ้ำ (มิเรอร์จาก PT_CODE)
                                PartNo = rdr["PT_PARTNO"]?.ToString() ?? "",
                                Customer = rdr["PT_CUST"]?.ToString() ?? "",
                                PartA = rdr["PT_PARTA"]?.ToString() ?? "",
                                Model = rdr["PT_MODEL"]?.ToString() ?? "",

                                ImageFileName = rdr["PT_IMG"] != DBNull.Value ? rdr["PT_IMG"].ToString() : null,
                                // เลือกคลังอื่นในช่องกรอง = SHOW/HIDE ของคลังนั้น / ทุกคลัง หรือคลังหลัก = แสดงในคลังหลัก
                                IsShow = Convert.ToBoolean(HasColumn(rdr, "IS_SHOW_VIEW") ? rdr["IS_SHOW_VIEW"] : rdr["IS_SHOW_MST"]),
                                IsShowMain = Convert.ToBoolean(rdr["IS_SHOW_MST"]),
                                IsActive = Convert.ToBoolean(rdr["IS_ACTIVE"])
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
        //                // 🎯 เพิ่มการ SET ค่าฟิลด์ PT_CUST, PT_MODEL, PT_NO ลงใน SQL Script
        //                string updateSql = @"UPDATE MST_PART SET 
        //                                     PT_ACODE = @NewACode,
        //                                     PT_CODE = @Code, 
        //                                     PT_DESC = @Name, 
        //                                     PT_PSZ = @Psz, 
        //                                     PT_QR = @QR,
        //                                     QTY_MAX = @Max, 
        //                                     QTY_MIN = @Min, 
        //                                     PT_CAT = @Cat,
        //                                     PT_IMG = @ImageFileName,
        //                                     PT_CUST = @CustomerCode,
        //                                     PT_MODEL = @ModelCode,
        //                                     PT_NO = @PartNo
        //                                     WHERE PT_ACODE = @OldACode";

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
                        // schema ใหม่ตัด PT_ACODE/PT_MODEL/PT_NO ออกจาก MST_PART แล้ว PT_CODE เป็นตัวระบุหลัก
                        // ตัวเดียว (ค่าที่ ViewModel ส่งมาเป็น "newACode"/"oldACode" คือค่า PartACode ซึ่งมิเรอร์
                        // มาจาก PT_CODE ตอนโหลดข้อมูล จึงยังคงอ้างอิง/แก้ไข PT_CODE ผ่านค่านี้ได้ถูกต้อง)
                        string updateSql = @"UPDATE MST_PART SET
                                     PT_CODE = @NewACode,
                                     PT_DESC = @Name,
                                     PT_PSZ = @Psz,
                                     PT_QR = @QR,
                                     PT_CAT = @Cat,
                                     PT_IMG = @ImageFileName,
                                     PT_SUPPLIER = @CustomerCode,
                                     PT_BIN = @Location,
                                     PT_CUST = @Cust,
                                     PT_PARTA = @PartA,
                                     PT_PARTNO = @PartNo,
                                     PT_MODEL = @Model
                                     WHERE (@PtId > 0 AND PT_ID = @PtId) OR (@PtId = 0 AND PT_CODE = @OldACode)";

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
        // คืน PT_ID ของสินค้าใหม่ (0 = ไม่สำเร็จ)
        public async Task<int> InsertNewPartAsync(string code, string name, int psz, string qrContent, int max, int min, string category, string imageFileName, string customerCode, string location,
                                                   string customer = null, string partA = null, string partNo = null, string model = null)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (SqlTransaction trans = conn.BeginTransaction())
                {
                    try
                    {
                        // schema ใหม่ตัด PT_MODEL/PT_ACODE/PT_NO ออกจาก MST_PART แล้ว - ใช้ค่า @Code (model.PartCode)
                        // เป็นค่า PT_CODE เพียงตัวเดียว, PT_LOC เปลี่ยนชื่อเป็น PT_BIN, QTY_STK เปลี่ยนเป็น QTY_STKB
                        string insertSql = @"INSERT INTO MST_PART (
                                        PT_CODE, PT_DESC, PT_PSZ, PT_QR, QTY_MAX, QTY_MIN, PT_CAT,
                                        PT_BIN, QTY_STKB, IS_ACTIVE, IS_SHOW_MST, PT_IMG,
                                        PT_SUPPLIER, PT_CUST, PT_PARTA, PT_PARTNO, PT_MODEL
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
                            cmd.Parameters.AddWithValue("@Max", max);
                            cmd.Parameters.AddWithValue("@Min", min);
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
        // Stock-CHR = IS_SHOW_MST (แสดงในตารางคลังหลัก - ปุ่ม SHOW/HIDE เดิม) / คลังอื่น = มีแถวใน MST_PART_STOCK

        public List<int> GetPartStockIds(string partCode, int ptId = 0)
        {
            var ids = new List<int>();
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(@"SELECT ps.STK_ID FROM MST_PART_STOCK ps JOIN MST_PART p ON p.PT_ID = ps.PT_ID
                                              WHERE (@pt > 0 AND p.PT_ID = @pt) OR (@pt = 0 AND p.PT_CODE = @code)", conn))
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
                    using (var cmd = new SqlCommand("SELECT TOP 1 PT_ID FROM MST_PART WHERE (@pt > 0 AND PT_ID = @pt) OR (@pt = 0 AND PT_CODE = @code)", conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@code", partCode ?? "");
                        cmd.Parameters.AddWithValue("@pt", ptIdKnown);
                        object r = cmd.ExecuteScalar();
                        if (r == null) { trans.Rollback(); return blocked; }
                        ptId = Convert.ToInt32(r);
                    }

                    using (var cmd = new SqlCommand("UPDATE MST_PART SET IS_SHOW_MST = @show WHERE PT_ID = @pt", conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@show", showInMain ? 1 : 0);
                        cmd.Parameters.AddWithValue("@pt", ptId);
                        cmd.ExecuteNonQuery();
                    }

                    // คลังที่มีอยู่ตอนนี้
                    var current = new List<(int StkId, string Code, int Qty)>();
                    using (var cmd = new SqlCommand(@"SELECT ps.STK_ID, s.STK_CODE, ps.QTY FROM MST_PART_STOCK ps
                                                      JOIN MST_STOCK s ON s.STK_ID = ps.STK_ID WHERE ps.PT_ID = @pt", conn, trans))
                    {
                        cmd.Parameters.AddWithValue("@pt", ptId);
                        using (var rdr = cmd.ExecuteReader())
                            while (rdr.Read()) current.Add((Convert.ToInt32(rdr[0]), rdr[1].ToString(), Convert.ToInt32(rdr[2])));
                    }

                    foreach (var c in current.Where(c => !keep.Contains(c.StkId)))
                    {
                        if (c.Qty > 0) { blocked.Add($"{c.Code} (คงเหลือ {c.Qty:N0})"); continue; }
                        using (var cmd = new SqlCommand("DELETE FROM MST_PART_STOCK WHERE STK_ID = @s AND PT_ID = @pt", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@s", c.StkId);
                            cmd.Parameters.AddWithValue("@pt", ptId);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    foreach (int stkId in keep.Where(id => !current.Any(c => c.StkId == id)))
                    {
                        using (var cmd = new SqlCommand(@"INSERT INTO MST_PART_STOCK (STK_ID, PT_ID, QTY)
                                                          SELECT @s, @pt, 0 WHERE EXISTS (SELECT 1 FROM MST_STOCK WHERE STK_ID = @s AND IS_MAIN = 0)", conn, trans))
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

        // schema ใหม่ตัดคอลัมน์ LAST_GEN_QR ออกจาก MST_PART แล้ว (ไม่ได้ track เวลาที่ generate QR อีกต่อไป)
        // คงฟังก์ชันนี้ไว้เป็น no-op คืนค่า true เพื่อไม่ให้ ViewModel/View ที่เรียกอยู่พัง
        public bool UpdateQRCodeStatus(string partACode)
        {
            return true;
        }

        // 🎯 [แก้ไขจุดบั๊กหลัก] อัปเดต Show/Hide เฉพาะแถวโดยระบุเงื่อนไขด้วย PT_CODE
        // (schema ใหม่ตัด PT_ACODE ออกแล้ว - partACode ที่รับเข้ามาเป็นค่ามิเรอร์จาก PT_CODE)
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
                        string sql = @"IF @stk IS NOT NULL AND EXISTS (SELECT 1 FROM MST_STOCK WHERE STK_ID = @stk AND IS_MAIN = 0)
                                           UPDATE ps SET IS_SHOW = @IsShow, UPDATED_DATE = GETDATE() FROM MST_PART_STOCK ps JOIN MST_PART p ON p.PT_ID = ps.PT_ID
                                           WHERE ps.STK_ID = @stk AND ((@PtId > 0 AND p.PT_ID = @PtId) OR (@PtId = 0 AND p.PT_CODE = @PartACode))
                                       ELSE
                                           UPDATE MST_PART SET IS_SHOW_MST = @IsShow WHERE (@PtId > 0 AND PT_ID = @PtId) OR (@PtId = 0 AND PT_CODE = @PartACode)";
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

        // 🎯 ลบข้อมูลโดยอ้างอิงผ่าน PT_CODE (partACode มิเรอร์จาก PT_CODE)
        public bool DeletePart(string partACode, string uid) => DeletePart(partACode, uid, out _);
        public bool DeletePart(string partACode, string uid, out string error) => DeletePart(partACode, uid, out error, 0);

        // ลบสินค้า: ถ้ายังอยู่ในคลังอื่น (MST_PART_STOCK) และยอดเป็น 0 -> เอาออกจากคลังนั้นให้ในรายการเดียวกัน
        // ถ้าคลังไหนยังมียอดคงเหลือ -> ไม่ลบ และคืนข้อความบอกว่าติดคลังไหน (กันยอดหายเงียบๆ)
        // (ประวัติแสกน TRN_SCAN / MST_CALC_CONFIG ฐานข้อมูลลบตามให้เองอยู่แล้ว - FK แบบ CASCADE)
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
                        using (SqlCommand cmd = new SqlCommand(@"SELECT s.STK_CODE, ps.QTY FROM MST_PART_STOCK ps
                                                                 JOIN MST_STOCK s ON s.STK_ID = ps.STK_ID
                                                                 JOIN MST_PART p ON p.PT_ID = ps.PT_ID
                                                                 WHERE ((@PtId > 0 AND p.PT_ID = @PtId) OR (@PtId = 0 AND p.PT_CODE = @PartACode)) AND ps.QTY > 0", conn, trans))
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

                        string sql = @"DELETE ps FROM MST_PART_STOCK ps JOIN MST_PART p ON p.PT_ID = ps.PT_ID
                                       WHERE (@PtId > 0 AND p.PT_ID = @PtId) OR (@PtId = 0 AND p.PT_CODE = @PartACode);
                                       DELETE FROM MST_PART WHERE (@PtId > 0 AND PT_ID = @PtId) OR (@PtId = 0 AND PT_CODE = @PartACode);";
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

        // 🎯 ตรวจสอบค่าซ้ำในระบบผ่าน PT_CODE (partACode มิเรอร์จาก PT_CODE)
        // PRODUCT CODE ซ้ำได้ถ้า PART A ต่างกัน -> ซ้ำจริง = PRODUCT CODE + PART A เหมือนกัน (ไม่นับแถว exceptPtId ที่กำลังแก้)
        public async Task<bool> CheckDuplicateCodeAsync(string partACode, string partA = null, int exceptPtId = 0)
        {
            using (IDbConnection db = new SqlConnection(_connectionString))
            {
                string sql = @"SELECT COUNT(1) FROM MST_PART WHERE PT_CODE = @PartACode
                               AND ISNULL(PT_PARTA, '') = ISNULL(@PartA, '') AND PT_ID <> @Except";
                int count = await db.ExecuteScalarAsync<int>(sql, new { PartACode = partACode ?? "", PartA = string.IsNullOrWhiteSpace(partA) ? null : partA.Trim(), Except = exceptPtId }).ConfigureAwait(false);
                return count > 0;
            }
        }
    }
}