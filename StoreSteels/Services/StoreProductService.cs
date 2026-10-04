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
        // อ่านข้อมูล: ทุกคลัง (รวมคลังหลัก) อ่านจาก VW_StockMonitoring (WHERE StkId) ซึ่งมีคอลัมน์เสริม
        //   CUSTOMER / PART A / PART NO / STOCK BOX / STOCK PCS / GroupKey - แถวของคลังหลักเหมือน VW_StoreMonitoring เดิม
        //   (ถ้ายังไม่ได้รัน Database/MultiStock.sql -> StkId = 0 -> ใช้ฟังก์ชันเดิมด้านล่าง)
        // แก้ไขข้อมูล: คลังหลัก = MST_PART (ฟังก์ชันเดิม) / คลังอื่น = MST_PART_STOCK

        private static bool IsOtherStock(StockModel stock) => stock != null && !stock.IsMain;
        private static bool UseStockView(StockModel stock) => stock != null && stock.StkId > 0;

        public List<StoreProductModel> GetProducts(StockModel stock, string searchKeyword, string category, string filterType, int skip, int take)
        {
            if (!UseStockView(stock)) return GetProducts(searchKeyword, category, filterType, skip, take);
            return QueryProducts("VW_StockMonitoring", stock.StkId, searchKeyword, category, filterType, skip, take);
        }

        // ตัวเลือกกรองด้านบน (CATEGORY หรือ CUSTOMER ตามที่คลังจัดกลุ่ม)
        public List<string> GetCategories(StockModel stock)
        {
            if (!UseStockView(stock)) return GetCategories();

            var categories = new List<string> { "ALL CATEGORIES" };
            using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                string sql = @"SELECT DISTINCT GroupKey FROM VW_StockMonitoring
                               WHERE StkId = @stk AND GroupKey IS NOT NULL AND GroupKey <> ''
                               ORDER BY GroupKey";
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
            return QueryMinimalUpdates("VW_StockMonitoring", stock.StkId, partCodes);
        }

        // box: แก้ STOCK (BOX) -> คลังอื่นเก็บ QTY_BOX (Trigger คำนวณ QTY = BOX x Pack Size) / คลังหลักแปลงเป็นจำนวน x Pack Size
        // ptId: แถวที่แก้ (PRODUCT CODE ซ้ำได้ถ้า PART A ต่างกัน) - 0 = หาจากรหัสแบบเดิม
        public bool UpdateProductMaster(StockModel stock, string partCode, string remark, int? max, int? min, double? qty, int? box = null, int ptId = 0)
        {
            if (!IsOtherStock(stock))
            {
                if (box.HasValue) qty = box.Value * Math.Max(1, GetPackSize(partCode, ptId));
                return UpdateProductMaster(partCode, remark, max, min, qty, ptId);
            }

            using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                StringBuilder sql = new StringBuilder("UPDATE ps SET ps.REMARK = @remark, ps.UPDATED_DATE = GETDATE()");
                if (max.HasValue) sql.Append(", ps.QTY_MAX = @max");
                if (min.HasValue) sql.Append(", ps.QTY_MIN = @min");
                // แก้กล่องอย่างเดียว ไม่ส่ง QTY ไปด้วย (ไม่งั้น Trigger ถือว่าแก้ทั้งคู่ จะไม่คำนวณชิ้นใหม่)
                if (box.HasValue) { sql.Append(", ps.QTY_BOX = @box"); qty = null; }
                if (qty.HasValue) sql.Append(", ps.QTY = @qty");
                sql.Append(" FROM MST_PART_STOCK ps JOIN MST_PART p ON p.PT_ID = ps.PT_ID WHERE ps.STK_ID = @stk AND ((@pt > 0 AND p.PT_ID = @pt) OR (@pt = 0 AND p.PT_CODE = @code))");

                SqlCommand cmd = new SqlCommand(sql.ToString(), conn);
                cmd.Parameters.AddWithValue("@remark", (object)remark ?? "");
                cmd.Parameters.AddWithValue("@code", partCode);
                cmd.Parameters.AddWithValue("@pt", ptId);
                cmd.Parameters.AddWithValue("@stk", stock.StkId);
                if (max.HasValue) cmd.Parameters.AddWithValue("@max", max.Value);
                if (min.HasValue) cmd.Parameters.AddWithValue("@min", min.Value);
                // ยอดคลังเป็นจำนวนเต็ม (CHECK QTY >= 0 ในตาราง)
                if (qty.HasValue) cmd.Parameters.AddWithValue("@qty", (int)Math.Round(Math.Max(0, qty.Value), MidpointRounding.AwayFromZero));
                if (box.HasValue) cmd.Parameters.AddWithValue("@box", Math.Max(0, box.Value));

                conn.Open();
                cmd.ExecuteNonQuery();
                return true;
            }
        }

        private static int GetPackSize(string partCode, int ptId = 0)
        {
            using (var conn = new SqlConnection(GlobalConfig.ConnStr))
            using (var cmd = new SqlCommand("SELECT TOP 1 ISNULL(PT_PSZ, 0) FROM MST_PART WHERE (@pt > 0 AND PT_ID = @pt) OR (@pt = 0 AND PT_CODE = @c)", conn))
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
                    string sql = @"UPDATE ps SET ps.REMARK = @remark, ps.UPDATED_DATE = GETDATE()
                                   FROM MST_PART_STOCK ps JOIN MST_PART p ON p.PT_ID = ps.PT_ID
                                   WHERE ps.STK_ID = @stk AND ((@pt > 0 AND p.PT_ID = @pt) OR (@pt = 0 AND p.PT_CODE = @code))";
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

        private List<StoreProductModel> QueryProducts(string viewName, int stkId, string searchKeyword, string category, string filterType, int skip, int take)
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

                sql.Append("ORDER BY GroupKey, PartCode OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY");

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
                            PartId = Convert.ToInt32(rdr["PT_ID"]),
                            Category = rdr["Category"]?.ToString() ?? "",
                            Supplier = rdr["Supplier"]?.ToString() ?? "",
                            ImageFileName = rdr["ImageFileName"]?.ToString() ?? "",
                            PartCode = rdr["PartCode"]?.ToString() ?? "",
                            PartName = rdr["PartName"]?.ToString() ?? "",
                            PackSize = rdr["PackSize"]?.ToString() ?? "",
                            Max = rdr["Max"]?.ToString() ?? "",
                            Min = rdr["Min"]?.ToString() ?? "",
                            Qty = rdr["QtyStkb"]?.ToString() ?? "",
                            Remark = rdr["Remark"]?.ToString() ?? "",
                            Bin = rdr["Bin"]?.ToString() ?? "",
                            Priority = int.TryParse(rdr["Priority"]?.ToString(), out int pri) ? pri : 0,
                            StockStatus = rdr["StockStatus"]?.ToString() ?? "NORMAL",
                            Customer = rdr["Customer"]?.ToString() ?? "",
                            PartA = rdr["PartA"]?.ToString() ?? "",
                            PartNo = rdr["PartNo"]?.ToString() ?? "",
                            Model = rdr["Model"]?.ToString() ?? "",
                            StockBox = FormatNum(rdr["StockBox"]),
                            StockPcs = FormatNum(rdr["StockPcs"]),
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

        private List<StoreProductModel> QueryMinimalUpdates(string viewName, int stkId, List<string> partCodes)
        {
            var list = new List<StoreProductModel>();
            if (partCodes == null || partCodes.Count == 0) return list;

            using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                var paramNames = partCodes.Select((s, i) => $"@p{i}").ToList();
                string sql = $@"SELECT PT_ID, PartCode, ISNULL([Max], 0) AS [Max], ISNULL([Min], 0) AS [Min],
                                       ISNULL(QtyStkb, 0) AS QtyStkb, Remark, StockStatus, StockBox, StockPcs
                                FROM {viewName} WHERE StkId = @stk AND PartCode IN ({string.Join(",", paramNames)})";

                SqlCommand cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@stk", stkId);
                for (int i = 0; i < partCodes.Count; i++) cmd.Parameters.AddWithValue($"@p{i}", partCodes[i]);

                conn.Open();
                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        list.Add(new StoreProductModel
                        {
                            PartId = Convert.ToInt32(rdr["PT_ID"]),
                            PartCode = rdr["PartCode"]?.ToString() ?? "",
                            Max = rdr["Max"]?.ToString() ?? "0",
                            Min = rdr["Min"]?.ToString() ?? "0",
                            Qty = rdr["QtyStkb"]?.ToString() ?? "0",
                            Remark = rdr["Remark"]?.ToString() ?? "",
                            StockStatus = rdr["StockStatus"]?.ToString() ?? "NORMAL",
                            StockBox = FormatNum(rdr["StockBox"]),
                            StockPcs = FormatNum(rdr["StockPcs"])
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
                FROM VW_StoreMonitoring
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
                            Max = rdr["Max"]?.ToString() ?? "",
                            Min = rdr["Min"]?.ToString() ?? "",
                            Qty = rdr["QtyStkb"]?.ToString() ?? "",
                            Remark = rdr["Remark"]?.ToString() ?? "",
                            Bin = rdr["Bin"]?.ToString() ?? "",

                            // 👑 ใช้ TryParse แทน Convert.ToInt32 กัน FormatException
                            //    กรณี LIT_STAT เป็น string หรือ null ใน DB
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

        public bool UpdateProductMaster(string partCode, string remark, int? max, int? min, double? qty, int ptId = 0)
        {
            using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                StringBuilder sql = new StringBuilder("UPDATE MST_PART SET PT_REMARK = @remark");

                if (max.HasValue) sql.Append(", QTY_MAX = @max");
                if (min.HasValue) sql.Append(", QTY_MIN = @min");
                if (qty.HasValue) sql.Append(", QTY_STKB = @qty");

                // PRODUCT CODE ซ้ำได้ (PART A ต่างกัน) -> แก้แถวนี้ด้วย PT_ID
                sql.Append(" WHERE (@pt > 0 AND PT_ID = @pt) OR (@pt = 0 AND PT_CODE = @code)");

                SqlCommand cmd = new SqlCommand(sql.ToString(), conn);
                cmd.Parameters.AddWithValue("@remark", (object)remark ?? "");
                cmd.Parameters.AddWithValue("@code", partCode);
                cmd.Parameters.AddWithValue("@pt", ptId);

                if (max.HasValue) cmd.Parameters.AddWithValue("@max", max.Value);
                if (min.HasValue) cmd.Parameters.AddWithValue("@min", min.Value);
                if (qty.HasValue) cmd.Parameters.AddWithValue("@qty", qty.Value);

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
                    string sql = "UPDATE MST_PART SET PT_REMARK = @remark WHERE (@pt > 0 AND PT_ID = @pt) OR (@pt = 0 AND PT_CODE = @code)";
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

        // เดิมชื่อ GetCustomers() ดึง PT_CUST - schema ใหม่จัดกลุ่ม/กรองด้วย PT_CAT (ประเภท) แทน
        public List<string> GetCategories()
        {
            var categories = new List<string> { "ALL CATEGORIES" };
            using (SqlConnection conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                string sql = @"SELECT DISTINCT PT_CAT
                       FROM MST_PART
                       WHERE IS_ACTIVE = 1
                         AND IS_SHOW_MST = 1
                         AND PT_CAT IS NOT NULL
                         AND PT_CAT <> ''
                       ORDER BY PT_CAT";

                SqlCommand cmd = new SqlCommand(sql, conn);
                conn.Open();
                using (SqlDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                        categories.Add(rdr["PT_CAT"].ToString());
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
                FROM VW_StoreMonitoring
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
                            Max = rdr["Max"]?.ToString() ?? "0",
                            Min = rdr["Min"]?.ToString() ?? "0",
                            Qty = rdr["QtyStkb"]?.ToString() ?? "0",
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
