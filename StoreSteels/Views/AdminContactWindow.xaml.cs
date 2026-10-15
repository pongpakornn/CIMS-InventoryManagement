using CIMS.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace CIMS.Views
{
    // 🔔 ตั้งค่าการแจ้งเตือน: ผู้ติดต่อ Admin (CIMS.AdminContacts) เพิ่ม / แก้ไข / ลบ / เลื่อนลำดับ - หน้า PERMISSION / User Management
    public partial class AdminContactWindow : Window
    {
        public class Row : INotifyPropertyChanged
        {
            private int _no; private string _name, _phone, _nick;
            public int No { get => _no; set { _no = value; N(); } }
            public string FullName { get => _name; set { _name = value; N(); } }
            public string Phone { get => _phone; set { _phone = value; N(); } }
            public string Nickname { get => _nick; set { _nick = value; N(); } }
            public event PropertyChangedEventHandler PropertyChanged;
            private void N([CallerMemberName] string p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }

        private readonly ObservableCollection<Row> _rows = new ObservableCollection<Row>();

        // เปิดแก้ไข + บันทึก + Log (ใช้ทั้งหน้า PERMISSION และ User Management) - คืน true ถ้าบันทึกแล้ว
        public static bool EditAndSave(string byUserId)
        {
            var svc = new ScannerSetupService();
            List<ScannerSetupService.AdminContact> cur;
            try { cur = svc.GetAdminContacts(); }
            catch (System.Exception ex) { CIMS.Helpers.DialogHelper.ShowError("โหลดข้อมูลผู้ติดต่อไม่สำเร็จ\n" + ex.Message); return false; }
            var w = new AdminContactWindow(cur);
            if (w.ShowDialog() != true) return false;
            try
            {
                var list = w.Contacts;
                svc.SaveAdminContacts(list, byUserId);
                LogService.WriteLog(byUserId, "ALERT_CONTACT",
                    "Alert contact: " + string.Join(" / ", cur.Select(c => c.Text)) + " -> " + string.Join(" / ", list.Select(c => c.Text)), "");
                CIMS.Helpers.NotificationManager.Show("Saved", "บันทึกผู้ติดต่อแล้ว\n" + ScannerSetupService.ContactText(list), true);
                return true;
            }
            catch (System.Exception ex) { CIMS.Helpers.DialogHelper.ShowError("บันทึกผู้ติดต่อไม่สำเร็จ\n" + ex.Message); return false; }
        }

        // ผลลัพธ์หลังกด SAVE (ตามลำดับบนจอ)
        public List<ScannerSetupService.AdminContact> Contacts =>
            _rows.Where(r => !IsBlank(r)).Select(r => new ScannerSetupService.AdminContact { FullName = r.FullName?.Trim(), Phone = r.Phone?.Trim(), Nickname = r.Nickname?.Trim() }).ToList();

        public AdminContactWindow(IEnumerable<ScannerSetupService.AdminContact> current)
        {
            InitializeComponent();
            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            foreach (var c in current ?? Enumerable.Empty<ScannerSetupService.AdminContact>())
                AddRow(c.FullName, c.Phone, c.Nickname);
            if (_rows.Count == 0) AddRow("", "", "");
            icContacts.ItemsSource = _rows;
            Preview();
        }

        private void AddRow(string name, string phone, string nick)
        {
            var r = new Row { FullName = name, Phone = phone, Nickname = nick };
            r.PropertyChanged += (s, e) => { if (e.PropertyName != nameof(Row.No)) Preview(); };
            _rows.Add(r);
            Renumber();
        }

        private void Renumber() { for (int i = 0; i < _rows.Count; i++) _rows[i].No = i + 1; Preview(); }
        private static bool IsBlank(Row r) => string.IsNullOrWhiteSpace(r.FullName) && string.IsNullOrWhiteSpace(r.Phone) && string.IsNullOrWhiteSpace(r.Nickname);

        private void Preview()
        {
            if (txtPreview == null) return;
            txtPreview.Text = "กรุณาติดต่อ Admin เพื่อขอความช่วยเหลือ\n" + ScannerSetupService.ContactText(Contacts);
        }

        private void Add_Click(object sender, RoutedEventArgs e) => AddRow("", "", "");

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is Row r) { _rows.Remove(r); Renumber(); }
        }

        private void Up_Click(object sender, RoutedEventArgs e)
        {
            if (!((sender as FrameworkElement)?.Tag is Row r)) return;
            int i = _rows.IndexOf(r);
            if (i > 0) { _rows.Move(i, i - 1); Renumber(); }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var bad = _rows.Where(r => !IsBlank(r) && (string.IsNullOrWhiteSpace(r.FullName) || string.IsNullOrWhiteSpace(r.Phone))).ToList();
            if (bad.Count > 0)
            {
                txtError.Text = $"แถวที่ {string.Join(", ", bad.Select(r => r.No))}: กรุณากรอกชื่อและเบอร์ติดต่อ";
                txtError.Visibility = Visibility.Visible;
                return;
            }
            if (Contacts.Count == 0)
            {
                txtError.Text = "กรุณาใส่ผู้ติดต่ออย่างน้อย 1 คน";
                txtError.Visibility = Visibility.Visible;
                return;
            }
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) DialogResult = false; }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
