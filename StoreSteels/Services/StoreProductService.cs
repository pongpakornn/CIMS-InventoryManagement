using Microsoft.Data.SqlClient;
using CIMS.Core;
using CIMS.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CIMS.Services
{
    public class StoreProductService
    {
        #region === [ Multi-Stock ] ===
        // อ่านข้อมูล: ทุกคลัง (รวมคลังหลัก) อ่านจาก CIMS.vw_StockMonitoring (WHERE StkId) ซึ่งมีคอลัมน์เสริม
        //   CUSTOMER / PART A / PART NO / STOCK BOX / STOCK PCS / GroupKey - แถวของคลังหลักเหมือน CIMS.vw_StoreMonitoring เดิม
        //   (ถ้ายังไม่ได้รัน Database/MultiStock.sql -> StkId = 0 -> ใช้ฟังก์ชันเดิมด้านล่าง)
        // แก้ไขข้อมูล: คลังหลัก = CIMS.Parts (ฟังก์ชันเดิม) / คลังอื่น = CIMS.PartStocks

        private static bool IsOtherStock(StockModel stock) => stock != null && !stock.IsMain;
        private static bool UseStockView(StockModel stock) => stock != null && stock.StkId > 0;

        public List<StoreProductModel> GetProducts(StockModel stock, string searchKeyword, string category, string filterType, int skip, int take)
        {
            if (!UseStockView(stock)) return GetProducts(searchKeyword, category, filterType, skip, take);
            return QueryProducts("CIMS.vw_StockMonitoring", stock.StkId, searchKeyword, category, filterType, skip, take, stock.AllowDecimal);
        }

        // ตัวเลือกกรองด้านบน (CATEGORY หรือ CUSTOMER ตามที่คลังจัดกลุ่ม)
        public List<string> GetCategories(StockModel stock)
        {
            if (!UseStockView(stock)) return GetCategories();

            var categories = new List<string> { "ALL CATEGORIES" };
            using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                string sql = @"SELECT DISTINCT GroupKey FROM CIMS.vw_StockMonitoring
                               WHERE StkId = @stk AND GroupKey IS NOT NULL AND GroupKey <> ''
                               ORDER BY GroupKey OPTION (MAX_GRANT_PERCENT = 5)";
                SqlCommand cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@stk", stock.StkId);
                conn.Open();
                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read()) categories.Add(rdr["GroupKey"].ToString());
                }
            }
            return categories;
        }

        public List<StoreProductModel> GetMinimalStockUpdates(StockModel stock, List<string> partCodes)
        {
            if (!UseStockView(stock)) return GetMinimalStockUpdates(partCodes);
            return QueryMinimalUpdates("CIMS.vw_StockMonitoring", stock.StkId, partCodes, stock.AllowDecimal);
        }

        // ⚡ เรียลไทม์แบบเบา: ค่าเดียวที่เปลี่ยนเมื่อยอด / MAX / MIN / REMARK / SHOW ของคลังนี้เปลี่ยน (นับแถว + CHECKSUM)
        //    ทุก 3 วินาทีถามแค่ค่านี้ (ไม่กี่ ms ไม่ส่งรหัสสินค้าไปเป็นพันตัว) - เปลี่ยนเมื่อไรค่อยดึงตัวเลขใหม่ทั้งคลังครั้งเดียว
        public string GetChangeToken(StockModel stock)
        {
            if (!UseStockView(stock)) return null;   // ระบบเก่า (ไม่มี vw_StockMonitoring) -> ใช้วิธีเดิม
            string sql = stock.IsMain
                ? @"SELECT COUNT(*), CHECKSUM_AGG(CHECKSUM(PartID, StockQuantity, MaxQuantity, MinQuantity, Remark, IsShowInMaster, IsActive, PackSize))
                    FROM CIMS.Parts"
                // ⏳ ชั่วคราว: คลังที่แสดงสดจาก StorePC -> ดูการเปลี่ยนแปลงในตารางของโปรแกรมเดิม
                : stock.IsLiveView
                ? @"SELECT COUNT(*), CHECKSUM_AGG(CHECKSUM(PT_ID, Quantity, BOXQ, QMAX, QMIN, RMK, PackSize, ImageFileName, Description))
                    FROM CIMS.vw_StorePcLive"
                : @"SELECT COUNT(*), CHECKSUM_AGG(CHECKSUM(ps.PartID, ps.Quantity, ps.BoxQuantity, ps.MaxQuantity, ps.MinQuantity, ps.Remark, ps.IsShow, p.PackSize, p.IsActive))
                    FROM CIMS.PartStocks ps JOIN CIMS.Parts p ON p.PartID = ps.PartID WHERE ps.StockID = @stk";
            using (var conn = new SqlConnection(GlobalConfig.ConnStr))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@stk", stock.StkId);
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    return r.Read() ? $"{r.GetInt32(0)}|{(r.IsDBNull(1) ? 0 : r.GetInt32(1))}" : "";
            }
        }

        // ตัวเลขล่าสุดของทั้งคลัง (เฉพาะคอลัมน์ที่เปลี่ยนได้) - ใช้ตอน GetChangeToken บอกว่ามีการเปลี่ยน
        public List<StoreProductModel> GetStockNumbers(StockModel stock) =>
            QueryMinimalUpdates("CIMS.vw_StockMonitoring", stock.StkId, null, stock.AllowDecimal);

        // box: แก้ STOCK (BOX) -> คลังอื่นเก็บ BoxQuantity (Trigger คำนวณ QTY = BOX x Pack Size) / คลังหลักแปลงเป็นจำนวน x Pack Size
        // ptId: แถวที่แก้ (PRODUCT CODE ซ้ำได้ถ้า PART A ต่างกัน) - 0 = หาจากรหัสแบบเดิม
        public bool UpdateProductMaster(StockModel stock, string partCode, string remark, decimal? max, decimal? min, decimal? qty, int? box = null, int ptId = 0)
        {
            if (!IsOtherStock(stock))
            {
                // คลังหลักที่นับ Coil: แก้ QTY (COIL) = แก้จำนวน Coil อย่างเดียว (ไม่แปลงเป็น KG)
                if (box.HasValue && stock?.CountCoil == true && CIMS.Helpers.DbSchema.HasCountCoil)
                {
                    using (var conn = new SqlConnection(GlobalConfig.ConnStr))
                    using (var cmd = new SqlCommand("UPDATE CIMS.Parts SET CoilQuantity = @c, Remark = @remark WHERE (@pt > 0 AND PartID = @pt) OR (@pt = 0 AND PartCode = @code)", conn))
                    {
                        cmd.Parameters.AddWithValue("@c", Math.Max(0, box.Value));
                        cmd.Parameters.AddWithValue("@remark", (object)remark ?? "");
                        cmd.Parameters.AddWithValue("@pt", ptId);
                        cmd.Parameters.AddWithValue("@code", partCode ?? "");
                        conn.Open();
                        cmd.ExecuteNonQuery();
                    }
                    box = null;
                }
                if (box.HasValue) qty = box.Value * Math.Max(1, GetPackSize(partCode, ptId));
                if (qty.HasValue) qty = CIMS.Helpers.Qty.Round(Math.Max(0, qty.Value), stock?.AllowDecimal == true);
                return UpdateProductMaster(partCode, remark, max, min, qty, ptId);
            }

            using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                StringBuilder sql = new StringBuilder("UPDATE ps SET ps.Remark = @remark, ps.UpdatedDate = GETDATE()");
                if (max.HasValue) sql.Append(", ps.MaxQuantity = @max");
                if (min.HasValue) sql.Append(", ps.MinQuantity = @min");
                // แก้กล่องอย่างเดียว ไม่ส่ง QTY ไปด้วย (ไม่งั้น Trigger ถือว่าแก้ทั้งคู่ จะไม่คำนวณชิ้นใหม่)
                if (box.HasValue) { sql.Append(", ps.BoxQuantity = @box"); qty = null; }
                if (qty.HasValue) sql.Append(", ps.Quantity = @qty");
                sql.Append(" FROM CIMS.PartStocks ps JOIN CIMS.Parts p ON p.PartID = ps.PartID WHERE ps.StockID = @stk AND ((@pt > 0 AND p.PartID = @pt) OR (@pt = 0 AND p.PartCode = @code))");

                SqlCommand cmd = new SqlCommand(sql.ToString(), conn);
                cmd.Parameters.AddWithValue("@remark", (object)remark ?? "");
                cmd.Parameters.AddWithValue("@code", partCode);
                cmd.Parameters.AddWithValue("@pt", ptId);
                cmd.Parameters.AddWithValue("@stk", stock.StkId);
                if (max.HasValue) cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@max", max.Value));
                if (min.HasValue) cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@min", min.Value));
                // ยอดคลัง: ทศนิยมตามการตั้งค่าคลัง (DECIMAL QTY) / ไม่ติดลบ (CHECK QTY >= 0 ในตาราง)
                if (qty.HasValue) cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@qty", CIMS.Helpers.Qty.Round(Math.Max(0, qty.Value), stock.AllowDecimal)));
                if (box.HasValue) cmd.Parameters.AddWithValue("@box", Math.Max(0, box.Value));

                conn.Open();
                cmd.ExecuteNonQuery();
                return true;
            }
        }

        private static int GetPackSize(string partCode, int ptId = 0)
        {
            using (var conn = new SqlConnection(GlobalConfig.ConnStr))
            using (var cmd = new SqlCommand("SELECT TOP 1 ISNULL(PackSize, 0) FROM CIMS.Parts WHERE (@pt > 0 AND PartID = @pt) OR (@pt = 0 AND PartCode = @c)", conn))
            {
                cmd.Parameters.AddWithValue("@c", partCode ?? "");
                cmd.Parameters.AddWithValue("@pt", ptId);
                conn.Open();
                object v = cmd.ExecuteScalar();
                return v == null || v == DBNull.Value ? 0 : Convert.ToInt32(v);
            }
        }

        public bool UpdateRemark(StockModel stock, string partCode, string remark, int ptId = 0)
        {
            if (!IsOtherStock(stock)) return UpdateRemark(partCode, remark, ptId);
            try
            {
                using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
                {
                    string sql = @"UPDATE ps SET ps.Remark = @remark, ps.UpdatedDate = GETDATE()
                                   FROM CIMS.PartStocks ps JOIN CIMS.Parts p ON p.PartID = ps.PartID
                                   WHERE ps.StockID = @stk AND ((@pt > 0 AND p.PartID = @pt) OR (@pt = 0 AND p.PartCode = @code))";
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@remark", (object)remark ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@code", partCode);
                    cmd.Parameters.AddWithValue("@pt", ptId);
                    cmd.Parameters.AddWithValue("@stk", stock.StkId);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DB Error in UpdateRemark(stock): {ex.Message}");
                return false;
            }
        }

        private List<StoreProductModel> QueryProducts(string viewName, int stkId, string searchKeyword, string category, string filterType, int skip, int take, bool dec = false)
        {
            var list = new List<StoreProductModel>();
            using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                StringBuilder sql = new StringBuilder($"SELECT * FROM {viewName} WHERE StkId = @stk ");

                if (!string.IsNullOrEmpty(searchKeyword))
                    sql.Append("AND (PartCode LIKE @key OR PartName LIKE @key OR Customer LIKE @key OR PartA LIKE @key OR PartNo LIKE @key OR Model LIKE @key) ");
                if (!string.IsNullOrEmpty(category))
                    sql.Append("AND GroupKey = @category ");
                if (filterType == "OVER_MAX")
                    sql.Append("AND StockStatus IN ('OVER_MAX', 'NORMAL_GOOD') AND ISNULL([Max], 0) > 0 ");
                else if (filterType == "UNDER_MIN")
                    sql.Append("AND StockStatus IN ('UNDER_MIN', 'OUT_OF_STOCK') AND ISNULL([Min], 0) > 0 ");

                sql.Append("ORDER BY GroupKey, PartCode OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY OPTION (MAX_GRANT_PERCENT = 5)");

                SqlCommand cmd = new SqlCommand(sql.ToString(), conn);
                cmd.Parameters.AddWithValue("@stk", stkId);
                if (!string.IsNullOrEmpty(searchKeyword)) cmd.Parameters.AddWithValue("@key", "%" + searchKeyword + "%");
                if (!string.IsNullOrEmpty(category)) cmd.Parameters.AddWithValue("@category", category);
                cmd.Parameters.AddWithValue("@Skip", skip);
                cmd.Parameters.AddWithValue("@Take", take);

                conn.Open();
                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    int rowNumber = skip + 1;
                    while (rdr.Read())
                    {
                        list.Add(new StoreProductModel
                        {
                            ID = (rowNumber++).ToString(),
                            PartId = Convert.ToInt32(rdr["PartID"]),
                            Category = rdr["Category"]?.ToString() ?? "",
                            Supplier = rdr["Supplier"]?.ToString() ?? "",
                            ImageFileName = rdr["ImageFileName"]?.ToString() ?? "",
                            PartCode = rdr["PartCode"]?.ToString() ?? "",
                            PartName = rdr["PartName"]?.ToString() ?? "",
                            PackSize = rdr["PackSize"]?.ToString() ?? "",
                            // ยอด / MAX / MIN ตามการตั้งค่าคลัง: DECIMAL QTY = 1234.50 / ปกติ = 1234
                            Max = rdr["Max"] == DBNull.Value ? "" : CIMS.Helpers.Qty.Edit(Convert.ToDecimal(rdr["Max"]), dec),
                            Min = rdr["Min"] == DBNull.Value ? "" : CIMS.Helpers.Qty.Edit(Convert.ToDecimal(rdr["Min"]), dec),
                            Qty = rdr["QtyStkb"] == DBNull.Value ? "" : CIMS.Helpers.Qty.Edit(Convert.ToDecimal(rdr["QtyStkb"]), dec),
                            Remark = rdr["Remark"]?.ToString() ?? "",
                            Bin = rdr["Bin"]?.ToString() ?? "",
                            Priority = int.TryParse(rdr["Priority"]?.ToString(), out int pri) ? pri : 0,
                            StockStatus = rdr["StockStatus"]?.ToString() ?? "NORMAL",
                            Customer = rdr["Customer"]?.ToString() ?? "",
                            PartA = rdr["PartA"]?.ToString() ?? "",
                            PartNo = rdr["PartNo"]?.ToString() ?? "",
                            Model = rdr["Model"]?.ToString() ?? "",
                            StockBox = FormatNum(rdr["StockBox"]),
                            StockPcs = CIMS.Helpers.Qty.Text(rdr["StockPcs"], dec),
                            GroupKey = rdr["GroupKey"]?.ToString() ?? ""
                        });
                    }
                }
            }
            return list;
        }

        // ตัดทศนิยมที่เป็น 0 ออก (เช่น 12.50 -> 12.5, 3.000 -> 3) / ไม่มีค่า -> "-"
        private static string FormatNum(object v)
        {
            if (v == null || v == DBNull.Value) return "-";
            return Convert.ToDecimal(v).ToString("#,0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        private List<StoreProductModel> QueryMinimalUpdates(string viewName, int stkId, List<string> partCodes, bool dec = false)
        {
            var list = new List<StoreProductModel>();
            // partCodes = null -> ทั้งคลัง / มีรายการ -> เฉพาะรหัสนั้น (SQL Server รับพารามิเตอร์ได้ไม่เกิน ~2,100 ตัว -> เกินนั้นดึงทั้งคลังแทน)
            bool all = partCodes == null || partCodes.Count > 1000;
            if (!all && partCodes.Count == 0) return list;

            using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                var paramNames = all ? new List<string>() : partCodes.Select((s, i) => $"@p{i}").ToList();
                string sql = $@"SELECT PartID, PartCode, ISNULL([Max], 0) AS [Max], ISNULL([Min], 0) AS [Min],
                                       ISNULL(QtyStkb, 0) AS QtyStkb, Remark, StockStatus, StockBox, StockPcs
                                FROM {viewName} WHERE StkId = @stk {(all ? "" : $"AND PartCode IN ({string.Join(",", paramNames)})")}
                                OPTION (MAX_GRANT_PERCENT = 5)";

                SqlCommand cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@stk", stkId);
                if (!all) for (int i = 0; i < partCodes.Count; i++) cmd.Parameters.AddWithValue($"@p{i}", partCodes[i]);

                conn.Open();
                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        list.Add(new StoreProductModel
                        {
                            PartId = Convert.ToInt32(rdr["PartID"]),
                            PartCode = rdr["PartCode"]?.ToString() ?? "",
                            Max = CIMS.Helpers.Qty.Edit(Convert.ToDecimal(rdr["Max"]), dec),
                            Min = CIMS.Helpers.Qty.Edit(Convert.ToDecimal(rdr["Min"]), dec),
                            Qty = CIMS.Helpers.Qty.Edit(Convert.ToDecimal(rdr["QtyStkb"]), dec),
                            Remark = rdr["Remark"]?.ToString() ?? "",
                            StockStatus = rdr["StockStatus"]?.ToString() ?? "NORMAL",
                            StockBox = FormatNum(rdr["StockBox"]),
                            StockPcs = CIMS.Helpers.Qty.Text(rdr["StockPcs"], dec)
                        });
                    }
                }
            }
            return list;
        }

        #endregion

        public List<StoreProductModel> GetProducts(
            string searchKeyword = "",
            string category = "",
            string filterType = "",
            int skip = 0,
            int take = 300)
        {
            var list = new List<StoreProductModel>();

            using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                StringBuilder sql = new StringBuilder(@"
                SELECT *
                FROM CIMS.vw_StoreMonitoring
                WHERE 1 = 1
                ");

                if (!string.IsNullOrEmpty(searchKeyword))
                    sql.Append("AND (PartCode LIKE @key OR PartName LIKE @key) ");

                if (!string.IsNullOrEmpty(category))
                    sql.Append(" AND Category = @category ");

                if (filterType == "OVER_MAX")
                {
                    sql.Append(@"
                    AND StockStatus IN ('OVER_MAX', 'NORMAL_GOOD')
                    AND ISNULL([Max], 0) > 0
                ");
                }
                else if (filterType == "UNDER_MIN")
                {
                    sql.Append(@"
                    AND StockStatus IN ('UNDER_MIN', 'OUT_OF_STOCK')
                    AND ISNULL([Min], 0) > 0
                ");
                }

                sql.Append("ORDER BY PartCode OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY");

                SqlCommand cmd = new SqlCommand(sql.ToString(), conn);

                if (!string.IsNullOrEmpty(searchKeyword))
                    cmd.Parameters.AddWithValue("@key", "%" + searchKeyword + "%");

                if (!string.IsNullOrEmpty(category))
                    cmd.Parameters.AddWithValue("@category", category);

                cmd.Parameters.AddWithValue("@Skip", skip);
                cmd.Parameters.AddWithValue("@Take", take);

                conn.Open();

                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    int rowNumber = skip + 1;

                    while (rdr.Read())
                    {
                        list.Add(new StoreProductModel
                        {
                            ID = (rowNumber++).ToString(),
                            Category = rdr["Category"]?.ToString() ?? "",
                            Supplier = rdr["Supplier"]?.ToString() ?? "",
                            ImageFileName = rdr["ImageFileName"]?.ToString() ?? "",
                            PartCode = rdr["PartCode"]?.ToString() ?? "",
                            PartName = rdr["PartName"]?.ToString() ?? "",
                            PackSize = rdr["PackSize"]?.ToString() ?? "",
                            // ยอด / MAX / MIN ตามการตั้งค่าคลัง: DECIMAL QTY = 1234.50 / ปกติ = 1234
                            Max = rdr["Max"] == DBNull.Value ? "" : CIMS.Helpers.Qty.Edit(Convert.ToDecimal(rdr["Max"]), false),
                            Min = rdr["Min"] == DBNull.Value ? "" : CIMS.Helpers.Qty.Edit(Convert.ToDecimal(rdr["Min"]), false),
                            Qty = rdr["QtyStkb"] == DBNull.Value ? "" : CIMS.Helpers.Qty.Edit(Convert.ToDecimal(rdr["QtyStkb"]), false),
                            Remark = rdr["Remark"]?.ToString() ?? "",
                            Bin = rdr["Bin"]?.ToString() ?? "",

                            // 👑 ใช้ TryParse แทน Convert.ToInt32 กัน FormatException
                            //    กรณี Status เป็น string หรือ null ใน DB
                            Priority = int.TryParse(rdr["Priority"]?.ToString(), out int pri) ? pri : 0,

                            // 👑 StockStatus เป็น string เสมอ ไม่ Parse เป็น int
                            StockStatus = rdr["StockStatus"]?.ToString() ?? "NORMAL"
                        });
                    }
                }
            }

            return list;
        }

        #region === [ Update Product Master ] ===

        public bool UpdateProductMaster(string partCode, string remark, decimal? max, decimal? min, decimal? qty, int ptId = 0)
        {
            using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                StringBuilder sql = new StringBuilder("UPDATE CIMS.Parts SET Remark = @remark");

                if (max.HasValue) sql.Append(", MaxQuantity = @max");
                if (min.HasValue) sql.Append(", MinQuantity = @min");
                if (qty.HasValue) sql.Append(", StockQuantity = @qty");

                // PRODUCT CODE ซ้ำได้ (PART A ต่างกัน) -> แก้แถวนี้ด้วย PartID
                sql.Append(" WHERE (@pt > 0 AND PartID = @pt) OR (@pt = 0 AND PartCode = @code)");

                SqlCommand cmd = new SqlCommand(sql.ToString(), conn);
                cmd.Parameters.AddWithValue("@remark", (object)remark ?? "");
                cmd.Parameters.AddWithValue("@code", partCode);
                cmd.Parameters.AddWithValue("@pt", ptId);

                if (max.HasValue) cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@max", max.Value));
                if (min.HasValue) cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@min", min.Value));
                if (qty.HasValue) cmd.Parameters.Add(CIMS.Helpers.QtyParam.Of("@qty", qty.Value));

                conn.Open();
                cmd.ExecuteNonQuery();
                return true;
            }
        }

        #endregion

        #region === [ Update Remark ] ===

        public bool UpdateRemark(string partCode, string remark, int ptId = 0)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
                {
                    string sql = "UPDATE CIMS.Parts SET Remark = @remark WHERE (@pt > 0 AND PartID = @pt) OR (@pt = 0 AND PartCode = @code)";
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@remark", (object)remark ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@code", partCode);
                    cmd.Parameters.AddWithValue("@pt", ptId);

                    conn.Open();
                    cmd.ExecuteNonQuery();
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DB Error in UpdateRemark: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region === [ Get Categories ] ===

        // เดิมชื่อ GetCustomers() ดึง Customer - schema ใหม่จัดกลุ่ม/กรองด้วย Category (ประเภท) แทน
        public List<string> GetCategories()
        {
            var categories = new List<string> { "ALL CATEGORIES" };
            using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                string sql = @"SELECT DISTINCT Category
                       FROM CIMS.Parts
                       WHERE IsActive = 1
                         AND IsShowInMaster = 1
                         AND Category IS NOT NULL
                         AND Category <> ''
                       ORDER BY Category";

                SqlCommand cmd = new SqlCommand(sql, conn);
                conn.Open();
                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                        categories.Add(rdr["Category"].ToString());
                }
            }
            return categories;
        }

        #endregion

        #region === [ Real-Time Stock Updates ] ===

        public List<StoreProductModel> GetMinimalStockUpdates(List<string> partCodes)
        {
            var list = new List<StoreProductModel>();
            if (partCodes == null || partCodes.Count == 0) return list;

            using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                StringBuilder sql = new StringBuilder(@"
                SELECT
                    PartCode,
                    ISNULL([Max], 0)   AS [Max],
                    ISNULL([Min], 0)   AS [Min],
                    ISNULL(QtyStkb, 0) AS QtyStkb,
                    Remark,
                    StockStatus
                FROM CIMS.vw_StoreMonitoring
                WHERE PartCode IN (");

                var paramNames = partCodes.Select((s, i) => $"@p{i}").ToList();
                sql.Append(string.Join(",", paramNames));
                sql.Append(")");

                SqlCommand cmd = new SqlCommand(sql.ToString(), conn);

                for (int i = 0; i < partCodes.Count; i++)
                    cmd.Parameters.AddWithValue($"@p{i}", partCodes[i]);

                conn.Open();
                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        list.Add(new StoreProductModel
                        {
                            PartCode = rdr["PartCode"]?.ToString() ?? "",
                            Max = CIMS.Helpers.Qty.Edit(Convert.ToDecimal(rdr["Max"]), false),
                            Min = CIMS.Helpers.Qty.Edit(Convert.ToDecimal(rdr["Min"]), false),
                            Qty = CIMS.Helpers.Qty.Edit(Convert.ToDecimal(rdr["QtyStkb"]), false),
                            Remark = rdr["Remark"]?.ToString() ?? "",
                            StockStatus = rdr["StockStatus"]?.ToString() ?? "NORMAL"
                        });
                    }
                }
            }
            return list;
        }

        #endregion
    }
}
