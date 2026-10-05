using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CIMS.Views
{
    // 👥 USER MANAGEMENT - การ์ดผู้ใช้ตาม UserID: ดู / เพิ่ม / แก้ไข / ลบ / กำหนดสิทธิ์ / Import
    public partial class UserManagementView : Page
    {
        private readonly UserAdminService _service = new UserAdminService();
        private readonly UserSession _session;
        private List<UserAccount> _all = new List<UserAccount>();
        private DispatcherTimer _searchTimer;

        public UserManagementView(UserSession session)
        {
            InitializeComponent();
            _session = session;
            btnAdd.Visibility = session?.CanAddUser == true ? Visibility.Visible : Visibility.Collapsed;
            btnImport.Visibility = session?.CanAddUser == true && session.CanEditUser ? Visibility.Visible : Visibility.Collapsed;
            btnTemplate.Visibility = btnImport.Visibility;
            Loaded += async (s, e) => await ReloadAsync();
        }

        private async Task ReloadAsync()
        {
            try
            {
                _all = await Task.Run(() => _service.GetUsers());
                ApplyFilter();
            }
            catch (Exception ex) { DialogHelper.ShowError("โหลดรายชื่อผู้ใช้งานไม่สำเร็จ\n" + ex.Message); }
        }

        private void ApplyFilter()
        {
            string k = (txtSearch.Text ?? "").Trim();
            var list = k.Length == 0 ? _all : _all.Where(u =>
                    new[] { u.UserId, u.Name, u.Position, u.Department, u.Section, u.Division, u.LevelText, u.StatusText }
                        .Any(v => !string.IsNullOrEmpty(v) && v.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                // ตรงที่สุดขึ้นก่อน: USER ID ตรงเป๊ะ > ขึ้นต้นด้วยคำค้น > มีคำค้นอยู่ข้างใน
                .OrderBy(u => string.Equals(u.UserId, k, StringComparison.OrdinalIgnoreCase) ? 0
                            : (u.UserId ?? "").StartsWith(k, StringComparison.OrdinalIgnoreCase) || (u.Name ?? "").StartsWith(k, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .ToList();
            icUsers.ItemsSource = list;
            txtEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            txtTotal.Text = $"{_all.Count:N0} USERS";
            txtOnline.Text = $"{_all.Count(u => u.IsOnline && !u.IsLocked):N0} ONLINE";
            txtLocked.Text = $"{_all.Count(u => u.IsLocked):N0} LOCKED";
        }

        private void Search_Changed(object sender, TextChangedEventArgs e)
        {
            _searchTimer?.Stop();
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _searchTimer.Tick += (s, ev) => { _searchTimer.Stop(); ApplyFilter(); };
            _searchTimer.Start();
        }

        private static UserAccount Of(object sender) => (sender as FrameworkElement)?.Tag as UserAccount;

        private void EditBtn_Loaded(object sender, RoutedEventArgs e) => ((Button)sender).Visibility = _session?.CanEditUser == true ? Visibility.Visible : Visibility.Collapsed;
        private void PermBtn_Loaded(object sender, RoutedEventArgs e) => ((Button)sender).Visibility = _session?.CanSetPermissions == true ? Visibility.Visible : Visibility.Collapsed;
        private void DelBtn_Loaded(object sender, RoutedEventArgs e) => ((Button)sender).Visibility = _session?.CanDeleteUser == true ? Visibility.Visible : Visibility.Collapsed;

        private void View_Click(object sender, RoutedEventArgs e)
        {
            var u = Of(sender); if (u == null) return;
            List<UserPermRow> perms;
            try { perms = _service.GetPermissions(u.UserId); }
            catch (Exception ex) { DialogHelper.ShowError("โหลดสิทธิ์ไม่สำเร็จ\n" + ex.Message); return; }
            new UserPermissionWindow(u, perms, readOnly: true).ShowDialog();
        }

        private async void Add_Click(object sender, RoutedEventArgs e)
        {
            var w = new UserEditWindow(null, _service);
            if (w.ShowDialog() != true) return;
            try
            {
                var u = w.Result; string pwd = w.Password;
                await Task.Run(() => _service.AddUser(u, pwd));
                LogService.WriteLog(_session?.UserId, "USER_ADD", $"Added user {u.UserId} | {u.Name} | Level {u.Level} | {u.Position} | {u.Department} | {u.Section} | {u.Division}", u.UserId);
                NotificationManager.Show("User added", $"เพิ่มผู้ใช้ {u.UserId} แล้ว\nกด 🔑 PERMISSION เพื่อกำหนดสิทธิ์", true);
                await ReloadAsync();
            }
            catch (Exception ex) { DialogHelper.ShowError("เพิ่มผู้ใช้ไม่สำเร็จ\n" + ex.Message); }
        }

        private async void Edit_Click(object sender, RoutedEventArgs e)
        {
            var old = Of(sender); if (old == null) return;
            var w = new UserEditWindow(old, _service, isSelf: string.Equals(old.UserId, _session?.UserId, StringComparison.OrdinalIgnoreCase));
            if (w.ShowDialog() != true) return;
            try
            {
                var u = w.Result; string pwd = w.Password;
                await Task.Run(() => _service.UpdateUser(u, pwd));
                var ch = new List<string>();
                void C(string label, string a, string b) { if ((a ?? "") != (b ?? "")) ch.Add($"{label}: {a}->{b}"); }
                C("NAME", old.Name, u.Name); C("POSITION", old.Position, u.Position); C("UNIT", old.Department, u.Department);
                C("SECTION", old.Section, u.Section); C("DIVISION", old.Division, u.Division);
                if (old.Level != u.Level) ch.Add($"LEVEL: {old.Level}->{u.Level}");
                if (old.IsLocked != u.IsLocked) ch.Add($"LOCKED: {old.IsLocked}->{u.IsLocked}");
                if (!string.IsNullOrEmpty(pwd)) ch.Add("PASSWORD changed");
                LogService.WriteLog(_session?.UserId, "USER_EDIT", $"Edited user {u.UserId} | {(ch.Count == 0 ? "no change" : string.Join(", ", ch))}", u.UserId);
                NotificationManager.Show("Saved", $"แก้ไขผู้ใช้ {u.UserId} แล้ว", true);
                await ReloadAsync();
            }
            catch (Exception ex) { DialogHelper.ShowError("แก้ไขผู้ใช้ไม่สำเร็จ\n" + ex.Message); }
        }

        private async void Perm_Click(object sender, RoutedEventArgs e)
        {
            var u = Of(sender); if (u == null) return;
            List<UserPermRow> perms;
            try { perms = await Task.Run(() => _service.GetPermissions(u.UserId)); }
            catch (Exception ex) { DialogHelper.ShowError("โหลดสิทธิ์ไม่สำเร็จ\n" + ex.Message); return; }
            var before = perms.Select(p => $"{p.SysId}:{p.View}{p.Add}{p.Edit}{p.Delete}{p.Approve}").ToList();
            var w = new UserPermissionWindow(u, perms, readOnly: false);
            if (w.ShowDialog() != true) return;
            try
            {
                var rows = w.Rows;
                await Task.Run(() => _service.SavePermissions(u.UserId, rows));
                string Flags(UserPermRow p) => (p.View ? "V" : "") + (p.Add ? "A" : "") + (p.Edit ? "E" : "") + (p.Delete ? "D" : "") + (p.Approve ? "P" : "");
                var changed = rows.Where((p, i) => $"{p.SysId}:{p.View}{p.Add}{p.Edit}{p.Delete}{p.Approve}" != before[i]).Select(p => $"{p.SysId}={(Flags(p).Length == 0 ? "-" : Flags(p))}");
                LogService.WriteLog(_session?.UserId, "USER_PERMISSION", $"Permissions of {u.UserId} | {string.Join(", ", changed)}", u.UserId);
                NotificationManager.Show("Saved", $"บันทึกสิทธิ์ของ {u.UserId} แล้ว" +
                    (string.Equals(u.UserId, _session?.UserId, StringComparison.OrdinalIgnoreCase) ? "\nสิทธิ์ของคุณเองมีผลเมื่อ Login ครั้งถัดไป" : "\nมีผลเมื่อผู้ใช้ Login ครั้งถัดไป"), true);
                await ReloadAsync();
            }
            catch (Exception ex) { DialogHelper.ShowError("บันทึกสิทธิ์ไม่สำเร็จ\n" + ex.Message); }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            var u = Of(sender); if (u == null) return;
            if (string.Equals(u.UserId, _session?.UserId, StringComparison.OrdinalIgnoreCase)) { DialogHelper.ShowWarning("ไม่สามารถลบบัญชีที่กำลังใช้งานอยู่ได้"); return; }
            if (u.IsMasterAdmin) { DialogHelper.ShowWarning($"{u.UserId} เป็นผู้ดูแลระบบหลัก (Master Admin) ลบไม่ได้"); return; }
            if (!DialogHelper.ShowConfirm($"ลบผู้ใช้ {u.UserId}\n{u.Name}\n\nสิทธิ์ทั้งหมดของผู้ใช้นี้จะถูกลบด้วย ยืนยันหรือไม่?", "DELETE USER")) return;
            try
            {
                string err = await Task.Run(() => _service.DeleteUser(u.UserId));
                if (err != null) { DialogHelper.ShowWarning(err, "DELETE USER"); return; }
                LogService.WriteLog(_session?.UserId, "USER_DELETE", $"Deleted user {u.UserId} | {u.Name}", u.UserId);
                NotificationManager.Show("Deleted", $"ลบผู้ใช้ {u.UserId} แล้ว", true);
                await ReloadAsync();
            }
            catch (Exception ex) { DialogHelper.ShowError("ลบผู้ใช้ไม่สำเร็จ\n" + ex.Message); }
        }

        // 📥 Import ผู้ใช้ + สิทธิ์จาก Excel (ชีทผู้ใช้: USERID / FULLNAME ... / ชีทสิทธิ์: USERID / SYSTEMID / CANVIEW ...)
        //    ผู้ใช้: มี USERID แล้ว = อัพเดท / ยังไม่มี = เพิ่มใหม่  •  สิทธิ์: ตั้งตามไฟล์เฉพาะระบบที่มีในไฟล์ (ต้องมีสิทธิ์กำหนดสิทธิ์)
        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Import User / Permission", Filter = "Excel Files (*.xlsx)|*.xlsx" };
            if (dlg.ShowDialog() != true) return;
            bool allowMaster = _session?.UserLevel == 1 || _session?.IsMasterAdmin == true;
            UserAdminService.ImportFile file;
            try { file = await Task.Run(() => _service.ReadImport(dlg.FileName, allowMaster)); }
            catch (Exception ex) { DialogHelper.ShowError("อ่านไฟล์ Excel ไม่สำเร็จ\n" + ex.Message); return; }

            // ไม่มีสิทธิ์กำหนดสิทธิ์ -> ข้ามชีทสิทธิ์
            bool permsSkipped = file.Perms.Count > 0 && _session?.CanSetPermissions != true;
            if (permsSkipped) foreach (var p in file.Perms.Where(p => p.IsValid)) p.Error = "ไม่มีสิทธิ์กำหนดสิทธิ์ผู้ใช้ (APPROVE ของ UserManagement)";

            var users = file.Users.Where(r => r.IsValid).ToList();
            var perms = file.Perms.Where(r => r.IsValid).ToList();
            var bad = file.Users.Where(r => !r.IsValid).Select(r => $"• {r.Sheet} แถว {r.RowNumber}: {(string.IsNullOrEmpty(r.User.UserId) ? "-" : r.User.UserId)} - {r.Error}")
                      .Concat(file.Perms.Where(r => !r.IsValid).Select(r => $"• {r.Sheet} แถว {r.RowNumber}: {(string.IsNullOrEmpty(r.UserId) ? "-" : r.UserId)} {r.SysId} - {r.Error}")).ToList();
            string errs = string.Join("\n", bad.Take(10)) + (bad.Count > 10 ? $"\n• ... และอีก {bad.Count - 10:N0} แถว" : "");
            if (users.Count == 0 && perms.Count == 0) { DialogHelper.ShowError("ไม่พบรายการที่นำเข้าได้ในไฟล์นี้\n\n" + errs); return; }

            string summary = $"ไฟล์: {Path.GetFileName(dlg.FileName)}\n\n" +
                             $"ผู้ใช้: เพิ่มใหม่ {users.Count(r => r.IsNew):N0} คน  •  อัพเดท {users.Count(r => !r.IsNew):N0} คน\n" +
                             $"สิทธิ์: ตั้งค่า {perms.Count:N0} รายการ ({perms.Select(p => p.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count():N0} คน)" +
                             (bad.Count > 0 ? $"\n\n⚠ ข้ามแถวที่มีปัญหา {bad.Count:N0} แถว:\n{errs}" : "") + "\n\nยืนยันการนำเข้าหรือไม่?";
            if (!DialogHelper.ShowConfirm(summary, "IMPORT USER / PERMISSION")) return;
            try
            {
                var r = await Task.Run(() => _service.ApplyImport(file));
                LogService.WriteLog(_session?.UserId, "USER_IMPORT",
                    $"Import Excel: {Path.GetFileName(dlg.FileName)} | Added: {r.Added} | Updated: {r.Updated} | Permissions: {r.Perms} | Skipped rows: {bad.Count}", "");
                DialogHelper.ShowSuccess($"นำเข้าสำเร็จ\nผู้ใช้เพิ่มใหม่ {r.Added:N0} คน  •  อัพเดท {r.Updated:N0} คน\nสิทธิ์ {r.Perms:N0} รายการ");
                await ReloadAsync();
            }
            catch (Exception ex) { DialogHelper.ShowError("นำเข้าไม่สำเร็จ (ยกเลิกทั้งไฟล์ ไม่มีรายการใดถูกบันทึก)\n" + ex.Message); }
        }

        // 📄 แบบฟอร์ม Import (ชีท USERS / PERMISSIONS / SYSTEM IDS)
        // รูปแบบเดียวกับ Template ทั้งระบบ: ยืนยันก่อน -> Desktop\CIMS_Export -> เปิดไฟล์ขึ้นมาเลย
        private async void Template_Click(object sender, RoutedEventArgs e)
        {
            if (!DialogHelper.ShowConfirm(
                    "ต้องการสร้างไฟล์ Template สำหรับนำเข้าผู้ใช้และสิทธิ์ใช่หรือไม่?\n\n" +
                    "• ไฟล์มีชีท USERS และ PERMISSIONS พร้อมชีทวิธีกรอก และรายการรหัสระบบ / รหัสคลัง\n" +
                    "• กรอกข้อมูลแล้วนำเข้าด้วยปุ่ม IMPORT USER / PERMISSION\n\n" +
                    "กด YES เพื่อสร้างไฟล์  •  กด NO เพื่อยกเลิก", "TEMPLATE EXCEL")) return;
            btnTemplate.IsEnabled = false;
            try
            {
                string path = ImportTemplateService.NewPath("User_Permission_Template");
                await System.Threading.Tasks.Task.Run(() =>
                {
                    System.IO.Directory.CreateDirectory(ImportTemplateService.ExportFolder);
                    _service.CreateImportTemplate(path);
                });
                LogService.WriteLog(_session?.UserId, "USER_TEMPLATE", $"File: {System.IO.Path.GetFileName(path)}", "");
                NotificationManager.Show("Template", $"สร้างไฟล์ Template แล้ว\nDesktop\\CIMS_Export\\{System.IO.Path.GetFileName(path)}", true);
                ImportTemplateService.OpenFile(path);
            }
            catch (Exception ex) { DialogHelper.ShowError("สร้างไฟล์ Template ไม่สำเร็จ (ปิดไฟล์ Excel เดิมก่อน)\n" + ex.Message); }
            finally { btnTemplate.IsEnabled = true; }
        }
    }
}