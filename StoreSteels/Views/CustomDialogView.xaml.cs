using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CIMS.Views
{
    public partial class CustomDialogView : Window
    {
        // เพิ่ม Info เข้าไปใน enum ตรงนี้ครับ
        public enum DialogType { Success, Confirm, Info }

        public CustomDialogView(string title, string message, DialogType type)
        {
            InitializeComponent();

            lblTitle.Text = title;
            lblMessage.Text = message;

            this.Loaded += (s, e) =>
            {
                SetupUI(type);
                RunOpenAnimation();
            };
        }

        private void SetupUI(DialogType type)
        {
            // --- 1. Reset พื้นฐาน ---
            progTime.Visibility = Visibility.Collapsed;
            progTime.BeginAnimation(ProgressBar.ValueProperty, null);

            ButtonPanel.Visibility = Visibility.Visible;
            btnConfirm.Visibility = Visibility.Visible;
            btnCancel.Visibility = Visibility.Visible;

            // เคลียร์ Animation เก่า
            txtIcon.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            txtIcon.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            CircleRotate.BeginAnimation(RotateTransform.AngleProperty, null);

            switch (type)
            {
                case DialogType.Info:
                    // สีน้ำเงินเข้ม/เทา ดูเป็นทางการ
                    StatusCircle.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34495E"));
                    txtIcon.Text = "i";
                    txtIcon.Foreground = Brushes.White;

                    // จัดระเบียบ Text ให้ชิดซ้ายเพื่อความสวยงาม
                    lblMessage.TextAlignment = TextAlignment.Left;
                    lblMessage.HorizontalAlignment = HorizontalAlignment.Left;
                    lblMessage.Margin = new Thickness(85, 0, 30, 0);  // (LEFT, TOP, RIGHT, BOTTOM)

                    // ซ่อนปุ่ม Cancel และเปลี่ยน YES เป็น OK
                    btnCancel.Visibility = Visibility.Collapsed;
                    btnConfirm.Content = "OK";
                    btnConfirm.Width = 100;
                    btnConfirm.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34495E"));
                    break;

                case DialogType.Success:
                    StatusCircle.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                    txtIcon.Text = "✓";
                    txtIcon.Foreground = Brushes.White;
                    lblMessage.TextAlignment = TextAlignment.Center; // Success ไว้กลางเหมือนเดิม

                    ButtonPanel.Visibility = Visibility.Collapsed;
                    progTime.Visibility = Visibility.Visible;
                    progTime.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));

                    RunSuccessAnimation();
                    break;

                case DialogType.Confirm:
                    StatusCircle.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3498DB"));
                    txtIcon.Text = "?";
                    txtIcon.Foreground = Brushes.White;
                    lblMessage.TextAlignment = TextAlignment.Center;

                    btnConfirm.Content = "YES";
                    btnCancel.Content = "NO";
                    btnConfirm.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3498DB"));

                    RunConfirmAnimation();
                    break;
            }
        }

        // --- ส่วนของ Animation คงเดิมตามที่คุณนนท์เขียนไว้ ---
        private void RunOpenAnimation()
        {
            DoubleAnimation fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2));
            DoubleAnimation scale = new DoubleAnimation(0.8, 1, TimeSpan.FromSeconds(0.3))
            {
                EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }
            };
            MainBorder.BeginAnimation(OpacityProperty, fade);
            WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        }

        private void RunSuccessAnimation()
        {
            DoubleAnimation rotate = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.6));
            CircleRotate.BeginAnimation(RotateTransform.AngleProperty, rotate);
            DoubleAnimation loadTime = new DoubleAnimation(0, 0, TimeSpan.FromSeconds(0.8));
            loadTime.Completed += (s, e) => CloseWindow();
            progTime.BeginAnimation(ProgressBar.ValueProperty, loadTime);
        }

        private void RunConfirmAnimation()
        {
            DoubleAnimation pulse = new DoubleAnimation(1, 1.1, TimeSpan.FromSeconds(0.5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            IconScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            IconScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }

        private void Confirm_Click(object sender, RoutedEventArgs e) { this.DialogResult = true; CloseWindow(); }
        private void Cancel_Click(object sender, RoutedEventArgs e) { this.DialogResult = false; CloseWindow(); }
        private void CloseWindow()
        {
            DoubleAnimation fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15));
            fade.Completed += (s, e) => this.Close();
            MainBorder.BeginAnimation(OpacityProperty, fade);
        }
    }
}