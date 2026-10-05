using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace CIMS.Views
{
    // 📄 หน้าต่างเลือก Template: หน้าที่มีปุ่ม Import หลายปุ่ม ให้เลือกก่อนว่าจะเตรียมไฟล์ของปุ่มไหน
    public partial class TemplateChoiceWindow : Window
    {
        public class Option
        {
            public string Key { get; set; }
            public string Title { get; set; }
            public string Detail { get; set; }
            public string UseWith { get; set; }
        }

        public string Chosen { get; private set; }

        private TemplateChoiceWindow(string subtitle, IEnumerable<Option> options)
        {
            InitializeComponent();
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            if (!string.IsNullOrWhiteSpace(subtitle)) txtSub.Text = subtitle;
            icOptions.ItemsSource = options.ToList();
        }

        // คืน Key ของตัวเลือก หรือ null ถ้ากดยกเลิก
        public static string Choose(string subtitle, params Option[] options)
        {
            var w = new TemplateChoiceWindow(subtitle, options);
            return w.ShowDialog() == true ? w.Chosen : null;
        }

        private void Option_Click(object sender, RoutedEventArgs e)
        {
            Chosen = (sender as FrameworkElement)?.Tag as string;
            DialogResult = Chosen != null;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) DialogResult = false; }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
