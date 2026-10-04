using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CIMS.Views
{
    public partial class CustomErrorView : Window
    {
        public CustomErrorView(string title, string message)
        {
            InitializeComponent();
            lblTitle.Text = title.ToUpper();
            lblMessage.Text = message;

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
                EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }
            };

            MainBorder.BeginAnimation(OpacityProperty, fade);
            WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        }

        private void RunShakeAnimation()
        {
            DoubleAnimation shake = new DoubleAnimation
            {
                From = -12, // Error สั่นแรงกว่า Warning นิดนึง
                To = 12,
                Duration = TimeSpan.FromSeconds(0.04),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(4),
                FillBehavior = FillBehavior.Stop // กลับมา Center เสมอ
            };
            IconTranslate.BeginAnimation(TranslateTransform.XProperty, shake);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DoubleAnimation fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15));
            fade.Completed += (s, ev) => this.Close();
            MainBorder.BeginAnimation(OpacityProperty, fade);
        }
    }
}