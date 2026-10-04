using CIMS.Helpers;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CIMS.Views
{
    // ป็อปอัพกรอกข้อความบังคับ (ห้ามว่าง) เช่น เหตุผลในการลบคลัง - สไตล์เดียวกับ QuantityInputDialog
    public partial class TextInputDialog : Window
    {
        public string InputText { get; private set; }

        public TextInputDialog(string title, string message, string confirmText = "CONFIRM", bool isDanger = false)
        {
            InitializeComponent();

            lblTitle.Text = title;
            lblMessage.Text = message;
            btnConfirm.Content = confirmText;

            if (isDanger)
            {
                var red = (Brush)new BrushConverter().ConvertFrom("#D32F2F");
                StatusCircle.Fill = red;
                btnConfirm.Background = red;
                lblIcon.Text = "!";
            }

            Loaded += (s, e) =>
            {
                RunOpenAnimation();
                txtInput.Focus();
            };
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            string text = (txtInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(text))
            {
                DialogHelper.ShowWarning("กรุณาระบุข้อความก่อนยืนยันครับ");
                txtInput.Focus();
                return;
            }
            InputText = text;
            DialogResult = true;
            CloseWindow();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            CloseWindow();
        }

        private void RunOpenAnimation()
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2));
            var scale = new DoubleAnimation(0.8, 1, TimeSpan.FromSeconds(0.3))
            {
                EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }
            };
            MainBorder.BeginAnimation(OpacityProperty, fade);
            WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        }

        private void CloseWindow()
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15));
            fade.Completed += (s, e) => Close();
            MainBorder.BeginAnimation(OpacityProperty, fade);
        }
    }
}
