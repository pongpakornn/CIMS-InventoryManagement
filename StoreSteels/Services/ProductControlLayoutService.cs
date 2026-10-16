using CIMS.Core;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CIMS.Services
{
    // 🧩 หน้า Inventory Registration: ตารางของแต่ละคลัง (คอลัมน์ตามลำดับ + จัดกลุ่ม) และรูปแบบ QR ที่ Export
    //    เก็บใน CIMS.ProductControlSettings (Update_20261016c.sql) - StockID 0 = ALL STOCKS / ตั้งได้เฉพาะ Level 1 (ปุ่ม ADMIN)
    public class ProductControlLayout
    {
        public int StockId { get; set; }
        public List<string> Columns { get; set; } = new List<string>();
        public string GroupBy { get; set; } = "CATEGORY";    // CATEGORY / CUSTOMER / SUPPLIER / NONE
        public string QrContent { get; set; } = "CODE_NAME"; // CODE = PRODUCT CODE อย่างเดียว / CODE_NAME = PRODUCT CODE|PRODUCT NAME
        public bool QrCodeOnly => string.Equals(QrContent, "CODE", StringComparison.OrdinalIgnoreCase);

        public ProductControlLayout Clone() =>
            new ProductControlLayout { StockId = StockId, Columns = new List<string>(Columns), GroupBy = GroupBy, QrContent = QrContent };
    }

    public static class ProductControlColumns
    {
        // คีย์ -> หัวคอลัมน์ (ช่องติ๊กเลือก / หัวตาราง) - ACTION แสดงท้ายสุดเสมอ ช่องติ๊กเลือกแถวอยู่หน้าสุดเสมอ
        public static readonly (string Key, string Label)[] All =
        {
            ("IMAGE", "IMAGE"), ("SUPPLIER", "SUPPLIER"), ("CUSTOMER", "CUSTOMER"), ("CATEGORY", "CATEGORY"),
            ("PARTA", "PART A"), ("PARTB", "PART B"), ("MODEL", "MODEL"), ("CODE", "PRODUCT CODE"), ("PARTNO", "PART NO"),
            ("NAME", "PRODUCT NAME"), ("PACKSIZE", "PACKSIZE"), ("BIN", "BIN"), ("MOTHER", "MOTHER COIL"), ("COILNO", "COIL NO"),
            ("QRCODE", "QR CODE")
        };
        public static readonly string[] Groups = { "CATEGORY", "CUSTOMER", "SUPPLIER", "NONE" };

        // ตารางเดิมของหน้านี้ (ALL STOCKS / คลังที่ยังไม่ได้ตั้ง)
        public static readonly string[] DefaultOrder = { "IMAGE", "SUPPLIER", "CATEGORY", "CODE", "NAME", "PACKSIZE", "BIN", "QRCODE" };

        public static string Label(string key) => All.FirstOrDefault(c => c.Key == key).Label ?? key;

        public static List<string> Parse(string csv)
        {
            var list = new List<string>();
            foreach (var k in (csv ?? "").Split(','))
            {
                string key = k.Trim().ToUpperInvariant();
                if (key.Length > 0 && All.Any(c => c.Key == key) && !list.Contains(key)) list.Add(key);
            }
            return list;
        }

        public static ProductControlLayout Default(int stockId) =>
            new ProductControlLayout { StockId = stockId, Columns = DefaultOrder.ToList(), GroupBy = "CATEGORY", QrContent = "CODE_NAME" };
    }

    public class ProductControlLayoutService
    {
        private readonly string _cs = GlobalConfig.ConnStr;

        // ทุกคลังที่ตั้งไว้ (ยังไม่รัน Update_20261016c.sql = ว่าง -> ใช้ตารางเดิม)
        public Dictionary<int, ProductControlLayout> GetAll()
        {
            var map = new Dictionary<int, ProductControlLayout>();
            if (!CIMS.Helpers.DbSchema.HasProductControlSettings) return map;
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("SELECT StockID, ColumnOrder, GroupBy, QrContent FROM CIMS.ProductControlSettings", conn))
            {
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        int id = Convert.ToInt32(r[0]);
                        var cols = ProductControlColumns.Parse(r[1] == DBNull.Value ? null : r[1].ToString());
                        string grp = r[2] == DBNull.Value ? "" : r[2].ToString().Trim().ToUpperInvariant();
                        string qr = r[3] == DBNull.Value ? "" : r[3].ToString().Trim().ToUpperInvariant();
                        map[id] = new ProductControlLayout
                        {
                            StockId = id,
                            Columns = cols.Count > 0 ? cols : ProductControlColumns.DefaultOrder.ToList(),
                            GroupBy = ProductControlColumns.Groups.Contains(grp) ? grp : "CATEGORY",
                            QrContent = qr == "CODE" ? "CODE" : "CODE_NAME"
                        };
                    }
            }
            return map;
        }

        public ProductControlLayout Get(int stockId)
        {
            try { return GetAll().TryGetValue(stockId, out var l) ? l : ProductControlColumns.Default(stockId); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ProductControlLayout: {ex.Message}"); return ProductControlColumns.Default(stockId); }
        }

        public void Save(IEnumerable<ProductControlLayout> layouts, string userId)
        {
            if (!CIMS.Helpers.DbSchema.HasProductControlSettings)
                throw new InvalidOperationException("ฐานข้อมูลยังไม่รองรับการตั้งค่านี้ กรุณารัน Database/Update_20261016c.sql");
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    foreach (var l in layouts)
                        using (var cmd = new SqlCommand(@"
                            UPDATE CIMS.ProductControlSettings SET ColumnOrder = @c, GroupBy = @g, QrContent = @q, UpdatedBy = @u, UpdatedDate = GETDATE() WHERE StockID = @id;
                            IF @@ROWCOUNT = 0
                                INSERT INTO CIMS.ProductControlSettings (StockID, ColumnOrder, GroupBy, QrContent, UpdatedBy) VALUES (@id, @c, @g, @q, @u);", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@id", l.StockId);
                            cmd.Parameters.AddWithValue("@c", string.Join(",", l.Columns));
                            cmd.Parameters.AddWithValue("@g", l.GroupBy ?? "CATEGORY");
                            cmd.Parameters.AddWithValue("@q", l.QrCodeOnly ? "CODE" : "CODE_NAME");
                            cmd.Parameters.AddWithValue("@u", (object)userId ?? DBNull.Value);
                            cmd.ExecuteNonQuery();
                        }
                    trans.Commit();
                }
            }
        }

        // 🧲 Coil ที่อยู่ในคลัง (Status IN) ต่อสินค้า: Coil แม่ (ไม่ซ้ำ) / เลข Coil ลูก - stkId null = ทุกคลัง
        public Dictionary<int, (List<string> Mothers, List<string> Coils)> GetCoils(int? stkId, int partId = 0)
        {
            var map = new Dictionary<int, (List<string>, List<string>)>();
            if (!CIMS.Helpers.DbSchema.HasCoilRegister) return map;
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"SELECT PartID, MotherCoil, CoilNo FROM CIMS.Coils
                                              WHERE Status = 'IN' AND (@stk IS NULL OR StockID = @stk) AND (@pt = 0 OR PartID = @pt)
                                              ORDER BY PartID, MotherCoil, CoilNo", conn))
            {
                cmd.Parameters.AddWithValue("@stk", (object)stkId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@pt", partId);
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        int pid = Convert.ToInt32(r[0]);
                        if (!map.TryGetValue(pid, out var v)) map[pid] = v = (new List<string>(), new List<string>());
                        string m = r[1] == DBNull.Value ? "" : r[1].ToString().Trim();
                        if (m.Length > 0 && !v.Item1.Contains(m)) v.Item1.Add(m);
                        string c = r[2] == DBNull.Value ? "" : r[2].ToString().Trim();
                        if (c.Length > 0) v.Item2.Add(c);
                    }
            }
            return map;
        }

        // 🔎 ค่าที่มีในระบบแล้ว สำหรับช่อง SUPPLIER / CUSTOMER / CATEGORY (พิมพ์แล้วขึ้นให้เลือก)
        public (List<string> Suppliers, List<string> Customers, List<string> Categories) GetSuggestions()
        {
            List<string> Read(SqlConnection conn, string sql)
            {
                var list = new List<string>();
                using (var cmd = new SqlCommand(sql, conn))
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) if (r[0] != DBNull.Value && r[0].ToString().Trim().Length > 0) list.Add(r[0].ToString().Trim());
                return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                return (Read(conn, "SELECT DISTINCT CAST(Supplier AS NVARCHAR(200)) FROM CIMS.Parts WHERE IsActive = 1 ORDER BY 1"),
                        Read(conn, "SELECT DISTINCT Customer FROM CIMS.Parts WHERE IsActive = 1 ORDER BY 1"),
                        Read(conn, "SELECT DISTINCT Category FROM CIMS.Parts WHERE IsActive = 1 ORDER BY 1"));
            }
        }
    }
}
