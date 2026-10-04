using Microsoft.Data.SqlClient;
using CIMS.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CIMS.Services
{
    // 👥 ผู้ใช้งาน 1 คน (MST_USER) สำหรับหน้า USER MANAGEMENT
    public class UserAccount
    {
        public string UserId { get; set; }
        public string Name { get; set; }
        public string Position { get; set; }       // ตำแหน่ง (USR_POS)
        public string Department { get; set; }     // หน่วยงาน (USR_DEPT)
        public string Section { get; set; }        // แผนก (USR_SECTION)
        public string Division { get; set; }       // ฝ่าย (USR_DIVISION)
        public int Level { get; set; } = 2;   // UserID ใหม่ = Level 2
        public bool IsOnline { get; set; }
        public bool IsLocked { get; set; }
        public bool IsMasterAdmin { get; set; }
        public DateTime? LastLogin { get; set; }
        public int PermCount { get; set; }

        public string Initials
        {
            get
            {
                // ตัวอักษรแรกของชื่อ + นามสกุล (ภาษาไทยนับสระ/วรรณยุกต์ติดกับพยัญชนะเป็นตัวเดียว)
                string s = string.IsNullOrWhiteSpace(Name) ? UserId ?? "?" : Name.Trim();
                string First(string w) => System.Globalization.StringInfo.GetNextTextElement(w, 0);
                var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                bool latin = s.All(ch => ch < 0x0E00 || ch > 0x0E7F);
                string ini = parts.Length >= 2 ? First(parts[0]) + First(parts[1])
                           : latin ? s.Substring(0, Math.Min(2, s.Length)) : First(s);
                return ini.ToUpperInvariant();
            }
        }
        public string LevelText => Level == 1 ? "LEVEL 1 • ADMIN" : Level == 2 ? "LEVEL 2 • MANAGER" : "LEVEL 3 • STAFF";
        public string LastLoginText => LastLogin.HasValue ? "Last login " + LastLogin.Value.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "Never logged in";
        public string StatusText => IsLocked ? "LOCKED" : IsOnline ? "ONLINE" : "OFFLINE";
        public string SubText => string.Join("  •  ", new[] { Position, Department }.Where(x => !string.IsNullOrWhiteSpace(x)));
        public string SubText2 => string.Join("  •  ", new[] { Section, Division }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    // สิทธิ์ 1 ระบบของผู้ใช้ (MST_PERM 1 แถว)
    public class UserPermRow
    {
        public string SysId { get; set; }          // ค่าที่โปรแกรมตรวจสิทธิ์ (เขียนลง MST_PERM.SYS_ID)
        public string Title { get; set; }
        public string Group { get; set; }          // SYSTEM / STOCK
        public string Hint { get; set; }           // ADD/EDIT/DEL ใช้ทำอะไรในระบบนี้
        public string GroupTitle { get; set; }     // หัวกลุ่ม (แถวแรกของกลุ่มเท่านั้น)
        public bool View { get; set; }
        public bool Add { get; set; }
        public bool Edit { get; set; }
        public bool Delete { get; set; }
        public bool Approve { get; set; }
    }

    public class UserAdminService
    {
        private readonly string _cs = GlobalConfig.ConnStr;

        // ทุกระบบที่โปรแกรมตรวจสิทธิ์ (SYS_ID ต้องตรงกับที่ UserSession ใช้) - คลังอื่นเพิ่มให้อัตโนมัติจาก MST_STOCK
        public static readonly (string SysId, string Title, string Hint)[] Systems =
        {
            ("DASHBOARD",   "Dashboard",                  "VIEW = open the page"),
            ("STORE",       "Store (Max-Min)",            "VIEW = open + stocks (read only)  •  ADD = add stock / barcode format  •  EDIT = stock settings / barcode formats  •  DEL = delete stock"),
            ("SCANNER",     "Multi-Scanner",              "VIEW = open / history / test mode  •  ADD = scan in + return  •  EDIT = scan out"),
            ("PDControl",   "Inventory Registration",     "VIEW = open + export QR  •  ADD = register / import  •  EDIT = edit / show-hide / image  •  DEL = delete"),
            ("PackingCard", "Pick List",                  "VIEW = open the page"),
            ("MAXMINCALC",  "Max-Min Calculator",         "VIEW = open  •  EDIT = edit / import / calculate"),
            ("PR",          "PR Management",              "VIEW = open + approved list  •  ADD = submit  •  EDIT = edit / PR settings  •  DEL = delete  •  APPROVE = approve / reject / export"),
            ("ActivityLog", "Activity Log",               "VIEW = open the page"),
            ("USERMGMT",    "User Management",            "VIEW = open  •  ADD / EDIT / DEL users  •  APPROVE = set permissions")
        };

        public List<UserAccount> GetUsers()
        {
            var list = new List<UserAccount>();
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"
                SELECT u.USR_ID, u.USR_NAME, u.USR_POS, u.USR_DEPT, u.USR_SECTION, u.USR_DIVISION, ISNULL(u.USR_LVL, 3) AS LVL, ISNULL(u.IS_ONLINE, 0) AS ONL,
                       ISNULL(u.IS_LOCKED, 0) AS LCK, ISNULL(u.IS_MASTER_ADMIN, 0) AS MA, u.LAST_LOGIN,
                       (SELECT COUNT(*) FROM MST_PERM p WHERE p.USR_ID = u.USR_ID AND p.PERM_VIEW = 'Y') AS PERMS
                FROM MST_USER u ORDER BY ISNULL(u.USR_LVL, 3), u.USR_ID", conn))
            {
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new UserAccount
                        {
                            UserId = r["USR_ID"].ToString(),
                            Name = r["USR_NAME"]?.ToString(),
                            Position = r["USR_POS"]?.ToString(),
                            Department = r["USR_DEPT"]?.ToString(),
                            Section = r["USR_SECTION"]?.ToString(),
                            Division = r["USR_DIVISION"]?.ToString(),
                            Level = Convert.ToInt32(r["LVL"]),
                            IsOnline = Convert.ToBoolean(r["ONL"]),
                            IsLocked = Convert.ToBoolean(r["LCK"]),
                            IsMasterAdmin = Convert.ToBoolean(r["MA"]),
                            LastLogin = r["LAST_LOGIN"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["LAST_LOGIN"]),
                            PermCount = Convert.ToInt32(r["PERMS"])
                        });
            }
            return list;
        }

        public bool Exists(string userId)
        {
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM MST_USER WHERE USR_ID = @u", conn))
            {
                cmd.Parameters.AddWithValue("@u", userId);
                conn.Open();
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        // เพิ่มผู้ใช้ (Trigger trg_MST_USER_GrantStockView ให้สิทธิ์ VIEW หน้า Store (Max-Min) + ทุกคลังรวมคลังหลักให้เอง)
        public void AddUser(UserAccount u, string password)
        {
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"INSERT INTO MST_USER (USR_ID, USR_PWD, USR_NAME, USR_POS, USR_DEPT, USR_SECTION, USR_DIVISION, USR_LVL, IS_ONLINE, IS_LOCKED, IS_MASTER_ADMIN)
                                              VALUES (@u, @p, @n, @pos, @d, @sec, @div, @l, 0, @lock, 0)", conn))
            {
                Fill(cmd, u);
                cmd.Parameters.AddWithValue("@p", password ?? "");
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        // แก้ไขผู้ใช้ (password ว่าง = ไม่เปลี่ยนรหัสผ่าน)
        public void UpdateUser(UserAccount u, string newPassword)
        {
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"UPDATE MST_USER SET USR_NAME = @n, USR_POS = @pos, USR_DEPT = @d, USR_SECTION = @sec, USR_DIVISION = @div, USR_LVL = @l, IS_LOCKED = @lock,
                                                     USR_PWD = CASE WHEN @p IS NULL THEN USR_PWD ELSE @p END
                                              WHERE USR_ID = @u", conn))
            {
                Fill(cmd, u);
                cmd.Parameters.AddWithValue("@p", string.IsNullOrEmpty(newPassword) ? (object)DBNull.Value : newPassword);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private static void Fill(SqlCommand cmd, UserAccount u)
        {
            object V(string s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : (object)s.Trim();
            cmd.Parameters.AddWithValue("@u", u.UserId.Trim());
            cmd.Parameters.AddWithValue("@n", V(u.Name));
            cmd.Parameters.AddWithValue("@pos", V(u.Position));
            cmd.Parameters.AddWithValue("@d", V(u.Department));
            cmd.Parameters.AddWithValue("@sec", V(u.Section));
            cmd.Parameters.AddWithValue("@div", V(u.Division));
            cmd.Parameters.AddWithValue("@l", u.Level);
            cmd.Parameters.AddWithValue("@lock", u.IsLocked);
        }

        // 📥 Import ผู้ใช้จาก Excel (แบบฟอร์มจริงรอผู้ใช้ส่งมา -> หาหัวคอลัมน์เองจาก 10 แถวแรก ไม่สนตัวพิมพ์/ช่องว่าง)
        //    USER ID + NAME บังคับ / PASSWORD (ผู้ใช้ใหม่บังคับ) / POSITION / หน่วยงาน / แผนก / ฝ่าย / LEVEL / LOCKED
        //    มี USER ID นี้แล้ว = อัพเดทข้อมูล (ช่องว่างไม่เปลี่ยน, PASSWORD ว่าง = ไม่เปลี่ยน) / ยังไม่มี = เพิ่มใหม่
        public class ImportRow
        {
            public int RowNumber { get; set; }
            public UserAccount User { get; set; } = new UserAccount();
            public string Password { get; set; }
            public bool IsNew { get; set; }
            public bool LevelGiven { get; set; }
            public bool LockGiven { get; set; }
            public string Error { get; set; }
            public bool IsValid => string.IsNullOrEmpty(Error);
        }

        private static readonly Dictionary<string, string[]> ImportHeads = new Dictionary<string, string[]>
        {
            ["ID"] = new[] { "USERID", "USRID", "USER", "EMPID", "EMPLOYEEID", "ID", "รหัสพนักงาน", "รหัสผู้ใช้" },
            ["NAME"] = new[] { "NAME", "FULLNAME", "USERNAME", "EMPLOYEENAME", "ชื่อ", "ชื่อสกุล", "ชื่อนามสกุล" },
            ["PWD"] = new[] { "PASSWORD", "PWD", "PASS", "รหัสผ่าน" },
            ["POS"] = new[] { "POSITION", "POS", "ตำแหน่ง" },
            ["DEPT"] = new[] { "DEPARTMENT", "DEPT", "UNIT", "หน่วยงาน" },
            ["SEC"] = new[] { "SECTION", "SEC", "แผนก" },
            ["DIV"] = new[] { "DIVISION", "DIV", "ฝ่าย" },
            ["LVL"] = new[] { "LEVEL", "LVL", "USERLEVEL", "ระดับ" },
            ["LOCK"] = new[] { "LOCKED", "LOCK", "STATUS", "สถานะ" }
        };

        private static string Norm(string h) =>
            new string((h ?? "").Where(c => !char.IsWhiteSpace(c) && c != '.' && c != '(' && c != ')' && c != '_' && c != '-' && c != '/').ToArray()).ToUpperInvariant();

        public List<ImportRow> ReadImport(string path)
        {
            var rows = new List<ImportRow>();
            var ms = new System.IO.MemoryStream();
            using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite)) fs.CopyTo(ms);
            ms.Position = 0;
            using (var wb = new ClosedXML.Excel.XLWorkbook(ms))
            {
                var ws = wb.Worksheet(1);
                var cols = new Dictionary<string, int>(); int header = 0;
                foreach (var row in ws.RowsUsed().Take(10))
                {
                    cols.Clear();
                    foreach (var cell in row.CellsUsed())
                    {
                        string h = Norm(cell.GetString());
                        foreach (var kv in ImportHeads)
                            if (!cols.ContainsKey(kv.Key) && kv.Value.Contains(h)) { cols[kv.Key] = cell.Address.ColumnNumber; break; }
                    }
                    if (cols.ContainsKey("ID") && cols.ContainsKey("NAME")) { header = row.RowNumber(); break; }
                }
                if (header == 0) throw new System.IO.InvalidDataException("ไม่พบหัวคอลัมน์ USER ID และ NAME ในไฟล์ Excel (อยู่ใน 10 แถวแรกของชีทแรก)");

                string Get(int r, string k) => cols.TryGetValue(k, out int c) ? ws.Cell(r, c).GetString().Trim() : "";
                int last = ws.LastRowUsed()?.RowNumber() ?? header;
                var existing = new HashSet<string>(GetUsers().Select(u => u.UserId), StringComparer.OrdinalIgnoreCase);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int r = header + 1; r <= last; r++)
                {
                    string id = Get(r, "ID"), name = Get(r, "NAME");
                    if (id.Length == 0 && name.Length == 0) continue;
                    var x = new ImportRow { RowNumber = r, Password = Get(r, "PWD") };
                    string Opt(string k) { string v = Get(r, k); return v.Length == 0 ? null : v; }
                    x.User = new UserAccount { UserId = id, Name = name, Position = Opt("POS"), Department = Opt("DEPT"), Section = Opt("SEC"), Division = Opt("DIV") };

                    string lv = Get(r, "LVL");
                    if (lv.Length > 0)
                    {
                        x.LevelGiven = true;
                        if (int.TryParse(lv.Split(' ')[0], out int l) && l >= 1 && l <= 3) x.User.Level = l;
                        else x.Error = $"LEVEL ต้องเป็น 1 / 2 / 3 ({lv})";
                    }
                    string lk = Get(r, "LOCK").ToUpperInvariant();
                    if (lk.Length > 0)
                    {
                        x.LockGiven = true;
                        if (lk == "1" || lk == "Y" || lk == "YES" || lk == "LOCK" || lk == "LOCKED" || lk == "TRUE") x.User.IsLocked = true;
                        else if (lk == "0" || lk == "N" || lk == "NO" || lk == "UNLOCK" || lk == "ACTIVE" || lk == "FALSE") x.User.IsLocked = false;
                        else x.Error ??= $"LOCKED ไม่ถูกต้อง ({lk}) - ใส่ 1 / 0";
                    }

                    if (x.Error != null) { }
                    else if (id.Length == 0) x.Error = "ไม่มี USER ID";
                    else if (id.Length > 20) x.Error = "USER ID ยาวเกิน 20 ตัวอักษร";
                    else if (name.Length == 0) x.Error = "ไม่มี NAME";
                    else if (!seen.Add(id)) x.Error = "USER ID ซ้ำกับแถวก่อนหน้าในไฟล์";
                    else
                    {
                        x.IsNew = !existing.Contains(id);
                        if (x.IsNew && string.IsNullOrEmpty(x.Password)) x.Error = "ผู้ใช้ใหม่ต้องมี PASSWORD";
                    }
                    rows.Add(x);
                }
            }
            return rows;
        }

        public (int Added, int Updated) ApplyImport(List<ImportRow> rows)
        {
            int add = 0, upd = 0;
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var tr = conn.BeginTransaction())
                {
                    foreach (var x in rows.Where(r => r.IsValid))
                    {
                        string sql = x.IsNew
                            ? @"INSERT INTO MST_USER (USR_ID, USR_PWD, USR_NAME, USR_POS, USR_DEPT, USR_SECTION, USR_DIVISION, USR_LVL, IS_ONLINE, IS_LOCKED, IS_MASTER_ADMIN)
                                VALUES (@u, @p, @n, @pos, @d, @sec, @div, @l, 0, @lock, 0)"
                            : @"UPDATE MST_USER SET USR_NAME = @n, USR_POS = ISNULL(@pos, USR_POS), USR_DEPT = ISNULL(@d, USR_DEPT),
                                       USR_SECTION = ISNULL(@sec, USR_SECTION), USR_DIVISION = ISNULL(@div, USR_DIVISION),
                                       USR_LVL = CASE WHEN @lg = 1 THEN @l ELSE USR_LVL END, IS_LOCKED = CASE WHEN @kg = 1 THEN @lock ELSE IS_LOCKED END,
                                       USR_PWD = CASE WHEN @p IS NULL THEN USR_PWD ELSE @p END
                                WHERE USR_ID = @u";
                        using (var cmd = new SqlCommand(sql, conn, tr))
                        {
                            Fill(cmd, x.User);
                            cmd.Parameters.AddWithValue("@p", string.IsNullOrEmpty(x.Password) ? (object)DBNull.Value : x.Password);
                            cmd.Parameters.AddWithValue("@lg", x.LevelGiven ? 1 : 0);
                            cmd.Parameters.AddWithValue("@kg", x.LockGiven ? 1 : 0);
                            cmd.ExecuteNonQuery();
                        }
                        if (x.IsNew) add++; else upd++;
                    }
                    tr.Commit();
                }
            }
            return (add, upd);
        }

        // ลบผู้ใช้: มีประวัติในระบบ (สแกน / PR / Log) -> ลบไม่ได้ คืนข้อความบอกเหตุผล (แนะนำให้ LOCK แทน)
        public string DeleteUser(string userId)
        {
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var cmd = new SqlCommand(@"SELECT (SELECT COUNT(*) FROM TRN_SCAN WHERE USR_ID = @u),
                                                         (SELECT COUNT(*) FROM TRN_PR_H WHERE USR_ID = @u),
                                                         (SELECT COUNT(*) FROM SYS_LOGS WHERE USR_ID = @u)", conn))
                {
                    cmd.Parameters.AddWithValue("@u", userId);
                    using (var r = cmd.ExecuteReader())
                    {
                        r.Read();
                        int scans = r.GetInt32(0), prs = r.GetInt32(1), logs = r.GetInt32(2);
                        if (scans + prs > 0)
                            return $"ผู้ใช้ {userId} มีประวัติในระบบ (สแกน {scans:N0} รายการ / PR {prs:N0} รายการ) จึงลบไม่ได้\nแนะนำให้ EDIT แล้วติ๊ก LOCKED เพื่อปิดการใช้งานแทน";
                        // มีแค่ Log การใช้งาน -> ลบได้ (Log ยังเก็บไว้ แต่ไม่ผูกกับผู้ใช้แล้ว)
                    }
                }
                using (var tr = conn.BeginTransaction())
                {
                    using (var cmd = new SqlCommand(@"UPDATE SYS_LOGS SET USR_ID = NULL WHERE USR_ID = @u;
                                                      DELETE FROM MST_PERM WHERE USR_ID = @u;
                                                      DELETE FROM MST_USER WHERE USR_ID = @u;", conn, tr))
                    {
                        cmd.Parameters.AddWithValue("@u", userId);
                        cmd.ExecuteNonQuery();
                    }
                    tr.Commit();
                }
                return null;
            }
        }

        // สิทธิ์ของผู้ใช้ทุกระบบ + ทุกคลัง (ไม่มีแถว = ไม่มีสิทธิ์) - เทียบ SYS_ID แบบไม่สนตัวพิมพ์ (ข้อมูลเก่าเช่น Dashboard / MaxMinCalc)
        public List<UserPermRow> GetPermissions(string userId)
        {
            var have = new Dictionary<string, (bool v, bool a, bool e, bool d, bool ap)>(StringComparer.OrdinalIgnoreCase);
            var stocks = new List<(string Code, string Name)>();
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var cmd = new SqlCommand("SELECT SYS_ID, PERM_VIEW, PERM_ADD, PERM_EDIT, PERM_DEL, PERM_APP FROM MST_PERM WHERE USR_ID = @u", conn))
                {
                    cmd.Parameters.AddWithValue("@u", userId);
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                        {
                            bool Y(int i) => string.Equals(r[i]?.ToString(), "Y", StringComparison.OrdinalIgnoreCase);
                            have[r.GetString(0)] = (Y(1), Y(2), Y(3), Y(4), Y(5));
                        }
                }
                using (var cmd = new SqlCommand("SELECT STK_CODE, STK_NAME FROM MST_STOCK ORDER BY IS_MAIN DESC, SORT_NO, STK_CODE", conn))
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) stocks.Add((r.GetString(0), r["STK_NAME"]?.ToString()));
            }

            UserPermRow Row(string id, string title, string group, string hint)
            {
                have.TryGetValue(id, out var p);
                return new UserPermRow { SysId = id, Title = title, Group = group, Hint = hint, View = p.v, Add = p.a, Edit = p.e, Delete = p.d, Approve = p.ap };
            }
            var list = Systems.Select(s => Row(s.SysId, s.Title, "SYSTEM", s.Hint)).ToList();
            list.AddRange(stocks.Select(s => Row(s.Code, $"{s.Code}  •  {s.Name}", "STOCK", "VIEW = see the stock (remark only)  •  ADD = import excel  •  EDIT = edit Max / Min / Qty / Stock box / pcs")));
            if (list.Count > 0) list[0].GroupTitle = "SYSTEMS";
            var firstStock = list.FirstOrDefault(r => r.Group == "STOCK");
            if (firstStock != null) firstStock.GroupTitle = "STOCKS";
            return list;
        }

        // บันทึกสิทธิ์ทั้งหมดของผู้ใช้ (แถวเดิมที่ SYS_ID ต่างกันแค่ตัวพิมพ์ ถูกแทนด้วยค่าที่โปรแกรมใช้)
        public void SavePermissions(string userId, IEnumerable<UserPermRow> rows)
        {
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var tr = conn.BeginTransaction())
                {
                    foreach (var p in rows)
                    {
                        using (var cmd = new SqlCommand(@"
                            DELETE FROM MST_PERM WHERE USR_ID = @u AND SYS_ID = @s COLLATE Latin1_General_CI_AS AND SYS_ID <> @s COLLATE Latin1_General_CS_AS;
                            IF EXISTS (SELECT 1 FROM MST_PERM WHERE USR_ID = @u AND SYS_ID = @s)
                                UPDATE MST_PERM SET PERM_VIEW = @v, PERM_ADD = @a, PERM_EDIT = @e, PERM_DEL = @d, PERM_APP = @ap WHERE USR_ID = @u AND SYS_ID = @s;
                            ELSE
                                INSERT INTO MST_PERM (USR_ID, SYS_ID, PERM_VIEW, PERM_ADD, PERM_EDIT, PERM_DEL, PERM_APP) VALUES (@u, @s, @v, @a, @e, @d, @ap);", conn, tr))
                        {
                            string YN(bool b) => b ? "Y" : "N";
                            cmd.Parameters.AddWithValue("@u", userId);
                            cmd.Parameters.AddWithValue("@s", p.SysId);
                            cmd.Parameters.AddWithValue("@v", YN(p.View));
                            cmd.Parameters.AddWithValue("@a", YN(p.Add));
                            cmd.Parameters.AddWithValue("@e", YN(p.Edit));
                            cmd.Parameters.AddWithValue("@d", YN(p.Delete));
                            cmd.Parameters.AddWithValue("@ap", YN(p.Approve));
                            cmd.ExecuteNonQuery();
                        }
                    }
                    tr.Commit();
                }
            }
        }
    }
}
