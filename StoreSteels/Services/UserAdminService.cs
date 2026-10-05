using Microsoft.Data.SqlClient;
using CIMS.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CIMS.Services
{
    // 👥 ผู้ใช้งาน 1 คน (CIMS.Users) สำหรับหน้า USER MANAGEMENT
    public class UserAccount
    {
        public string UserId { get; set; }
        public string Name { get; set; }
        public string Position { get; set; }       // ตำแหน่ง (Position)
        public string Department { get; set; }     // หน่วยงาน (Department)
        public string Section { get; set; }        // แผนก (Section)
        public string Division { get; set; }       // ฝ่าย (Division)
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

    // สิทธิ์ 1 ระบบของผู้ใช้ (CIMS.Permissions 1 แถว)
    public class UserPermRow
    {
        public string SysId { get; set; }          // ค่าที่โปรแกรมตรวจสิทธิ์ (เขียนลง CIMS.Permissions.SystemID)
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

        // ทุกระบบที่โปรแกรมตรวจสิทธิ์ (SystemID ต้องตรงกับที่ UserSession ใช้) - คลังอื่นเพิ่มให้อัตโนมัติจาก CIMS.Stocks
        public static readonly (string SysId, string Title, string Hint)[] Systems =
        {
            ("DASHBOARD",   "Dashboard",                  "VIEW = open the page"),
            ("StoreMaxMin", "Store(Max-Min)",            "VIEW = open + stocks (read only)  •  ADD = add stock / barcode format  •  EDIT = stock settings / barcode formats  •  DEL = delete stock"),
            ("MultiScanner", "Multi-Scanner",              "VIEW = open / history / test mode  •  ADD = scan in + return  •  EDIT = scan out"),
            ("ProductControl", "ProductControl",     "VIEW = open + export QR  •  ADD = register / import  •  EDIT = edit / show-hide / image  •  DEL = delete"),
            ("PackingCard", "Pick List",                  "VIEW = open the page"),
            ("MAXMINCALC",  "Max-Min Calculator",         "VIEW = open  •  EDIT = edit / import / calculate"),
            ("ForecastOrder", "Forecast / Order / Delivery", "VIEW = open + export  •  ADD = import forecast / order / delivery"),
            ("PR",          "PR Management",              "VIEW = open + approved list  •  ADD = submit  •  EDIT = edit / PR settings  •  DEL = delete  •  APPROVE = approve / reject / export"),
            ("ActivityLog", "Activity Log",               "VIEW = open the page"),
            ("UserManagement", "UserManagement",            "VIEW = open  •  ADD / EDIT / DEL users  •  APPROVE = set permissions")
        };

        public List<UserAccount> GetUsers()
        {
            var list = new List<UserAccount>();
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand(@"
                SELECT u.UserID, u.FullName, u.Position, u.Department, u.Section, u.Division, ISNULL(u.UserLevel, 3) AS LVL, ISNULL(u.IsOnline, 0) AS ONL,
                       ISNULL(u.IsLocked, 0) AS LCK, ISNULL(u.IsMasterAdmin, 0) AS MA, u.LastLogin,
                       (SELECT COUNT(*) FROM CIMS.Permissions p WHERE p.UserID = u.UserID AND p.CanView = 'Y') AS PERMS
                FROM CIMS.Users u ORDER BY ISNULL(u.UserLevel, 3), u.UserID", conn))
            {
                conn.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new UserAccount
                        {
                            UserId = r["UserID"].ToString(),
                            Name = r["FullName"]?.ToString(),
                            Position = r["Position"]?.ToString(),
                            Department = r["Department"]?.ToString(),
                            Section = r["Section"]?.ToString(),
                            Division = r["Division"]?.ToString(),
                            Level = Convert.ToInt32(r["LVL"]),
                            IsOnline = Convert.ToBoolean(r["ONL"]),
                            IsLocked = Convert.ToBoolean(r["LCK"]),
                            IsMasterAdmin = Convert.ToBoolean(r["MA"]),
                            LastLogin = r["LastLogin"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(r["LastLogin"]),
                            PermCount = Convert.ToInt32(r["PERMS"])
                        });
            }
            return list;
        }

        public bool Exists(string userId)
        {
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM CIMS.Users WHERE UserID = @u", conn))
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
            using (var cmd = new SqlCommand(@"INSERT INTO CIMS.Users (UserID, Password, FullName, Position, Department, Section, Division, UserLevel, IsOnline, IsLocked, IsMasterAdmin)
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
            using (var cmd = new SqlCommand(@"UPDATE CIMS.Users SET FullName = @n, Position = @pos, Department = @d, Section = @sec, Division = @div, UserLevel = @l, IsLocked = @lock,
                                                     Password = CASE WHEN @p IS NULL THEN Password ELSE @p END
                                              WHERE UserID = @u", conn))
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

        // 📥 Import ผู้ใช้ + สิทธิ์จาก Excel (อ่านทุกชีทในไฟล์ แยกชนิดจากหัวคอลัมน์)
        //   ชีทผู้ใช้  : NO / USERID / PASSWORD / FULLNAME / DIVISION / DEPARTMENT / SECTION / POSITION / USERLEVEL / ISONLINE / ISLOCKED / ISMASTERADMIN
        //               มี USERID แล้ว = อัพเดท (ช่องว่างไม่เปลี่ยน, PASSWORD ว่าง = ไม่เปลี่ยน) / ยังไม่มี = เพิ่มใหม่ (ต้องมี PASSWORD)
        //               ISONLINE ระบบจัดการเอง (ไม่นำเข้า) / ISMASTERADMIN = 1 นำเข้าได้เฉพาะผู้ Import ที่เป็น Level 1 หรือ Master Admin
        //   ชีทสิทธิ์  : NO / USERID / SYSTEMID / CANVIEW / CANADD / CANEDIT / CANDELETE / CANAPPROVE
        //               SYSTEMID = รหัสระบบ (Dashboard, StoreMaxMin, MultiScanner ...) หรือรหัสคลัง / ค่า Y N, 1 0, TRUE FALSE (ว่าง = N)
        //               แถวที่มีในไฟล์ = ตั้งสิทธิ์ตามไฟล์ / ระบบที่ไม่มีในไฟล์ คงสิทธิ์เดิม
        public class ImportRow
        {
            public int RowNumber { get; set; }
            public string Sheet { get; set; }
            public UserAccount User { get; set; } = new UserAccount();
            public string Password { get; set; }
            public bool IsNew { get; set; }
            public bool LevelGiven { get; set; }
            public bool LockGiven { get; set; }
            public bool MasterGiven { get; set; }
            public string Error { get; set; }
            public bool IsValid => string.IsNullOrEmpty(Error);
        }

        public class PermImportRow
        {
            public int RowNumber { get; set; }
            public string Sheet { get; set; }
            public string UserId { get; set; }
            public string SysId { get; set; }
            public bool View { get; set; }
            public bool Add { get; set; }
            public bool Edit { get; set; }
            public bool Delete { get; set; }
            public bool Approve { get; set; }
            public string Error { get; set; }
            public bool IsValid => string.IsNullOrEmpty(Error);
        }

        public class ImportFile
        {
            public List<ImportRow> Users { get; } = new List<ImportRow>();
            public List<PermImportRow> Perms { get; } = new List<PermImportRow>();
        }

        private static readonly Dictionary<string, string[]> ImportHeads = new Dictionary<string, string[]>
        {
            ["ID"] = new[] { "USERID", "USRID", "USER", "EMPID", "EMPLOYEEID", "ID", "รหัสพนักงาน", "รหัสผู้ใช้" },
            ["NAME"] = new[] { "FULLNAME", "NAME", "USERNAME", "EMPLOYEENAME", "ชื่อ", "ชื่อสกุล", "ชื่อนามสกุล" },
            ["PWD"] = new[] { "PASSWORD", "PWD", "PASS", "รหัสผ่าน" },
            ["POS"] = new[] { "POSITION", "POS", "ตำแหน่ง" },
            ["DEPT"] = new[] { "DEPARTMENT", "DEPT", "UNIT", "หน่วยงาน" },
            ["SEC"] = new[] { "SECTION", "SEC", "แผนก" },
            ["DIV"] = new[] { "DIVISION", "DIV", "ฝ่าย" },
            ["LVL"] = new[] { "USERLEVEL", "LEVEL", "LVL", "ระดับ" },
            ["LOCK"] = new[] { "ISLOCKED", "LOCKED", "LOCK", "STATUS", "สถานะ" },
            ["MASTER"] = new[] { "ISMASTERADMIN", "MASTERADMIN", "MASTER" }
        };

        private static readonly Dictionary<string, string[]> PermHeads = new Dictionary<string, string[]>
        {
            ["ID"] = new[] { "USERID", "USRID", "USER", "EMPID", "ID" },
            ["SYS"] = new[] { "SYSTEMID", "SYSID", "SYSTEM", "ระบบ" },
            ["V"] = new[] { "CANVIEW", "VIEW" },
            ["A"] = new[] { "CANADD", "ADD" },
            ["E"] = new[] { "CANEDIT", "EDIT" },
            ["D"] = new[] { "CANDELETE", "DELETE", "DEL" },
            ["P"] = new[] { "CANAPPROVE", "APPROVE", "APP" }
        };

        private static string Norm(string h) =>
            new string((h ?? "").Where(c => !char.IsWhiteSpace(c) && c != '.' && c != '(' && c != ')' && c != '_' && c != '-' && c != '/').ToArray()).ToUpperInvariant();

        // แถวหัวตาราง (10 แถวแรก) ที่มีคอลัมน์บังคับครบ -> (แถว, คอลัมน์)
        private static (int Row, Dictionary<string, int> Cols) FindHeader(ClosedXML.Excel.IXLWorksheet ws, Dictionary<string, string[]> heads, params string[] required)
        {
            foreach (var row in ws.RowsUsed().Take(10))
            {
                var cols = new Dictionary<string, int>();
                foreach (var cell in row.CellsUsed())
                {
                    string h = Norm(cell.GetString());
                    foreach (var kv in heads)
                        if (!cols.ContainsKey(kv.Key) && kv.Value.Contains(h)) { cols[kv.Key] = cell.Address.ColumnNumber; break; }
                }
                if (required.All(cols.ContainsKey)) return (row.RowNumber(), cols);
            }
            return (0, null);
        }

        // Y / N, 1 / 0, TRUE / FALSE, ✓ (ว่าง = false) - ค่าอื่น = null (ผิดรูปแบบ)
        private static bool? Flag(string v)
        {
            v = (v ?? "").Trim().ToUpperInvariant();
            if (v.Length == 0 || v == "N" || v == "NO" || v == "0" || v == "FALSE" || v == "-" || v == "✗") return false;
            if (v == "Y" || v == "YES" || v == "1" || v == "TRUE" || v == "✓" || v == "/" || v == "X") return true;
            return null;
        }

        // รหัสระบบที่ใช้ได้ + รหัสคลังทั้งหมด (ตัวพิมพ์ตามที่โปรแกรมใช้)
        private List<string> ValidSystemIds()
        {
            var ids = Systems.Select(s => s.SysId).ToList();
            using (var conn = new SqlConnection(_cs))
            using (var cmd = new SqlCommand("SELECT StockCode FROM CIMS.Stocks", conn))
            {
                conn.Open();
                using (var r = cmd.ExecuteReader()) while (r.Read()) ids.Add(r.GetString(0));
            }
            return ids;
        }

        public ImportFile ReadImport(string path, bool allowMasterAdmin)
        {
            var result = new ImportFile();
            var ms = new System.IO.MemoryStream();
            using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite)) fs.CopyTo(ms);
            ms.Position = 0;
            var existing = new HashSet<string>(GetUsers().Select(u => u.UserId), StringComparer.OrdinalIgnoreCase);
            var seenUser = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenPerm = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var systemIds = ValidSystemIds();

            using (var wb = new ClosedXML.Excel.XLWorkbook(ms))
            {
                foreach (var ws in wb.Worksheets)
                {
                    var perm = FindHeader(ws, PermHeads, "ID", "SYS");
                    if (perm.Row > 0) { ReadPermSheet(ws, perm.Row, perm.Cols, result, systemIds, seenPerm); continue; }
                    var user = FindHeader(ws, ImportHeads, "ID", "NAME");
                    if (user.Row > 0) ReadUserSheet(ws, user.Row, user.Cols, result, existing, seenUser, allowMasterAdmin);
                }
            }
            if (result.Users.Count == 0 && result.Perms.Count == 0)
                throw new System.IO.InvalidDataException("ไม่พบหัวคอลัมน์ในไฟล์ Excel\n\nชีทผู้ใช้: USERID + FULLNAME\nชีทสิทธิ์: USERID + SYSTEMID\n(หัวคอลัมน์อยู่ใน 10 แถวแรกของชีท)");

            // ผู้ใช้ในชีทสิทธิ์ต้องมีอยู่แล้ว หรือเพิ่มใหม่จากชีทผู้ใช้ในไฟล์เดียวกัน
            var newIds = new HashSet<string>(result.Users.Where(u => u.IsValid && u.IsNew).Select(u => u.User.UserId), StringComparer.OrdinalIgnoreCase);
            foreach (var p in result.Perms.Where(p => p.IsValid))
                if (!existing.Contains(p.UserId) && !newIds.Contains(p.UserId)) p.Error = $"ไม่พบ USERID {p.UserId} ในระบบ (และไม่ได้เพิ่มในชีทผู้ใช้)";
            return result;
        }

        private void ReadUserSheet(ClosedXML.Excel.IXLWorksheet ws, int header, Dictionary<string, int> cols, ImportFile result,
                                   HashSet<string> existing, HashSet<string> seen, bool allowMasterAdmin)
        {
            string Get(int r, string k) => cols.TryGetValue(k, out int c) ? ws.Cell(r, c).GetString().Trim() : "";
            int last = ws.LastRowUsed()?.RowNumber() ?? header;
            for (int r = header + 1; r <= last; r++)
            {
                string id = Get(r, "ID"), name = Get(r, "NAME");
                if (id.Length == 0 && name.Length == 0) continue;
                var x = new ImportRow { RowNumber = r, Sheet = ws.Name, Password = Get(r, "PWD") };
                string Opt(string k) { string v = Get(r, k); return v.Length == 0 ? null : v; }
                x.User = new UserAccount { UserId = id, Name = name, Position = Opt("POS"), Department = Opt("DEPT"), Section = Opt("SEC"), Division = Opt("DIV") };

                string lv = Get(r, "LVL");
                if (lv.Length > 0)
                {
                    x.LevelGiven = true;
                    if (int.TryParse(lv.Split(' ')[0], out int l) && l >= 1 && l <= 3) x.User.Level = l;
                    else x.Error = $"USERLEVEL ต้องเป็น 1 / 2 / 3 ({lv})";
                }
                string lk = Get(r, "LOCK");
                if (lk.Length > 0)
                {
                    x.LockGiven = true;
                    string u = lk.ToUpperInvariant();
                    bool? f = u == "LOCK" || u == "LOCKED" ? true : u == "UNLOCK" || u == "ACTIVE" ? false : Flag(lk);
                    if (f.HasValue) x.User.IsLocked = f.Value; else x.Error ??= $"ISLOCKED ไม่ถูกต้อง ({lk}) - ใส่ 1 / 0";
                }
                string ma = Get(r, "MASTER");
                if (ma.Length > 0)
                {
                    bool? f = Flag(ma);
                    if (!f.HasValue) x.Error ??= $"ISMASTERADMIN ไม่ถูกต้อง ({ma}) - ใส่ 1 / 0";
                    else if (f.Value && !allowMasterAdmin) x.Error ??= "ISMASTERADMIN = 1 นำเข้าได้เฉพาะผู้ดูแลระบบ Level 1";
                    else { x.MasterGiven = allowMasterAdmin; x.User.IsMasterAdmin = f.Value; }
                }

                if (x.Error != null) { }
                else if (id.Length == 0) x.Error = "ไม่มี USERID";
                else if (id.Length > 20) x.Error = "USERID ยาวเกิน 20 ตัวอักษร";
                else if (name.Length == 0) x.Error = "ไม่มี FULLNAME";
                else if (!seen.Add(id)) x.Error = "USERID ซ้ำกับแถวก่อนหน้าในไฟล์";
                else
                {
                    x.IsNew = !existing.Contains(id);
                    if (x.IsNew && string.IsNullOrEmpty(x.Password)) x.Error = "ผู้ใช้ใหม่ต้องมี PASSWORD";
                }
                result.Users.Add(x);
            }
        }

        private static void ReadPermSheet(ClosedXML.Excel.IXLWorksheet ws, int header, Dictionary<string, int> cols, ImportFile result,
                                          List<string> systemIds, HashSet<string> seen)
        {
            string Get(int r, string k) => cols.TryGetValue(k, out int c) ? ws.Cell(r, c).GetString().Trim() : "";
            int last = ws.LastRowUsed()?.RowNumber() ?? header;
            for (int r = header + 1; r <= last; r++)
            {
                string id = Get(r, "ID"), sys = Get(r, "SYS");
                if (id.Length == 0 && sys.Length == 0) continue;
                var p = new PermImportRow { RowNumber = r, Sheet = ws.Name, UserId = id };
                bool? v = Flag(Get(r, "V")), a = Flag(Get(r, "A")), e = Flag(Get(r, "E")), d = Flag(Get(r, "D")), ap = Flag(Get(r, "P"));
                string canon = systemIds.FirstOrDefault(s => string.Equals(s, sys, StringComparison.OrdinalIgnoreCase));

                if (id.Length == 0) p.Error = "ไม่มี USERID";
                else if (sys.Length == 0) p.Error = "ไม่มี SYSTEMID";
                else if (canon == null) p.Error = $"ไม่รู้จัก SYSTEMID \"{sys}\"";
                else if (v == null || a == null || e == null || d == null || ap == null) p.Error = "ค่าสิทธิ์ต้องเป็น Y / N หรือ 1 / 0";
                else if (!seen.Add(id + "|" + canon)) p.Error = "USERID + SYSTEMID ซ้ำกับแถวก่อนหน้าในไฟล์";
                p.SysId = canon ?? sys;
                p.View = v == true; p.Add = a == true; p.Edit = e == true; p.Delete = d == true; p.Approve = ap == true;
                result.Perms.Add(p);
            }
        }

        // นำเข้าทั้งไฟล์ใน Transaction เดียว: ผู้ใช้ก่อน (Trigger ให้สิทธิ์เริ่มต้นกับผู้ใช้ใหม่) แล้วค่อยตั้งสิทธิ์ตามชีทสิทธิ์
        public (int Added, int Updated, int Perms) ApplyImport(ImportFile file)
        {
            int add = 0, upd = 0, perms = 0;
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var tr = conn.BeginTransaction())
                {
                    foreach (var x in file.Users.Where(r => r.IsValid))
                    {
                        string sql = x.IsNew
                            ? @"INSERT INTO CIMS.Users (UserID, Password, FullName, Position, Department, Section, Division, UserLevel, IsOnline, IsLocked, IsMasterAdmin)
                                VALUES (@u, @p, @n, @pos, @d, @sec, @div, @l, 0, @lock, @ma)"
                            : @"UPDATE CIMS.Users SET FullName = @n, Position = ISNULL(@pos, Position), Department = ISNULL(@d, Department),
                                       Section = ISNULL(@sec, Section), Division = ISNULL(@div, Division),
                                       UserLevel = CASE WHEN @lg = 1 THEN @l ELSE UserLevel END, IsLocked = CASE WHEN @kg = 1 THEN @lock ELSE IsLocked END,
                                       IsMasterAdmin = CASE WHEN @mg = 1 THEN @ma ELSE IsMasterAdmin END,
                                       Password = CASE WHEN @p IS NULL THEN Password ELSE @p END
                                WHERE UserID = @u";
                        using (var cmd = new SqlCommand(sql, conn, tr))
                        {
                            Fill(cmd, x.User);
                            cmd.Parameters.AddWithValue("@p", string.IsNullOrEmpty(x.Password) ? (object)DBNull.Value : x.Password);
                            cmd.Parameters.AddWithValue("@lg", x.LevelGiven ? 1 : 0);
                            cmd.Parameters.AddWithValue("@kg", x.LockGiven ? 1 : 0);
                            cmd.Parameters.AddWithValue("@mg", x.MasterGiven ? 1 : 0);
                            cmd.Parameters.AddWithValue("@ma", x.MasterGiven && x.User.IsMasterAdmin ? 1 : 0);
                            cmd.ExecuteNonQuery();
                        }
                        if (x.IsNew) add++; else upd++;
                    }

                    foreach (var p in file.Perms.Where(r => r.IsValid))
                    {
                        using (var cmd = new SqlCommand(@"
                            DELETE FROM CIMS.Permissions WHERE UserID = @u AND SystemID = @s COLLATE Latin1_General_CI_AS AND SystemID <> @s COLLATE Latin1_General_CS_AS;
                            IF EXISTS (SELECT 1 FROM CIMS.Permissions WHERE UserID = @u AND SystemID = @s)
                                UPDATE CIMS.Permissions SET CanView = @v, CanAdd = @a, CanEdit = @e, CanDelete = @d, CanApprove = @ap WHERE UserID = @u AND SystemID = @s;
                            ELSE
                                INSERT INTO CIMS.Permissions (UserID, SystemID, CanView, CanAdd, CanEdit, CanDelete, CanApprove) VALUES (@u, @s, @v, @a, @e, @d, @ap);", conn, tr))
                        {
                            string YN(bool b) => b ? "Y" : "N";
                            cmd.Parameters.AddWithValue("@u", p.UserId);
                            cmd.Parameters.AddWithValue("@s", p.SysId);
                            cmd.Parameters.AddWithValue("@v", YN(p.View));
                            cmd.Parameters.AddWithValue("@a", YN(p.Add));
                            cmd.Parameters.AddWithValue("@e", YN(p.Edit));
                            cmd.Parameters.AddWithValue("@d", YN(p.Delete));
                            cmd.Parameters.AddWithValue("@ap", YN(p.Approve));
                            cmd.ExecuteNonQuery();
                        }
                        perms++;
                    }
                    tr.Commit();
                }
            }
            return (add, upd, perms);
        }

        // 📄 แบบฟอร์ม Import (รูปแบบเดียวกับ Template อื่นทั้งระบบ): ชีท USERS + ชีท PERMISSIONS (ว่างพร้อมกรอก)
        //    + ชีท HOW TO (วิธีกรอก + ตัวอย่าง) + ชีท SYSTEM IDS (รหัสระบบ / รหัสคลังที่ใช้ได้)
        public void CreateImportTemplate(string path)
        {
            using (var wb = new ClosedXML.Excel.XLWorkbook())
            {
                var u = wb.AddWorksheet("USERS");
                ImportTemplateService.Header(u, new[] { "NO", "USERID", "PASSWORD", "FULLNAME", "DIVISION", "DEPARTMENT", "SECTION", "POSITION", "USERLEVEL", "ISONLINE", "ISLOCKED", "ISMASTERADMIN" },
                                             new double[] { 6.4, 14, 14, 28, 16, 16, 16, 16, 12, 11, 11, 19 });
                ImportTemplateService.Body(u, 31, 12);
                for (int r = 2; r <= 31; r++) u.Cell(r, 1).Value = r - 1;

                var p = wb.AddWorksheet("PERMISSIONS");
                ImportTemplateService.Header(p, new[] { "NO", "USERID", "SYSTEMID", "CANVIEW", "CANADD", "CANEDIT", "CANDELETE", "CANAPPROVE" },
                                             new double[] { 6.4, 14, 20, 11, 11, 11, 12, 13 });
                ImportTemplateService.Body(p, 51, 8);
                for (int r = 2; r <= 51; r++) p.Cell(r, 1).Value = r - 1;

                ImportTemplateService.Guide(wb, "CIMS - Import User / Permission", new[]
                {
                    ("ชีท USERS", "1 แถว = 1 ผู้ใช้  •  ตัวอย่าง: USERID EMP001 / PASSWORD 1234 / FULLNAME Example Name / USERLEVEL 2 / ISLOCKED 0"),
                    ("USERID * / FULLNAME *", "บังคับ - USERID ที่มีอยู่แล้ว = อัพเดทข้อมูล / ใหม่ = เพิ่มผู้ใช้ (ต้องมี PASSWORD)"),
                    ("PASSWORD", "รหัสผ่าน - ผู้ใช้เดิมเว้นว่าง = ใช้รหัสเดิม"),
                    ("DIVISION - POSITION", "สังกัด / ตำแหน่ง (ไม่บังคับ)"),
                    ("USERLEVEL", "1 = Admin (ทำได้ทุกอย่าง) / 2 / 3 = ได้เฉพาะสิทธิ์ที่ติ๊ก"),
                    ("ISLOCKED", "1 = ล็อก (เข้าระบบไม่ได้) / 0 = ใช้งานได้  •  ISONLINE ระบบจัดการเอง"),
                    ("ISMASTERADMIN", "1 = Master Admin (นำเข้าได้เฉพาะ Admin)"),
                    ("ชีท PERMISSIONS", "1 แถว = 1 ผู้ใช้ + 1 ระบบ  •  ตัวอย่าง: EMP001 / MultiScanner / Y / Y / N / N / N"),
                    ("SYSTEMID", "รหัสระบบหรือรหัสคลัง ดูในชีท SYSTEM IDS - ระบบที่ไม่มีในไฟล์คงสิทธิ์เดิม"),
                    ("CANVIEW - CANAPPROVE", "Y / N หรือ 1 / 0"),
                    ("NO", "ลำดับ - ระบบไม่ได้ใช้"),
                    ("", "กรอกในชีท USERS และ / หรือ PERMISSIONS แล้วกด IMPORT USER / PERMISSION ในหน้า User Management"),
                });

                var s = wb.AddWorksheet("SYSTEM IDS");
                ImportTemplateService.Header(s, new[] { "SYSTEMID", "SYSTEM", "VIEW / ADD / EDIT / DEL / APPROVE" }, new double[] { 20, 30, 90 });
                int row = 2;
                foreach (var sys in Systems) { s.Cell(row, 1).Value = sys.SysId; s.Cell(row, 2).Value = sys.Title; s.Cell(row, 3).Value = sys.Hint; row++; }
                foreach (var code in ValidSystemIds().Skip(Systems.Length)) { s.Cell(row, 1).Value = code; s.Cell(row, 2).Value = "Stock"; s.Cell(row, 3).Value = "VIEW = see the stock  •  ADD = import excel  •  EDIT = edit Max / Min / Qty"; row++; }
                ImportTemplateService.Body(s, row - 1, 3);
                s.Range(2, 3, Math.Max(2, row - 1), 3).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Left;
                wb.Worksheet(1).SetTabActive();
                wb.SaveAs(path);
            }
        }

        // ลบผู้ใช้: มีประวัติในระบบ (สแกน / PR / Log) -> ลบไม่ได้ คืนข้อความบอกเหตุผล (แนะนำให้ LOCK แทน)
        public string DeleteUser(string userId)
        {
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var cmd = new SqlCommand(@"SELECT (SELECT COUNT(*) FROM CIMS.ScanTransactions WHERE UserID = @u),
                                                         (SELECT COUNT(*) FROM CIMS.PRHeaders WHERE UserID = @u),
                                                         (SELECT COUNT(*) FROM CIMS.SystemLogs WHERE UserID = @u)", conn))
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
                    using (var cmd = new SqlCommand(@"UPDATE CIMS.SystemLogs SET UserID = NULL WHERE UserID = @u;
                                                      DELETE FROM CIMS.Permissions WHERE UserID = @u;
                                                      DELETE FROM CIMS.Users WHERE UserID = @u;", conn, tr))
                    {
                        cmd.Parameters.AddWithValue("@u", userId);
                        cmd.ExecuteNonQuery();
                    }
                    tr.Commit();
                }
                return null;
            }
        }

        // สิทธิ์ของผู้ใช้ทุกระบบ + ทุกคลัง (ไม่มีแถว = ไม่มีสิทธิ์) - เทียบ SystemID แบบไม่สนตัวพิมพ์ (ข้อมูลเก่าเช่น Dashboard / MaxMinCalc)
        public List<UserPermRow> GetPermissions(string userId)
        {
            var have = new Dictionary<string, (bool v, bool a, bool e, bool d, bool ap)>(StringComparer.OrdinalIgnoreCase);
            var stocks = new List<(string Code, string Name)>();
            using (var conn = new SqlConnection(_cs))
            {
                conn.Open();
                using (var cmd = new SqlCommand("SELECT SystemID, CanView, CanAdd, CanEdit, CanDelete, CanApprove FROM CIMS.Permissions WHERE UserID = @u", conn))
                {
                    cmd.Parameters.AddWithValue("@u", userId);
                    using (var r = cmd.ExecuteReader())
                        while (r.Read())
                        {
                            bool Y(int i) => string.Equals(r[i]?.ToString(), "Y", StringComparison.OrdinalIgnoreCase);
                            have[r.GetString(0)] = (Y(1), Y(2), Y(3), Y(4), Y(5));
                        }
                }
                using (var cmd = new SqlCommand("SELECT StockCode, StockName FROM CIMS.Stocks ORDER BY IsMain DESC, SortNo, StockCode", conn))
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) stocks.Add((r.GetString(0), r["StockName"]?.ToString()));
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

        // บันทึกสิทธิ์ทั้งหมดของผู้ใช้ (แถวเดิมที่ SystemID ต่างกันแค่ตัวพิมพ์ ถูกแทนด้วยค่าที่โปรแกรมใช้)
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
                            DELETE FROM CIMS.Permissions WHERE UserID = @u AND SystemID = @s COLLATE Latin1_General_CI_AS AND SystemID <> @s COLLATE Latin1_General_CS_AS;
                            IF EXISTS (SELECT 1 FROM CIMS.Permissions WHERE UserID = @u AND SystemID = @s)
                                UPDATE CIMS.Permissions SET CanView = @v, CanAdd = @a, CanEdit = @e, CanDelete = @d, CanApprove = @ap WHERE UserID = @u AND SystemID = @s;
                            ELSE
                                INSERT INTO CIMS.Permissions (UserID, SystemID, CanView, CanAdd, CanEdit, CanDelete, CanApprove) VALUES (@u, @s, @v, @a, @e, @d, @ap);", conn, tr))
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
