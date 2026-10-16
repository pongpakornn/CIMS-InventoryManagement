using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace CIMS.Views
{
    // ▣ QR CODE (หน้าต่าง ADMIN ของ Inventory Registration - Level 1 เท่านั้น)
    //   QR ที่ EXPORT QR SELECT สร้าง ต่อคลัง: CODE = PRODUCT CODE อย่างเดียว / CODE_NAME = PRODUCT CODE|PRODUCT NAME (ค่าเดิม)
    public partial class QrFormatPanel : UserControl
    {
        public class Row : INotifyPropertyChanged
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public string Group => "qr" + Id;
            public string Original { get; set; }
            private string _value;
            public string Value
            {
                get => _value;
                set { _value = value; Notify(); Notify(nameof(CodeOnly)); Notify(nameof(CodeAndName)); Notify(nameof(Example)); Notify(nameof(ChangedVisibility)); }
            }
            public bool CodeOnly { get => Value == "CODE"; set { if (value) Value = "CODE"; } }
            public bool CodeAndName { get => Value != "CODE"; set { if (value) Value = "CODE_NAME"; } }
            public string SampleCode { get; set; } = "PD-0001";
            public string SampleName { get; set; } = "PRODUCT NAME";
            public string Example => CodeOnly ? SampleCode : $"{SampleCode}|{SampleName}";
            public bool Changed => Value != Original;
            public Visibility ChangedVisibility => Changed ? Visibility.Visible : Visibility.Collapsed;
            public event PropertyChangedEventHandler PropertyChanged;
            private void Notify([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        private readonly UserSession _session;
        private readonly List<Row> _rows = new List<Row>();
        private Dictionary<int, ProductControlLayout> _map;

        public QrFormatPanel(UserSession session, List<StockModel> stocks)
        {
            InitializeComponent();
            _session = session;
            try { _map = new ProductControlLayoutService().GetAll(); }
            catch (Exception ex) { _map = new Dictionary<int, ProductControlLayout>(); DialogHelper.ShowError("โหลดการตั้งค่า QR ไม่สำเร็จ\n" + ex.Message); }

            void Add(int id, string code)
            {
                string v = _map.TryGetValue(id, out var l) ? (l.QrCodeOnly ? "CODE" : "CODE_NAME") : "CODE_NAME";
                _rows.Add(new Row { Id = id, Code = code, Original = v, Value = v });
            }
            Add(0, "ALL STOCKS");
            foreach (var s in stocks) Add(s.StkId, s.Code);
            icRows.ItemsSource = _rows;
            if (!DbSchema.HasProductControlSettings) txtStatus.Text = "⚠ Run Database/Update_20261016c.sql first - settings cannot be saved yet.";
        }

        // ✨ เปิดแผง = แถวคลังไหลเข้าทีละแถว
        private void Panel_Loaded(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(new Action(() => Motion.Stagger(icRows, 30)), System.Windows.Threading.DispatcherPriority.Loaded);

        private void Option_Checked(object sender, RoutedEventArgs e)
        {
            int n = _rows.Count(r => r.Changed);
            btnSave.IsEnabled = n > 0 && DbSchema.HasProductControlSettings;
            txtStatus.Text = n > 0 ? $"{n} stock(s) changed - press SAVE" : "";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var changed = _rows.Where(r => r.Changed).ToList();
            if (changed.Count == 0) return;
            try
            {
                // คอลัมน์ / จัดกลุ่มของคลัง = ค่าล่าสุดในฐานข้อมูล (ไม่ทับค่าที่ตั้งในหน้า TABLE COLUMNS)
                var now = new ProductControlLayoutService().GetAll();
                var save = changed.Select(r =>
                {
                    var l = now.TryGetValue(r.Id, out var x) ? x.Clone() : ProductControlColumns.Default(r.Id);
                    l.QrContent = r.Value;
                    return l;
                }).ToList();
                new ProductControlLayoutService().Save(save, _session?.UserId);
                LogService.WriteLog(_session?.UserId, "PRODUCT_CONTROL_QR",
                    string.Join(" | ", changed.Select(r => $"{r.Code}: {r.Original} -> {r.Value}")), string.Join(",", changed.Select(r => r.Code)));
                foreach (var r in changed) { r.Original = r.Value; r.Value = r.Value; }
                Option_Checked(null, null);
                NotificationManager.Show("QR CODE", $"บันทึกรูปแบบ QR ของ {string.Join(", ", changed.Select(r => r.Code))} แล้ว", true);
            }
            catch (Exception ex) { DialogHelper.ShowError("บันทึกไม่สำเร็จ\n" + ex.Message); }
        }

        public bool HasUnsaved => _rows.Any(r => r.Changed);
    }
}
