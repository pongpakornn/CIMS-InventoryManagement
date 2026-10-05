using CIMS.Helpers;
using CIMS.Models;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CIMS.Views
{
    // ✅ หน้าต่าง Approve PR: อนุมัติทั้งหมด หรือกำหนดยอด แล้วยืนยันอีกครั้ง
    public partial class PRApproveWindow : Window
    {
        private readonly PRModel _pr;
        public bool Confirmed { get; private set; }
        public int ApprovedQty { get; private set; }
        public bool KeepRemainder { get; private set; }

        public PRApproveWindow(PRModel pr)
        {
            InitializeComponent();
            _pr = pr;
            var owner = Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }

            txtPrNo.Text = pr.PRNumber;
            txtProduct.Text = pr.PartName;
            txtRequested.Text = $"REQUESTED : {pr.QTY:N0} {pr.Unit}";
            txtAll.Text = $"APPROVE ALL  ({pr.QTY:N0})";
            txtUnit.Text = pr.Unit;
            txtQty.Text = pr.QTY.ToString();
            Loaded += (s, e) => btnConfirm.Focus();
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (txtQty == null) return;
            bool set = rbSet.IsChecked == true;
            txtQty.IsEnabled = set;
            if (set) { txtQty.Focus(); txtQty.SelectAll(); }
            UpdateRemain();
        }

        private void Qty_TextChanged(object sender, TextChangedEventArgs e) => UpdateRemain();

        private void Digits_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsDigit);

        private int? EnteredQty() => rbSet.IsChecked == true ? (int.TryParse(txtQty.Text, out int q) ? q : (int?)null) : _pr.QTY;

        private void UpdateRemain()
        {
            if (pnlRemain == null) return;
            int? q = EnteredQty();
            txtError.Visibility = Visibility.Collapsed;
            if (q.HasValue && q.Value > 0 && q.Value < _pr.QTY)
            {
                pnlRemain.Visibility = Visibility.Visible;
                txtRemain.Text = $"REMAINING : {_pr.QTY - q.Value:N0} {_pr.Unit}  (approve {q.Value:N0} of {_pr.QTY:N0})";
            }
            else pnlRemain.Visibility = Visibility.Collapsed;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            int? q = EnteredQty();
            if (!q.HasValue || q.Value <= 0 || q.Value > _pr.QTY)
            {
                txtError.Text = $"กรุณากรอกยอดที่อนุมัติ 1 - {_pr.QTY:N0}";
                txtError.Visibility = Visibility.Visible;
                txtQty.Focus();
                return;
            }
            bool partial = q.Value < _pr.QTY;
            bool keep = partial && rbKeep.IsChecked == true;
            string msg = $"อนุมัติ {_pr.PRNumber}\n{_pr.PartName}\n\nยอดที่อนุมัติ {q.Value:N0} จาก {_pr.QTY:N0} {_pr.Unit}" +
                         (partial ? (keep ? $"\nยอดที่เหลือ {_pr.QTY - q.Value:N0} จะสร้างเป็น PR ใหม่ (Waiting)" : $"\nยอดที่เหลือ {_pr.QTY - q.Value:N0} ไม่เก็บไว้") : "") +
                         "\n\nยืนยันการอนุมัติหรือไม่?";
            if (!DialogHelper.ShowConfirm(msg, "CONFIRM APPROVE")) return;

            ApprovedQty = q.Value;
            KeepRemainder = keep;
            Confirmed = true;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) DialogResult = false;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
