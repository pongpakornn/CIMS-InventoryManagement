using CIMS.Services;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CIMS.Views
{
    // 🔑 สิทธิ์ของผู้ใช้ทุกระบบ + ทุกคลัง / readOnly = ปุ่ม VIEW (ดูข้อมูลผู้ใช้ + สิทธิ์ แก้ไม่ได้)
    public partial class UserPermissionWindow : Window
    {
        public List<UserPermRow> Rows { get; }

        public UserPermissionWindow(UserAccount user, List<UserPermRow> rows, bool readOnly)
        {
            InitializeComponent();
            Rows = rows;
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }

            txtTitle.Text = readOnly ? "👤 USER PROFILE" : "🔑 PERMISSION";
            txtUser.Text = $"{user.UserId}  •  {user.Name}  •  {user.LevelText}";
            pnlLevel1.Visibility = user.Level == 1 ? Visibility.Visible : Visibility.Collapsed;

            if (readOnly)
            {
                foreach (var (label, value) in new[] {
                    ("USER ID", user.UserId), ("NAME", user.Name), ("POSITION", user.Position), ("UNIT", user.Department),
                    ("SECTION", user.Section), ("DIVISION", user.Division), ("LEVEL", user.LevelText), ("STATUS", user.StatusText),
                    ("LAST LOGIN", user.LastLogin?.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "-") })
                {
                    var b = new Border { Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF5, 0xFA)), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 8), MinWidth = 140 };
                    var sp = new StackPanel();
                    sp.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x84, 0x90)) });
                    sp.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(value) ? "-" : value, FontSize = 13, FontWeight = FontWeights.Black, Foreground = new SolidColorBrush(Color.FromRgb(0x2D, 0x2A, 0x32)) });
                    b.Child = sp;
                    pnlProfile.Children.Add(b);
                }
                icPerms.IsEnabled = false;
                pnlQuick.Visibility = Visibility.Collapsed;
                btnSave.Visibility = Visibility.Collapsed;
                btnCancel.Content = "CLOSE";
            }
            Refresh();
        }

        // วาดแถวใหม่หลังกด VIEW ALL / SELECT ALL / CLEAR (หัวกลุ่ม SYSTEMS / STOCKS มาจาก GroupTitle)
        private void Refresh()
        {
            icPerms.ItemsSource = null;
            icPerms.ItemsSource = Rows;
        }

        private void ViewAll_Click(object sender, RoutedEventArgs e) { foreach (var r in Rows) r.View = true; Refresh(); }
        private void All_Click(object sender, RoutedEventArgs e) { foreach (var r in Rows) r.View = r.Add = r.Edit = r.Delete = r.Approve = true; Refresh(); }
        private void Clear_Click(object sender, RoutedEventArgs e) { foreach (var r in Rows) r.View = r.Add = r.Edit = r.Delete = r.Approve = false; Refresh(); }

        private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) DialogResult = false; }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
