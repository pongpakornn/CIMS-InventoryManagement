using CIMS.Helpers;
using CIMS.Models;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace CIMS.Views
{
    public partial class PREditWindow : Window
    {
        public int Qty { get; private set; }
        public string Dept => txtDept.Text.Trim();
        public string Target => txtTarget.Text.Trim();
        public string Remark => txtRemark.Text.Trim();

        public PREditWindow(PRModel pr)
        {
            InitializeComponent();
            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w != this) ?? Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            txtPrNo.Text = pr.PRNumber;
            txtProduct.Text = pr.PartName;
            txtQty.Text = pr.QTY.ToString();
            txtDept.Text = pr.Department;
            txtTarget.Text = pr.TargetDept;
            txtRemark.Text = pr.Remark;
            Loaded += (s, e) => { txtQty.Focus(); txtQty.SelectAll(); };
        }

        private void Digits_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsDigit);

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(txtQty.Text, out int q) || q <= 0) { DialogHelper.ShowWarning("จำนวนต้องมากกว่า 0"); txtQty.Focus(); return; }
            if (Dept.Length == 0) { DialogHelper.ShowWarning("กรุณากรอก Department"); txtDept.Focus(); return; }
            Qty = q;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) DialogResult = false; }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
