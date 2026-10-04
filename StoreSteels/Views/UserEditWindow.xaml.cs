using CIMS.Services;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CIMS.Views
{
    // 👤 เพิ่มผู้ใช้ (user = null) / แก้ไขผู้ใช้ - แก้ไข: รหัสผ่านว่าง = ไม่เปลี่ยน
    public partial class UserEditWindow : Window
    {
        private readonly UserAccount _old;
        private readonly UserAdminService _service;
        public UserAccount Result { get; private set; }
        public string Password { get; private set; }

        public UserEditWindow(UserAccount user, UserAdminService service, bool isSelf = false)
        {
            InitializeComponent();
            _old = user;
            _service = service;
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }

            cbLevel.SelectedIndex = 1;   // UserID ใหม่ = Level 2
            if (user != null)
            {
                txtTitle.Text = "✏ EDIT USER";
                txtSub.Text = "Leave the password empty to keep the current password";
                txtId.Text = user.UserId; txtId.IsReadOnly = true;
                txtName.Text = user.Name; txtPos.Text = user.Position; txtDept.Text = user.Department;
                txtSection.Text = user.Section; txtDivision.Text = user.Division;
                cbLevel.SelectedIndex = System.Math.Max(0, System.Math.Min(2, user.Level - 1));
                chkLocked.IsChecked = user.IsLocked;
                lblPwd.Text = "NEW PASSWORD (optional)";
                // ล็อก / ลดระดับบัญชีของตัวเองไม่ได้ (กันล็อกตัวเองออกจากระบบ)
                if (isSelf) { chkLocked.IsEnabled = false; cbLevel.IsEnabled = false; }
            }
            Loaded += (s, e) => { if (user == null) txtId.Focus(); else txtName.Focus(); };
        }

        private void Fail(string msg, Control focus)
        {
            txtError.Text = msg;
            txtError.Visibility = Visibility.Visible;
            focus?.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string id = txtId.Text.Trim(), name = txtName.Text.Trim(), p1 = txtPwd.Password, p2 = txtPwd2.Password;
            if (id.Length == 0) { Fail("กรุณากรอก USER ID", txtId); return; }
            if (id.Contains(" ")) { Fail("USER ID ห้ามมีช่องว่าง", txtId); return; }
            if (name.Length == 0) { Fail("กรุณากรอก NAME", txtName); return; }
            if (_old == null && p1.Length == 0) { Fail("กรุณากรอก PASSWORD", txtPwd); return; }
            if (p1 != p2) { Fail("PASSWORD และ CONFIRM PASSWORD ไม่ตรงกัน", txtPwd2); return; }
            if (_old == null)
            {
                bool exists;
                try { exists = _service.Exists(id); }
                catch (System.Exception ex) { Fail("ตรวจสอบ USER ID ไม่สำเร็จ: " + ex.Message, null); return; }
                if (exists) { Fail($"USER ID {id} มีในระบบแล้ว", txtId); return; }
            }

            Result = new UserAccount
            {
                UserId = id, Name = name,
                Position = txtPos.Text.Trim(), Department = txtDept.Text.Trim(),
                Section = txtSection.Text.Trim(), Division = txtDivision.Text.Trim(),
                Level = int.Parse(((ComboBoxItem)cbLevel.SelectedItem).Tag.ToString()),
                IsLocked = chkLocked.IsChecked == true
            };
            Password = p1.Length == 0 ? null : p1;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) DialogResult = false; }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
