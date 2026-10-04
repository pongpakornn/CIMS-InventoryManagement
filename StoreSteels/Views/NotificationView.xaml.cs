using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CIMS.Views
{
    public partial class NotificationView : Window
    {
        public NotificationView(string title, string message, bool isSuccess)
        {
            InitializeComponent();

            // ตั้งค่าข้อมูล
            txtTitle.Text = title;
            txtMessage.Text = message;

            if (!isSuccess)
            {
                txtIcon.Text = "✕";
                IconCircle.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E74C3C"));
                ProgBar.Foreground = IconCircle.Fill;
            }

            this.Loaded += NotificationView_Loaded;
        }

        private async void NotificationView_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. Animation หลอดโหลดไหลถอยหลัง
            DoubleAnimation da = new DoubleAnimation(100, 0, new Duration(TimeSpan.FromSeconds(3)));
            ProgBar.BeginAnimation(ProgressBar.ValueProperty, da);

            // 2. Fade In ตอนปรากฏตัว
            DoubleAnimation fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(400)));
            this.BeginAnimation(Window.OpacityProperty, fadeIn);

            await Task.Delay(3000);

            // 3. Fade Out และ Slide Down ตอนหายไป
            DoubleAnimation fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(500)));
            DoubleAnimation slideDown = new DoubleAnimation(this.Top, this.Top + 30, new Duration(TimeSpan.FromMilliseconds(500)));

            fadeOut.Completed += (s, a) => this.Close();

            this.BeginAnimation(Window.OpacityProperty, fadeOut);
            this.BeginAnimation(Window.TopProperty, slideDown);
        }
    }
}