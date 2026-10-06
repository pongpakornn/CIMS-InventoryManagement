using CIMS.Helpers;
using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace CIMS.Views
{
    // ป็อปอัพกรอกจำนวน ใช้สำหรับ "คืนเหล็ก" ในหน้า Multi-Scanner (IN) - สไตล์เดียวกับ CustomDialogView
    public partial class QuantityInputDialog : Window
    {
        private static readonly Regex DigitsOnly = new Regex("^[0-9]+$");

        public int Quantity { get; private set; }

        // จำนวนที่กรอก (ทศนิยมได้เมื่อเปิด allowDecimal - คลังที่ตั้ง DECIMAL QTY เช่น KG)
        public decimal DecimalQuantity { get; private set; }

        // จำนวนสูงสุดที่กรอกได้ (null = ไม่จำกัด) เช่น ยอดคงเหลือในคลังต้นทางตอนโอนย้ายบางส่วน
        private readonly decimal? _maxQty;
        private readonly bool _allowDecimal;

        public QuantityInputDialog(string title, string message, int? maxQty = null) : this(title, message, maxQty, false) { }

        public QuantityInputDialog(string title, string message, decimal? maxQty, bool allowDecimal)
        {
            InitializeComponent();
            _maxQty = maxQty;
            _allowDecimal = allowDecimal;

            lblTitle.Text = title;
            lblMessage.Text = message;

            this.Loaded += (s, e) =>
            {
                RunOpenAnimation();
                txtQuantity.Focus();
            };
        }

        private void txtQuantity_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // ทศนิยม: ให้พิมพ์จุดได้ 1 ตัว
            if (_allowDecimal && e.Text == "." && !txtQuantity.Text.Contains(".")) { e.Handled = false; return; }
            e.Handled = !DigitsOnly.IsMatch(e.Text);
        }

        private void txtQuantity_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Confirm_Click(sender, e);
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            bool ok = Qty.TryParse(txtQuantity.Text, out decimal qty) && qty > 0;
            if (ok) qty = Qty.Round(qty, _allowDecimal);
            if (ok && qty <= 0) ok = false;
            if (ok && _maxQty.HasValue && qty > _maxQty.Value)
            {
                DialogHelper.ShowWarning($"จำนวนที่กรอกเกินยอดคงเหลือ\nกรอกได้สูงสุด {Qty.Plain(_maxQty.Value)}");
            }
            else if (ok)
            {
                DecimalQuantity = qty;
                Quantity = (int)Math.Round(qty, MidpointRounding.AwayFromZero);
                this.DialogResult = true;
                CloseWindow();
            }
            else
            {
                DialogHelper.ShowWarning("กรุณากรอกจำนวนที่มากกว่า 0 ครับ");
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            CloseWindow();
        }

        private void RunOpenAnimation()
        {
            DoubleAnimation fade = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2));
            DoubleAnimation scale = new DoubleAnimation(0.8, 1, TimeSpan.FromSeconds(0.3))
            {
                EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }
            };
            MainBorder.BeginAnimation(OpacityProperty, fade);
            WindowScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale);
            WindowScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale);
        }

        private void CloseWindow()
        {
            DoubleAnimation fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.15));
            fade.Completed += (s, e) => this.Close();
            MainBorder.BeginAnimation(OpacityProperty, fade);
        }
    }
}
