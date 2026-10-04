using CIMS.Helpers;
using CIMS.Services;
using CIMS.Models;
using System;
using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;

namespace CIMS.Views
{
    public partial class LoginView : Window
    {
        public LoginView()
        {
            InitializeComponent();

            // ดึงค่า Username และ Password ที่บันทึกไว้
            string savedUser = CIMS.Properties.Settings.Default.SavedUsername;
            string savedPass = CIMS.Properties.Settings.Default.SavedPassword;

            if (!string.IsNullOrEmpty(savedUser))
            {
                txt_Username.Text = savedUser;
                txt_Password.Password = savedPass; // ใส่รหัสผ่านที่จำไว้ให้เลย
                chk_Remember.IsChecked = true;

                // ถ้ามีข้อมูลครบแล้ว ให้ Focus ที่ปุ่ม Login เลยก็ได้ หรือจะ Focus ที่ Password เผื่อเขาอยากแก้
                txt_Password.Focus();
            }
            else
            {
                txt_Username.Focus();
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            string username = txt_Username.Text;
            string password = txt_Password.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                NotificationManager.Show("Warning", "กรุณาระบุชื่อผู้ใช้และรหัสผ่าน", false);
                return;
            }

            try
            {
                var authService = new AuthService();
                var session = authService.Authenticate(username, password);

                if (session != null)
                {
                    // จัดการเรื่อง Remember Me
                    if (chk_Remember.IsChecked == true)
                    {
                        CIMS.Properties.Settings.Default.SavedUsername = username;
                        CIMS.Properties.Settings.Default.SavedPassword = password; // จำรหัสผ่าน
                    }
                    else
                    {
                        CIMS.Properties.Settings.Default.SavedUsername = string.Empty;
                        CIMS.Properties.Settings.Default.SavedPassword = string.Empty;
                    }

                    CIMS.Properties.Settings.Default.Save();

                    authService.UpdateLoginStats(session.UserId);
                    LogService.WriteLog(session.UserId, "LOGIN", "เข้าสู่ระบบสำเร็จ", Environment.MachineName);

                    NotificationManager.Show("Success", $"ยินดีต้อนรับคุณ {session.UserName}", true);

                    this.IsHitTestVisible = false;
                    await Task.Delay(800);

                    var mainWin = new MainView(session);

                    // เพิ่มบรรทัดนี้ก่อนสั่ง Show เพื่อให้เปิดมาแล้วเต็มจอทันที
                    mainWin.WindowState = WindowState.Maximized;

                    mainWin.Show();
                    this.Close();
                }
                else
                {
                    NotificationManager.Show("Error", "ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง", false);
                }
            }
            catch (Exception ex)
            {
                NotificationManager.Show("Login Denied", ex.Message, false);
            }
        }

        private void txt_Username_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Logic ที่คุณนนท์ต้องการ: 
                // ถ้าชื่อในช่องพิมพ์ ไม่ตรงกับชื่อที่บันทึกไว้ (มีการเปลี่ยน User)
                if (txt_Username.Text != CIMS.Properties.Settings.Default.SavedUsername)
                {
                    txt_Password.Clear(); // เคลียร์รหัสผ่านเดิมทิ้ง
                }

                txt_Password.Focus(); // ย้ายไปช่อง Password
            }
        }

        private void txt_Password_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Login_Click(sender, e);
        }
    }
}