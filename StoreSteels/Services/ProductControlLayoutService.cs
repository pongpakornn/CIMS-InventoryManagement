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
        // ▣ QR รูปแบบที่ 2 = สิ่งที่อยู่ใน QR (หน้าแสกนอ่าน) คั่นด้วย | ตามลำดับ - ต้องมี PRODUCT CODE เสมอ
        public List<string> QrFields { get; set; } = new List<string>(QrFormat.DefaultFields);
        // ▣ QR รูปแบบที่ 1 = ข้อความใต้ QR ใน PDF บรรทัดละช่อง (บรรทัดแรกตัวหนา)
        public List<string> QrLabel { get; set; } = new List<string>(QrFormat.DefaultLabel);
        // ค่าเดิม (แอปเวอร์ชันเก่า): CODE = PRODUCT CODE อย่างเดียว / CODE_NAME = อย่างอื่น
        public string QrContent => QrFields.Count == 1 && QrFields[0] == "CODE" ? "CODE" : "CODE_NAME";

        public ProductControlLayout Clone() =>
            new ProductControlLayout { StockId = StockId, Columns = new List<string>(Columns), GroupBy = GroupBy, QrFields = new List<string>(QrFields), QrLabel = new List<string>(QrLabel) };
    }

    // ▣ รูปแบบ QR ที่ EXPORT QR SELECT สร้าง (ตั้งต่อคลังที่ปุ่ม ADMIN > QR CODE)
    public static class QrFormat
    {
        public const char Sep = '|';
        public static readonly string[] DefaultFields = { "CODE", "NAME" };
        public static readonly string[] DefaultLabel = { "CODE", "NAME" };

        // ช่องที่เลือกได้ = คอลัมน์ของตาราง Inventory Registration (ไม่รวม IMAGE / QR CODE)
        public static IEnumerable<(string Key, string Label)> Fields => ProductControlColumns.All.Where(c => c.Key != "IMAGE" && c.Key != "QRCODE");

        public static List<string> Parse(string csv, string[] fallback)
        {
            var list = ProductControlColumns.Parse(csv).Where(k => k != "IMAGE" && k != "QRCODE").ToList();
            return list.Count > 0 ? list : fallback.ToList();
        }

        // ค่าของช่องจากสินค้า (ไม่มี = ว่าง) - ตัด | ในค่าออก กันตำแหน่งช่องเลื่อนตอนแสกน
        //   coils = Coil ที่อยู่ในคลัง (MOTHER COIL / COIL NO) จาก GetCoils - มี = ใช้เลข Coil ทั้งหมดคั่นด้วย ,
        public static string Value(CIMS.Models.ProductControlModel m, string key, IDictionary<int, (List<string> Mothers, List<string> Coils)> coils = null)
        {
            if ((key == "MOTHER" || key == "COILNO") && coils != null && coils.TryGetValue(m.PtId, out var cl))
                return string.Join(",", key == "MOTHER" ? cl.Mothers : cl.Coils).Replace(Sep, '/');
            string v;
            switch (key)
            {
                case "SUPPLIER": v = m.CustomerCode; break;
                case "CUSTOMER": v = m.Customer; break;
                case "CATEGORY": v = m.Category; break;
                case "PARTA": v = m.PartA; break;
                case "PARTB": v = m.PartB; break;
                case "MODEL": v = m.Model; break;
                case "CODE": v = m.PartCode; break;
                case "PARTNO": v = m.PartNo; break;
                case "NAME": v = m.PartName; break;
                case "PACKSIZE": v = m.PackSize; break;
                case "BIN": v = m.Location; break;
                case "MOTHER": v = m.MotherCoil; break;
                case "COILNO": v = m.CoilNo; break;
                default: v = null; break;
            }
            v = (v ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return v == "-" ? "" : v.Replace(Sep, '/');
        }

        // ข้อความใน QR เช่น CODE,NAME,PACKSIZE -> I2AB-01002|ถุงพลาสติก ...|100 (ช่องเดียว = ค่าอย่างเดียว ไม่มี |)
        public static string Content(CIMS.Models.ProductControlModel m, IList<string> fields, IDictionary<int, (List<string> Mothers, List<string> Coils)> coils = null)
        {
            IEnumerable<string> f = (fields == null || fields.Count == 0) ? DefaultFields : fields;
            string s = string.Join(Sep.ToString(), f.Select(k => Value(m, k, coils)));
            return s.Replace(Sep.ToString(), "").Length == 0 ? (m.PartCode ?? "N/A") : s;
        }

        // ตัวอย่างในหน้าตั้งค่า: แสดงชื่อช่องแทนค่า
        public static string Sample(IList<string> fields) =>
            string.Join(Sep.ToString(), fields.Select(k => ProductControlColumns.Label(k)));

        // ตำแหน่ง PRODUCT CODE ใน QR (เริ่ม 0) - ไม่มี = 0 (QR เดิม)
        public static int CodeIndex(IList<string> fields)
        {
            int i = fields == null ? -1 : fields.IndexOf("CODE");
            return i < 0 ? 0 : i;
        }
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
            new ProductControlLayout { StockId = stockId, Columns = DefaultOrder.ToList(), GroupBy = "CATEGORY" };
    }

    public class ProductControlLayoutService
    {
        private readonly string _cs = GlobalConfig.ConnStr;

        // ทุกคลังที่ตั้งไว้ (ยังไม่รัน Update_20261016c.sql = ว่าง -> ใช้ตารางเดิม)
        public Dictionary<int, ProductControlLayout> GetAll()
        {
            var map = new Dictionary<int, ProductControlLayout>();
            if (!CIMS.Helpers.DbSchema.HasProductControlSettings) return map;
            bool qf = CIMS.Helpers.DbSchema.HasQrFields;
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("SELECT StockID, ColumnOrder, GroupBy, QrContent" + (qf ? ", QrFields, QrLabel" : "") + " FROM CIMS.ProductControlSettings", conn))
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
                            QrFields = qf && r[4] != DBNull.Value ? QrFormat.Parse(r[4].ToString(), QrFormat.DefaultFields)
                                     : (qr == "CODE" ? new List<string> { "CODE" } : QrFormat.DefaultFields.ToList()),
                            QrLabel = qf && r[5] != DBNull.Value ? QrFormat.Parse(r[5].ToString(), QrFormat.DefaultLabel) : QrFormat.DefaultLabel.ToList()
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
            bool qf = CIMS.Helpers.DbSchema.HasQrFields;
            string upd = qf ? ", QrFields = @qf, QrLabel = @ql" : "", insCols = qf ? ", QrFields, QrLabel" : "", insVals = qf ? ", @qf, @ql" : "";
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    foreach (var l in layouts)
                        using (var cmd = new SqlCommand($@"
                            UPDATE CIMS.ProductControlSettings SET ColumnOrder = @c, GroupBy = @g, QrContent = @q{upd}, UpdatedBy = @u, UpdatedDate = GETDATE() WHERE StockID = @id;
                            IF @@ROWCOUNT = 0
                                INSERT INTO CIMS.ProductControlSettings (StockID, ColumnOrder, GroupBy, QrContent{insCols}, UpdatedBy) VALUES (@id, @c, @g, @q{insVals}, @u);", conn, trans))
                        {
                            cmd.Parameters.AddWithValue("@id", l.StockId);
                            cmd.Parameters.AddWithValue("@c", string.Join(",", l.Columns));
                            cmd.Parameters.AddWithValue("@g", l.GroupBy ?? "CATEGORY");
                            cmd.Parameters.AddWithValue("@q", l.QrContent);
                            if (qf)
                            {
                                cmd.Parameters.AddWithValue("@qf", string.Join(",", l.QrFields));
                                cmd.Parameters.AddWithValue("@ql", string.Join(",", l.QrLabel));
                            }
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

        // ▣ หน้าแสกน: รหัสไหนเป็น PRODUCT CODE ที่มีอยู่จริง (QR ระบบที่คลังตั้งรูปแบบต่างกัน)
        public HashSet<string> ExistingCodes(IEnumerable<string> codes)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = codes.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count == 0) return set;
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("", conn))
            {
                var names = new List<string>();
                for (int i = 0; i < list.Count; i++) { names.Add("@c" + i); cmd.Parameters.AddWithValue("@c" + i, list[i]); }
                cmd.CommandText = $"SELECT DISTINCT PartCode FROM CIMS.Parts WHERE PartCode IN ({string.Join(",", names)})";
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) if (r[0] != DBNull.Value) set.Add(r[0].ToString().Trim());
            }
            return set;
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
