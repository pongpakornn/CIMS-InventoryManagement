using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace CIMS.Views
{
    // 🏷️ ตั้งค่าที่แสดงในช่อง PRODUCT CODE ต่อคลัง (ของผู้ใช้คนนั้น): คอลัมน์ของสินค้า (PRODUCT CODE / PART A / PART NO / MODEL / PRODUCT NAME)
    //    หรือช่องที่ n ของป้ายตาม Barcode Format ที่คลังรับ (เช่น Panta #11) - แสดงผลอย่างเดียว
    public partial class ScanDisplayWindow : Window
    {
        public class Option { public string Key { get; set; } public string Label { get; set; } public override string ToString() => Label; }

        public class StockRow
        {
            public StockModel Stock { get; set; }
            public string Code => Stock.Code;
            public string Name => Stock.Name;
            public List<Option> Options { get; set; }
            public string Selected { get; set; }
            public string Original { get; set; }
        }

        private readonly UserSession _session;
        private readonly string _targetUserId;   // ผู้ใช้ที่ตั้งค่าให้ (Admin ตั้งให้คนอื่นได้จากหน้า User Management)
        private readonly bool _deferSave;
        private readonly List<StockRow> _rows;
        public bool Saved { get; private set; }
        // deferSave: ค่าที่เปลี่ยน (StockID -> ค่าที่แสดง) ให้หน้าต่างที่เปิดมาบันทึกเอง
        public Dictionary<int, string> Changes { get; private set; } = new Dictionary<int, string>();

        // targetUserId = null -> ของผู้ใช้ที่ Login อยู่ / pending = ค่าที่แก้ไว้แต่ยังไม่ได้บันทึก (เปิดซ้ำในหน้าต่างเดิม)
        public ScanDisplayWindow(UserSession session, List<StockModel> stocks, string targetUserId = null,
                                 IDictionary<int, string> pending = null, bool deferSave = false)
        {
            InitializeComponent();
            _session = session;
            _targetUserId = string.IsNullOrWhiteSpace(targetUserId) ? session?.UserId : targetUserId;
            _deferSave = deferSave;
            if (!string.Equals(_targetUserId, session?.UserId, StringComparison.OrdinalIgnoreCase))
            {
                txtTitle.Text = $"🏷️ SCAN DISPLAY • {_targetUserId}";
                if (deferSave) btnSave.Content = "OK";   // บันทึกพร้อมปุ่ม SAVE ของหน้าต่างสิทธิ์
            }
            var owner = Application.Current?.Windows.OfType<Window>().LastOrDefault(w => w.IsActive && w != this)
                        ?? Application.Current?.Windows.OfType<MainView>().FirstOrDefault();
            if (owner != null) Owner = owner; else WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var scan = new ScanService();
            Dictionary<int, string> map;
            List<BarcodeFormatModel> formats;
            try
            {
                map = scan.GetScanDisplayMap(_targetUserId);
                if (pending != null) foreach (var kv in pending) map[kv.Key] = kv.Value;
                formats = new StockService().GetFormats();
            }
            catch (Exception ex) { DialogHelper.ShowError("โหลดการตั้งค่าไม่สำเร็จ\n" + ex.Message); map = new Dictionary<int, string>(); formats = new List<BarcodeFormatModel>(); }

            _rows = stocks.Select(s =>
            {
                string cur = map.TryGetValue(s.StkId, out string v) ? v : ScanDisplay.Default;
                return new StockRow { Stock = s, Options = OptionsFor(s, formats, cur), Selected = cur, Original = cur };
            }).ToList();
            icStocks.ItemsSource = _rows;
        }

        // ตัวเลือก DISPLAY ของคลัง: คอลัมน์ของสินค้า + ช่องที่ n ของป้าย Supplier ที่คลังนี้รับ (ไม่ได้ผูกไว้ = ทุกรูปแบบ)
        public static List<Option> OptionsFor(StockModel s, List<BarcodeFormatModel> formats, string current)
        {
            var opts = ScanDisplay.Columns.Select(c => new Option { Key = c.Key, Label = c.Label }).ToList();
            var fmts = s.FormatIds != null && s.FormatIds.Count > 0 ? formats.Where(f => s.FormatIds.Contains(f.FmtId)).ToList() : formats;
            foreach (var f in fmts)
            {
                string[] sample = string.IsNullOrWhiteSpace(f.SampleText) ? new string[0] : f.SplitFields(f.SampleText);
                int count = Math.Max(sample.Length, new[] { f.MinFields, f.CodePos, f.AltCodePos ?? 0, f.QtyPos ?? 0 }.Max());
                for (int n = 1; n <= count; n++)
                {
                    string ex = n <= sample.Length ? sample[n - 1].Trim() : "";
                    opts.Add(new Option { Key = ScanDisplay.FieldKey(f.FmtId, n), Label = $"BARCODE {f.Name} #{n}" + (ex.Length > 0 ? $"   ({ex})" : "") });
                }
            }
            if (!string.IsNullOrEmpty(current) && !opts.Any(o => o.Key == current)) opts.Add(new Option { Key = current, Label = current + "  (format removed)" });
            return opts;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var changed = _rows.Where(r => r.Selected != r.Original).ToList();
            if (changed.Count == 0) { DialogResult = false; return; }
            if (_deferSave)
            {
                Changes = changed.ToDictionary(r => r.Stock.StkId, r => r.Selected);
                Saved = true;
                DialogResult = true;
                return;
            }
            try
            {
                new ScanService().SaveScanDisplayMap(changed.ToDictionary(r => r.Stock.StkId, r => r.Selected), _targetUserId);
                LogService.WriteLog(_session?.UserId, "SCAN_DISPLAY_SETTING",
                    (string.Equals(_targetUserId, _session?.UserId, StringComparison.OrdinalIgnoreCase) ? "" : $"User {_targetUserId} | ") +
                    string.Join(" | ", changed.Select(r => $"{r.Code}: {r.Original} -> {r.Selected}")), string.Join(",", changed.Select(r => r.Code)));
                NotificationManager.Show("Scan Display", "บันทึกการแสดงผลแล้ว", true);
                Saved = true;
                DialogResult = true;
            }
            catch (Exception ex) { DialogHelper.ShowError("บันทึกไม่สำเร็จ\n" + ex.Message); }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        private void Window_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) DialogResult = false; }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
    }
}
