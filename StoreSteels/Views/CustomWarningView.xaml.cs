using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CIMS.Views
{
    public partial class CustomWarningView : Window
    {
        public CustomWarningView(string title, string message)
        {
            InitializeComponent();
            lblTitle.Text = title;
            lblMessage.Text = message;

            // กำหนดให้ขยายจากจุดกึ่งกลางของ Border
            MainBorder.RenderTransformOrigin = new Point(0.5, 0.5);

            this.Loaded += (s, e) => {
                RunOpenAnimation();
                RunShakeAnimation();
            };
        }

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

        private void RunShakeAnimation()
        {
            // เขย่า Icon เล็กน้อยเพื่อเตือน
            DoubleAnimation shake = new DoubleAnimation
            {
                From = -10,
                To = 10,
                Duration = TimeSpan.FromSeconds(0.05),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3),
                // เพิ่มบรรทัดนี้ครับ: เมื่อจบ Animation ให้คืนค่ากลับมาที่ 0 (จุดเดิม)
                FillBehavior = FillBehavior.Stop
            };

            IconTranslate.BeginAnimation(TranslateTransform.XProperty, shake);
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DoubleAnimation fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15));
            fade.Completed += (s, ev) => this.Close();
            MainBorder.BeginAnimation(OpacityProperty, fade);
        }
    }
}