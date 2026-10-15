using CIMS.Core;
using CIMS.Helpers;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CIMS.Services
{
    // 🏬 Multi-Scanner ที่ Admin กำหนดให้แต่ละ UserID (Update_20261015.sql)
    //    CIMS.UserScanStocks = คลังที่สแกนได้ + ★ คลังที่ตัดก่อนตอนสแกนออก / CIMS.AdminContacts = ผู้ติดต่อ Admin (ชื่อ / เบอร์ / ชื่อเล่น)
    //    DISPLAY ของแต่ละคลังใช้ CIMS.UserScanDisplays เดิม (ScanService.GetScanDisplayMap / SaveScanDisplayMap)
    public class ScannerSetupService
    {
        private readonly string _cs = GlobalConfig.ConnStr;

        public class Assignment
        {
            public List<int> StockIds { get; set; } = new List<int>();
            public int? PriorityId { get; set; }
            public List<int> DeductIds { get; set; } = new List<int>();   // คลังที่เห็นปุ่ม DEDUCTED / NOT DEDUCTED (Update_20261016.sql)
            public bool IsSet => StockIds.Count > 0;
        }

        public Assignment GetAssignment(string userId)
        {
            var a = new Assignment();
            if (!DbSchema.HasUserScanStocks || string.IsNullOrEmpty(userId)) return a;
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("SELECT StockID, IsPriority, " + (DbSchema.HasScanDeduct ? "ShowDeduct" : "CAST(0 AS bit) AS ShowDeduct") + " FROM CIMS.UserScanStocks WHERE UserID = @u ORDER BY StockID", conn))
            {
                cmd.Parameters.AddWithValue("@u", userId);
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        int id = Convert.ToInt32(r["StockID"]);
                        a.StockIds.Add(id);
                        if (Convert.ToBoolean(r["IsPriority"])) a.PriorityId = id;
                        if (Convert.ToBoolean(r["ShowDeduct"])) a.DeductIds.Add(id);
                    }
            }
            return a;
        }

        // ทุกผู้ใช้: UserID -> คลังที่กำหนด (ไว้แสดงบนการ์ดผู้ใช้ หน้า User Management)
        public Dictionary<string, Assignment> GetAllAssignments()
        {
            var map = new Dictionary<string, Assignment>(StringComparer.OrdinalIgnoreCase);
            if (!DbSchema.HasUserScanStocks) return map;
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("SELECT UserID, StockID, IsPriority FROM CIMS.UserScanStocks ORDER BY UserID, StockID", conn))
            {
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                    {
                        string u = r["UserID"].ToString();
                        if (!map.TryGetValue(u, out var a)) map[u] = a = new Assignment();
                        int id = Convert.ToInt32(r["StockID"]);
                        a.StockIds.Add(id);
                        if (Convert.ToBoolean(r["IsPriority"])) a.PriorityId = id;
                    }
            }
            return map;
        }

        // แทนที่คลังของผู้ใช้ทั้งชุด (ว่าง = ยังไม่ได้กำหนด -> สแกนไม่ได้)
        public void SaveAssignment(string userId, IEnumerable<int> stockIds, int? priorityId, string by, IEnumerable<int> deductIds = null)
        {
            var ids = (stockIds ?? Enumerable.Empty<int>()).Distinct().ToList();
            var deduct = new HashSet<int>(deductIds ?? Enumerable.Empty<int>());
            bool hasDeduct = DbSchema.HasScanDeduct;
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var tr = conn.BeginTransaction())
                {
                    using (var del = new SqlCommand("DELETE FROM CIMS.UserScanStocks WHERE UserID = @u", conn, tr))
                    {
                        del.Parameters.AddWithValue("@u", userId);
                        del.ExecuteNonQuery();
                    }
                    foreach (int id in ids)
                        using (var ins = new SqlCommand(hasDeduct
                                   ? "INSERT INTO CIMS.UserScanStocks (UserID, StockID, IsPriority, ShowDeduct, UpdatedBy) VALUES (@u, @s, @p, @d, @by)"
                                   : "INSERT INTO CIMS.UserScanStocks (UserID, StockID, IsPriority, UpdatedBy) VALUES (@u, @s, @p, @by)", conn, tr))
                        {
                            ins.Parameters.AddWithValue("@d", deduct.Contains(id));
                            ins.Parameters.AddWithValue("@u", userId);
                            ins.Parameters.AddWithValue("@s", id);
                            ins.Parameters.AddWithValue("@p", priorityId.HasValue && priorityId.Value == id);
                            ins.Parameters.AddWithValue("@by", (object)by ?? DBNull.Value);
                            ins.ExecuteNonQuery();
                        }
                    tr.Commit();
                }
            }
        }

        // 🔁 คลังที่ตัดสต็อกข้ามคลัง: คลังต้นทางของรูปแบบป้ายที่ตั้ง "ตัดยอดจากคลัง" + คลังที่รับป้ายรูปแบบนั้น (เปิดรับ Supplier)
        //    ใช้แสดงปุ่ม DEDUCTED / NOT DEDUCTED เฉพาะคลังกลุ่มนี้ (วันนี้ = STOCK-MAT รับ Panta แล้วตัด STOCK-PANTA)
        public static HashSet<int> DeductStockIds(IEnumerable<CIMS.Models.StockModel> stocks, IEnumerable<CIMS.Models.BarcodeFormatModel> formats)
        {
            var set = new HashSet<int>();
            var deductFormats = (formats ?? Enumerable.Empty<CIMS.Models.BarcodeFormatModel>()).Where(f => f.IsActive && f.SourceStkId.HasValue).ToList();
            foreach (var f in deductFormats)
            {
                set.Add(f.SourceStkId.Value);
                foreach (var s in stocks ?? Enumerable.Empty<CIMS.Models.StockModel>())
                    if (s.StkId != f.SourceStkId.Value && s.InSupplier && (s.FormatIds == null || s.FormatIds.Count == 0 || s.FormatIds.Contains(f.FmtId)))
                        set.Add(s.StkId);
            }
            return set;
        }
        // หน้า Multi-Scanner เช็คทุกไม่กี่วินาทีว่า Admin เปลี่ยนคลังให้หรือยัง
        public string AssignmentToken(string userId)
        {
            if (!DbSchema.HasUserScanStocks || string.IsNullOrEmpty(userId)) return "";
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"SELECT CAST(COUNT(*) AS varchar) + '|' + ISNULL(CAST(CHECKSUM_AGG(BINARY_CHECKSUM(*)) AS varchar), '')
                                                     + '|' + ISNULL((SELECT CAST(CHECKSUM_AGG(BINARY_CHECKSUM(StockID, DisplayField)) AS varchar) FROM CIMS.UserScanDisplays WHERE UserID = @u), '')
                                              FROM CIMS.UserScanStocks WHERE UserID = @u", conn))
            {
                cmd.Parameters.AddWithValue("@u", userId);
                conn.Open();
                return cmd.ExecuteScalar()?.ToString() ?? "";
            }
        }

        // ☎ ผู้ติดต่อ Admin (CIMS.AdminContacts) - หลายคนได้ เรียงตาม SortNo
        public class AdminContact
        {
            public string FullName { get; set; }
            public string Phone { get; set; }
            public string Nickname { get; set; }
            public string Text => ContactLine(FullName, Phone, Nickname);
        }

        public static string ContactLine(string name, string phone, string nick) =>
            $"{(name ?? "").Trim()} - {(phone ?? "").Trim()}" + (string.IsNullOrWhiteSpace(nick) ? "" : $" ({nick.Trim()})");

        public List<AdminContact> GetAdminContacts()
        {
            var list = new List<AdminContact>();
            if (!DbSchema.HasAdminContacts) return list;
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("SELECT FullName, Phone, Nickname FROM CIMS.AdminContacts ORDER BY SortNo, ContactID", conn))
            {
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new AdminContact { FullName = r["FullName"].ToString(), Phone = r["Phone"].ToString(), Nickname = r["Nickname"]?.ToString() });
            }
            return list;
        }

        // แทนที่รายชื่อผู้ติดต่อทั้งชุด (ตามลำดับที่ส่งมา)
        public void SaveAdminContacts(IList<AdminContact> contacts, string by)
        {
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var tr = conn.BeginTransaction())
                {
                    using (var del = new SqlCommand("DELETE FROM CIMS.AdminContacts", conn, tr)) del.ExecuteNonQuery();
                    int n = 0;
                    foreach (var c in contacts)
                        using (var ins = new SqlCommand("INSERT INTO CIMS.AdminContacts (FullName, Phone, Nickname, SortNo, UpdatedBy) VALUES (@n, @p, @k, @s, @by)", conn, tr))
                        {
                            ins.Parameters.AddWithValue("@n", c.FullName.Trim());
                            ins.Parameters.AddWithValue("@p", c.Phone.Trim());
                            ins.Parameters.AddWithValue("@k", string.IsNullOrWhiteSpace(c.Nickname) ? (object)DBNull.Value : c.Nickname.Trim());
                            ins.Parameters.AddWithValue("@s", ++n);
                            ins.Parameters.AddWithValue("@by", (object)by ?? DBNull.Value);
                            ins.ExecuteNonQuery();
                        }
                    tr.Commit();
                }
            }
        }

        // ข้อความติดต่อ Admin (หน้าสแกน / หน้าต่างแจ้งเตือน) - ไม่มีรายชื่อ = "กรุณาติดต่อ Admin"
        public static string ContactText(IEnumerable<AdminContact> list)
        {
            var lines = (list ?? Enumerable.Empty<AdminContact>()).Select(c => "☎ " + c.Text).ToList();
            return lines.Count == 0 ? "☎ Admin" : string.Join("\n", lines);
        }
    }
}