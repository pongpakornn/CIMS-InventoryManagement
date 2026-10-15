using CIMS.Core;
using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using Microsoft.Data.SqlClient;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CIMS.Views
{
    // 📋 NOT DEDUCTED > ＋ PRODUCT CONTROL: ลงทะเบียนสินค้าของป้ายที่ค้างอยู่ (หรือเลือกสินค้าที่มีแล้ว) แล้วรับเข้าคลังตามป้าย
    //    + ตัด Coil แม่ในคลังต้นทางเท่าที่ตัดได้ -> รายการไปแสดงที่ DEDUCTED (สถานะ REGISTERED)
    //    MAX / MIN / STOCK COIL / STOCK (KG) ไม่บังคับ: STOCK (KG) ว่าง = จำนวนตามป้าย / STOCK COIL ว่าง = 1 Coil ตามป้าย
    public partial class NotDeductedRegisterWindow : Window
    {
        private readonly UserSession _session;
        private readonly DeductMissRow _row;
        private readonly StockModel _stock;
        private readonly ProductControlView _page;
        private bool _busy;
        public bool Done { get; private set; }

        public NotDeductedRegisterWindow(UserSession session, DeductMissRow row)
        {
            InitializeComponent();
            _session = session;
            _row = row;
            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
            if (owner != null && owner != this) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            // ทุกขนาดจอ: ไม่เกินพื้นที่จอ
            var area = SystemParameters.WorkArea;
            Width = Math.Min(Width, area.Width - 10);
            Height = Math.Min(Height, area.Height - 10);

            _stock = new StockService().GetStocks().FirstOrDefault(s => s.StkId == row.StockId);
            var fmt = row.FormatId.HasValue ? new StockService().GetFormats().FirstOrDefault(f => f.FmtId == row.FormatId.Value) : null;

            txtSub.Text = $"{row.DateText}  •  {row.UserId}  •  {row.ReasonText}";
            txtBarcode.Text = row.Barcode;
            AddChip("COIL NO", row.CoilNo);
            AddChip("MOTHER COIL", row.MotherCoil);
            AddChip("QTY (KG)", row.LabelQtyText);
            AddChip("FORMAT", fmt?.Name);
            if (row.HasProduct) AddChip("PD CODE", row.PartCode);
            txtReceive.Text = $"RECEIVE INTO {row.StockCode}  •  CUT {row.SourceCode}";
            txtReason.Text = row.MotherNow.HasValue ? $"Mother coil {row.MotherCoil} now in {row.SourceCode}: {Qty.Plain(row.MotherNow.Value)} KG" : $"Mother coil {row.MotherCoil} is not in {row.SourceCode} yet";
            txtStockHead.Text = $"{row.StockCode}  •  MAX / MIN / STOCK (optional)";
            bool kg = _stock?.AllowDecimal == true;
            lblQty.Text = kg ? "STOCK (KG)" : "STOCK (QTY)";
            pnlCoil.Visibility = _stock?.CountCoil == true ? Visibility.Visible : Visibility.Collapsed;
            txtHint.Text = $"Blank = as the label: {row.LabelQtyText}{(kg ? " KG" : "")}" + (_stock?.CountCoil == true ? " / 1 coil" : "") +
                           $". The amount received is cut from mother coil {row.MotherCoil} in {row.SourceCode} when it is there.";

            // ข้อมูลจากป้ายบางส่วน: ชื่อสินค้า (รูปแบบที่จับคู่ด้วยชื่อ) / รหัสบนป้าย (ช่อง QR CODE) / Supplier = ชื่อรูปแบบ
            string name = null, code = null;
            if (fmt != null && !string.IsNullOrWhiteSpace(row.Barcode))
            {
                var f = fmt.SplitFields(row.Barcode);
                string Field(int n) => n >= 1 && n <= f.Length ? f[n - 1].Trim() : null;
                if (fmt.MatchByName) name = string.Join(" ", fmt.NamePositions().Select(Field).Where(s => !string.IsNullOrWhiteSpace(s)));
                else code = Field(fmt.CodePos);
            }
            _page = new ProductControlView(session) { ForceAllowRegister = true };
            _page.PartRegistered += async (ptId, partCode) => await ReceiveAsync(ptId > 0 ? ptId : FindPartId(partCode), partCode);
            frmProduct.Navigate(_page);
            _page.Loaded += (s, e) => _page.Prefill(row.HasProduct ? row.PartCode : null, row.HasProduct ? row.PartName : name, code, fmt?.Name,
                                                    row.HasProduct ? row.PartCode : (name ?? code));
        }

        private void AddChip(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var b = new Border { Background = System.Windows.Media.Brushes.White, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 8, 0) };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = label + "  ", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8A, 0x84, 0x90)), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeights.Black, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x2A, 0x32)) });
            b.Child = sp;
            pnlLabel.Children.Add(b);
        }

        private static int FindPartId(string partCode)
        {
            using (var conn = new SqlConnection(GlobalConfig.ConnStr))
            using (var cmd = new SqlCommand("SELECT TOP 1 PartID FROM CIMS.Parts WHERE PartCode = @c ORDER BY PartID DESC", conn))
            {
                cmd.Parameters.AddWithValue("@c", partCode ?? "");
                conn.Open();
                object v = cmd.ExecuteScalar();
                return v == null || v == DBNull.Value ? 0 : Convert.ToInt32(v);
            }
        }

        private static bool TryNum(string text, out decimal? value)
        {
            value = null;
            string t = (text ?? "").Replace(",", "").Trim();
            if (t.Length == 0) return true;
            if (decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d) && d >= 0) { value = d; return true; }
            return false;
        }

        // สินค้ามีอยู่แล้ว: เลือกแถวในตาราง แล้วรับเข้าด้วยสินค้านั้น
        private async void UseSelected_Click(object sender, RoutedEventArgs e)
        {
            var p = _page.SelectedTableProduct;
            if (p == null || string.IsNullOrWhiteSpace(p.PartCode)) { DialogHelper.ShowWarning("กรุณาเลือกสินค้าในตารางด้านล่างก่อน\n(ค้นหา PD CODE / ชื่อสินค้า แล้วคลิกแถว)"); return; }
            await ReceiveAsync(p.PtId > 0 ? p.PtId : FindPartId(p.PartCode), p.PartCode);
        }

        private async Task ReceiveAsync(int partId, string partCode)
        {
            if (_busy) return;
            if (partId <= 0) { DialogHelper.ShowError($"ไม่พบสินค้า {partCode} ในระบบ"); return; }
            if (!TryNum(txtMax.Text, out var max) || !TryNum(txtMin.Text, out var min) || !TryNum(txtCoil.Text, out var coilCount) || !TryNum(txtQty.Text, out var qtyIn))
            {
                DialogHelper.ShowWarning("MAX / MIN / STOCK ต้องเป็นตัวเลข (เว้นว่างได้)");
                return;
            }
            decimal qty = Qty.Round(qtyIn ?? _row.LabelQty, _stock?.AllowDecimal ?? true);
            if (qty <= 0) { DialogHelper.ShowWarning("จำนวนที่รับเข้าต้องมากกว่า 0"); return; }
            string cutText = _row.MotherNow.HasValue ? $"ตัด Coil แม่ {_row.MotherCoil} ใน {_row.SourceCode} (มี {Qty.Plain(_row.MotherNow.Value)} KG)"
                                                     : $"Coil แม่ {_row.MotherCoil} ยังไม่มีใน {_row.SourceCode} (รับเข้าอย่างเดียว)";
            if (!DialogHelper.ShowConfirm($"รับเข้า {_row.StockCode}\n{partCode}\nจำนวน {Qty.Plain(qty)}" + (coilCount.HasValue ? $"  •  {coilCount} Coil" : "") +
                                          (max.HasValue || min.HasValue ? $"\nMAX {(max.HasValue ? Qty.Plain(max.Value) : "-")} / MIN {(min.HasValue ? Qty.Plain(min.Value) : "-")}" : "") +
                                          $"\n\n{cutText}\n\nยืนยันหรือไม่?", "REGISTER & RECEIVE")) return;
            _busy = true;
            try
            {
                var svc = new DeductMissService();
                string uid = _session?.UserId;
                var res = await Task.Run(() => svc.Receive(_row, partId, qty, uid, "REGISTERED"));
                if (!res.Ok) { DialogHelper.ShowError(res.Message); return; }
                if (max.HasValue || min.HasValue || (coilCount.HasValue && coilCount.Value != 1))
                    await Task.Run(() => svc.ApplyStockSetting(_row.StockId, partId, max, min, coilCount.HasValue ? (int?)Math.Max(0, (int)coilCount.Value - 1) : null, uid));
                Done = true;
                var d = res.Deduct;
                DialogHelper.ShowSuccess($"รับเข้า {_row.StockCode} แล้ว  {partCode}  {Qty.Plain(qty)}\n" +
                    (d != null && d.Deducted > 0 ? $"ตัด {d.SourceCode} {Qty.Plain(d.Deducted)} ({d.SourcePartCode} เหลือ {Qty.Plain(d.SourceAfter)})" : $"ยังไม่ได้ตัด {_row.SourceCode} (ไม่มี Coil แม่)") +
                    "\n\nรายการย้ายไปที่ DEDUCTED แล้ว");
                Close();
            }
            catch (Exception ex) { DialogHelper.ShowError("บันทึกไม่สำเร็จ\n" + ex.Message); }
            finally { _busy = false; }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
