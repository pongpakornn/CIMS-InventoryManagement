using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CIMS.Models;
using CIMS.Helpers; // <-- เรียกใช้ DialogHelper

namespace CIMS.Views
{
    public partial class ManageUserView : Page
    {
        private ObservableCollection<UserData> _userList;

        public ManageUserView()
        {
            InitializeComponent();
            this.DataContext = this;
            LoadUserData();
            RunEntryAnimation();
        }

        private void LoadUserData()
        {
            // ข้อมูลตัวอย่าง
            _userList = new ObservableCollection<UserData>
            {
                new UserData { EmpID="66001", FullName="NONTHAWAT PM", Position="Assistant Production Manager", Department="Production", RoleLevel="Admin" },
                new UserData { EmpID="66002", FullName="SOMCHAI IT", Position="Senior IT Support", Department="IT", RoleLevel="Admin" },
                new UserData { EmpID="67005", FullName="WICHAI WORKER", Position="Production Staff", Department="Production", RoleLevel="User" }
            };
            dgUsers.ItemsSource = _userList;
        }

        private void RunEntryAnimation()
        {
            TimeSpan duration = TimeSpan.FromSeconds(0.6);
            IEasingFunction ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            DoubleAnimation fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = duration };
            DoubleAnimation slideUp = new DoubleAnimation { From = 30, To = 0, Duration = duration, EasingFunction = ease };

            this.BeginAnimation(Page.OpacityProperty, fadeIn);
            if (PageTransform != null)
                PageTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
        }

        private void SearchUser_Click(object sender, RoutedEventArgs e)
        {
            string filter = txtSearchUser.Text.ToLower().Trim();

            DoubleAnimation fadeGrid = new DoubleAnimation { From = 0.5, To = 1, Duration = TimeSpan.FromSeconds(0.3) };
            dgUsers.BeginAnimation(DataGrid.OpacityProperty, fadeGrid);

            if (string.IsNullOrEmpty(filter))
                dgUsers.ItemsSource = _userList;
            else
            {
                dgUsers.ItemsSource = _userList.Where(x =>
                    x.FullName.ToLower().Contains(filter) ||
                    x.EmpID.Contains(filter) ||
                    x.Department.ToLower().Contains(filter)).ToList();
            }
        }

        private void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            if (dgUsers.SelectedItem is UserData selected)
            {
                // 1. ใช้ DialogHelper.ShowConfirm แทน MessageBox แบบเดิม
                bool isConfirm = DialogHelper.ShowConfirm($"ยืนยันที่จะลบผู้ใช้งาน [{selected.FullName}] ใช่หรือไม่?", "CONFIRM DELETE");

                if (isConfirm)
                {
                    try
                    {
                        // TODO: ใส่ Logic ลบข้อมูลใน Database (SQL Delete)
                        _userList.Remove(selected);

                        // 2. แจ้งสำเร็จด้วย ShowSuccess
                        DialogHelper.ShowSuccess($"ลบข้อมูลคุณ {selected.FullName} เรียบร้อยแล้ว");
                    }
                    catch (Exception ex)
                    {
                        DialogHelper.ShowError($"ไม่สามารถลบข้อมูลได้: {ex.Message}");
                    }
                }
            }
            else
            {
                DialogHelper.ShowWarning("กรุณาเลือก User ที่ต้องการลบจากตารางก่อนครับ");
            }
        }

        private void AddUser_Click(object sender, RoutedEventArgs e)
        {
            UserDetailWindow addUserWin = new UserDetailWindow();
            addUserWin.Owner = Window.GetWindow(this);

            // ถ้า ShowDialog คืนค่า true (กดบันทึกสำเร็จ)
            if (addUserWin.ShowDialog() == true)
            {
                // TODO: Refresh ข้อมูลจาก Database เพื่ออัปเดต List ล่าสุด
                // LoadUserData(); 
            }
        }

        private void EditUser_Click(object sender, RoutedEventArgs e)
        {
            if (dgUsers.SelectedItem is UserData selected)
            {
                UserDetailWindow editWin = new UserDetailWindow(selected);
                editWin.Owner = Window.GetWindow(this);

                if (editWin.ShowDialog() == true)
                {
                    // Refresh ข้อมูลหลังจากแก้ไขสำเร็จ
                }
            }
            else
            {
                DialogHelper.ShowWarning("กรุณาเลือก User ที่ต้องการแก้ไขก่อนครับ");
            }
        }

        private void ViewUser_Click(object sender, RoutedEventArgs e)
        {
            // ดึงข้อมูลจาก DataContext ของปุ่มที่ถูกคลิก
            if (sender is Button btn && btn.DataContext is UserData selected)
            {
                // 1. สร้างข้อความสรุปข้อมูล User
                string userInfo = $"EMPLOYEE ID :  {selected.EmpID}\n" +
                                  $"FULL NAME   :  {selected.FullName}\n" +
                                  $"POSITION    :  {selected.Position}\n" +
                                  $"DEPARTMENT  :  {selected.Department}\n" +
                                  $"ROLE LEVEL  :  {selected.RoleLevel}";

                // 2. เรียกใช้ DialogHelper.ShowInfo แสดงผล
                DialogHelper.ShowInfo(userInfo, "USER PROFILE INFORMATION");

                /* หมายเหตุ: หากคุณนนท์ยังต้องการเปิดหน้า UserDetailWindow แบบ ReadOnly 
                   ควบคู่ไปด้วย สามารถปลดคอมเมนต์ด้านล่างนี้ได้ครับ:

                   UserDetailWindow viewWin = new UserDetailWindow(selected);
                   viewWin.Owner = Window.GetWindow(this);
                   viewWin.ShowDialog();
                */
            }
        }
    }
}