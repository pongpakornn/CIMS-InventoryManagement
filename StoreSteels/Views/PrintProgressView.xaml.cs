using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace CIMS.Views
{
    public partial class PrintProgressView : Window
    {
        public PrintProgressView()
        {
            InitializeComponent();
            this.Loaded += (s, e) => RunOpenAnimation();
        }

        // อัปเดตความคืบหน้าจริงตามจำนวนใบที่พิมพ์ไปแล้ว (เรียกจาก UI thread เท่านั้น)
        public void UpdateProgress(int current, int total)
        {
            lblStatus.Text = $"กำลังพิมพ์ {current} / {total}";
            double percent = total > 0 ? (double)current / total * 100 : 0;

            DoubleAnimation anim = new DoubleAnimation(percent, TimeSpan.FromMilliseconds(150));
            progPrint.BeginAnimation(System.Windows.Controls.ProgressBar.ValueProperty, anim);
        }

        public void Finish()
        {
            DoubleAnimation fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.2));
            fade.Completed += (s, e) => this.Close();
            MainBorder.BeginAnimation(OpacityProperty, fade);
        }

        private void RunOpenAnimation()
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2));
            var scale = new DoubleAnimation(0.8, 1, TimeSpan.FromSeconds(0.3))
            {
                EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }
            };
            MainBorder.BeginAnimation(OpacityProperty, fade);
            WindowScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale);
            WindowScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale);
        }
    }
}
