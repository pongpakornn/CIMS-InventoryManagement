using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Linq;
using CIMS.Helpers; // <-- เรียกใช้ Helper ของเรา

namespace CIMS.Views
{
    public partial class UserDetailWindow : Window
    {
        private bool _isEditMode = false;

        public UserDetailWindow()
        {
            InitializeComponent();
            LoadInitialData();
            this.Opacity = 0;
            this.Loaded += UserDetailWindow_Loaded;
        }

        public UserDetailWindow(dynamic selectedUser) : this()
        {
            _isEditMode = true;

            txtTitle.Text = "UPDATE USER PROFILE";
            btnSave.Content = "UPDATE USER";

            txtEmpID.Text = selectedUser.EmpID?.ToString();
            txtEmpID.IsEnabled = false;

            txtFullName.Text = selectedUser.FullName?.ToString();
            txtPosition.Text = selectedUser.Position?.ToString();

            cbDept.SelectedItem = selectedUser.Department?.ToString();

            foreach (var item in cbRole.Items)
            {
                if (item.ToString().Contains(selectedUser.RoleLevel?.ToString() ?? ""))
                {
                    cbRole.SelectedItem = item;
                    break;
                }
            }
        }

        private void LoadInitialData()
        {
            cbDept.ItemsSource = new List<string> { "IT", "Production", "Accounting", "HR", "Store" };
            cbRole.ItemsSource = new List<string> { "10 - Admin (Full Access)", "20 - Manager", "30 - Staff", "40 - Viewer" };

            cbDept.SelectedIndex = 0;
            cbRole.SelectedIndex = 2;
        }

        private void UserDetailWindow_Loaded(object sender, RoutedEventArgs e)
        {
            DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.4));
            this.BeginAnimation(Window.OpacityProperty, fadeIn);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // 1. Validation: ใช้ ShowWarning แทน MessageBox
            if (string.IsNullOrWhiteSpace(txtEmpID.Text) || string.IsNullOrWhiteSpace(txtFullName.Text))
            {
                DialogHelper.ShowWarning("กรุณากรอกข้อมูล EmpID และ Full Name ให้ครบถ้วนด้วยครับ", "ข้อมูลไม่สมบูรณ์");
                return;
            }

            try
            {
                if (_isEditMode)
                {
                    // TODO: ใส่ SQL UPDATE สำหรับแก้ไขข้อมูล
                    // หลังจาก Update DB สำเร็จ ใช้ ShowSuccess
                    DialogHelper.ShowSuccess($"อัปเดตข้อมูลคุณ {txtFullName.Text} เรียบร้อยครับ!");
                }
                else
                {
                    // TODO: ใส่ SQL INSERT สำหรับเพิ่มข้อมูลใหม่
                    // หลังจาก Insert DB สำเร็จ ใช้ ShowSuccess
                    DialogHelper.ShowSuccess($"บันทึก User: {txtFullName.Text} เข้าสู่ระบบแล้ว");
                }

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                // หากเกิด Error ระหว่างติดต่อ Database
                DialogHelper.ShowError($"ไม่สามารถบันทึกข้อมูลได้: {ex.Message}");
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // ก่อนจะปิดหน้าเพิ่มข้อมูล ถ้ามีการพิมพ์ค้างไว้อาจจะถาม Confirm สักหน่อย
            if (!string.IsNullOrWhiteSpace(txtFullName.Text))
            {
                if (!DialogHelper.ShowConfirm("ข้อมูลที่คุณกรอกไว้จะหายไป ต้องการยกเลิกใช่ไหมครับ?"))
                    return;
            }

            this.DialogResult = false;
            this.Close();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            try { this.DragMove(); } catch { }
        }
    }
}