// ⚡ เรียลไทม์ทุกหน้า: เครื่องอื่นแก้ / เพิ่ม / ลบข้อมูล -> หน้าที่เปิดอยู่อัพเดทเองโดยไม่ต้องกดรีเฟรช
//   ทุก N วินาทีถามฐานข้อมูลแค่ "ค่าตรวจการเปลี่ยนแปลง" (COUNT + CHECKSUM ไม่กี่ ms) ในเบื้องหลัง
//   ค่าเปลี่ยน -> โหลดข้อมูลของหน้านั้นใหม่ครั้งเดียว (แต่ละหน้าเก็บคำค้น / ตัวกรอง / ตำแหน่งเลื่อน / รายการที่ติ๊กไว้เอง)
//   ไม่รบกวนผู้ใช้: ข้ามรอบเมื่อกำลังพิมพ์ในตาราง / กดเมาส์ค้าง / เปิดหน้าต่างหรือกล่องข้อความอยู่ / หน้าไม่ได้แสดง
//   ฐานข้อมูลช้าหรือหลุด -> ไม่ขึ้น Error เว้นระยะถามนานขึ้นเอง แล้วกลับมาปกติเมื่อต่อได้
using Microsoft.Data.SqlClient;
using CIMS.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CIMS.Helpers
{
    public sealed class LiveRefresh
    {
        private readonly FrameworkElement _page;
        private readonly Func<string> _token;
        private readonly Func<Task> _reload;
        private readonly Func<bool> _busy;
        private readonly TimeSpan _every;
        private readonly DispatcherTimer _timer;
        private string _last;
        private bool _running;
        private int _fails;

        private LiveRefresh(FrameworkElement page, TimeSpan every, Func<string> token, Func<Task> reload, Func<bool> busy)
        {
            _page = page; _every = every; _token = token; _reload = reload; _busy = busy;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = every };
            _timer.Tick += Tick;
            page.Loaded += (s, e) => Start();
            page.Unloaded += (s, e) => _timer.Stop();
            if (page.IsLoaded) Start();
        }

        // token = คำสั่งอ่านค่าตรวจการเปลี่ยนแปลง (รันเบื้องหลัง) / reload = โหลดหน้าใหม่ (รันบนหน้าจอ) / busy = เงื่อนไขเพิ่มของหน้านั้นที่ห้ามโหลด
        public static LiveRefresh Attach(FrameworkElement page, TimeSpan every, Func<string> token, Func<Task> reload, Func<bool> busy = null)
            => new LiveRefresh(page, every, token, reload, busy);

        public static LiveRefresh Attach(FrameworkElement page, TimeSpan every, Func<string> token, Action reload, Func<bool> busy = null)
            => new LiveRefresh(page, every, token, () => { reload(); return Task.CompletedTask; }, busy);

        private void Start()
        {
            _fails = 0;
            _timer.Interval = _every;
            _timer.Start();
            _ = Baseline();
        }

        // ค่าตั้งต้น = ตอนหน้าเปิด (หน้ากำลังโหลดข้อมูลชุดเดียวกันอยู่) - เก็บทันที ไม่รอรอบแรก
        //   ถ้ารอรอบแรก การแก้ไขจากเครื่องอื่นในช่วงนั้นจะกลายเป็นค่าตั้งต้นและไม่ถูกแสดง
        private async Task Baseline()
        {
            _last = null;
            if (_running) return;
            _running = true;
            try { _last = await Task.Run(_token); }
            catch { _last = null; }
            finally { _running = false; }
        }

        // หน้าโหลดข้อมูลใหม่เองแล้ว (เช่นผู้ใช้กดบันทึก) -> เก็บค่าตั้งต้นใหม่ ไม่โหลดซ้ำ
        public void Rebase() => _ = Baseline();

        private async void Tick(object sender, EventArgs e)
        {
            if (_running || !_page.IsVisible) return;
            if (UserBusy(_page) || (_busy?.Invoke() ?? false)) return;
            _running = true;
            try
            {
                string t = await Task.Run(_token);
                if (_fails > 0) { _fails = 0; _timer.Interval = _every; }
                if (_last == null) { _last = t; return; }
                if (t == _last) return;
                // ผู้ใช้เริ่มทำอะไรระหว่างรอ -> ไว้รอบหน้า (ไม่อัพเดทค่า จะได้โหลดเมื่อว่าง)
                if (!_page.IsVisible || UserBusy(_page) || (_busy?.Invoke() ?? false)) return;
                _last = t;
                await _reload();
            }
            catch (Exception ex)
            {
                // ฐานข้อมูลช้า / หลุด: ไม่ขึ้นหน้าต่าง Error ระหว่างทำงาน - เว้นระยะนานขึ้น (สูงสุด 1 นาที)
                _fails++;
                _timer.Interval = TimeSpan.FromSeconds(Math.Min(60, _every.TotalSeconds * (1 + _fails)));
                System.Diagnostics.Debug.WriteLine($"LiveRefresh {_page.GetType().Name}: {ex.Message}");
            }
            finally { _running = false; }
        }

        // ผู้ใช้กำลังทำอะไรอยู่ -> อย่าโหลดทับ
        public static bool UserBusy(FrameworkElement page)
        {
            if (ComponentDispatcher.IsThreadModal) return true;                         // มีหน้าต่าง / กล่องข้อความ (ShowDialog) เปิดอยู่
            if (Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed) return true;
            if (Keyboard.FocusedElement is DependencyObject f)
            {
                if (f is ComboBox cb && cb.IsDropDownOpen) return true;
                if (f is ComboBoxItem || f is MenuItem) return true;
                // พิมพ์อยู่ในเซลล์ตาราง (แก้ MAX / MIN / REMARK ...)
                if (f is TextBoxBase && FindAncestor<DataGridCell>(f) != null) return true;
                if (f is DataGridCell c && c.IsEditing) return true;
            }
            foreach (var g in FindChildren<DataGrid>(page))
                if (IsEditing(g)) return true;
            return false;
        }

        private static bool IsEditing(DataGrid g)
        {
            // DataGrid ไม่มี IsEditing - ดูจากเซลล์ปัจจุบัน
            var cell = g.CurrentCell;
            if (cell.Column == null || cell.Item == null) return false;
            var content = cell.Column.GetCellContent(cell.Item);
            return content != null && FindAncestor<DataGridCell>(content) is DataGridCell dc && dc.IsEditing;
        }

        private static T FindAncestor<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T))
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }

        private static IEnumerable<T> FindChildren<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) yield break;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is T t) { yield return t; continue; }   // ไม่ลงไปในตาราง (แถวเป็นพัน)
                foreach (var x in FindChildren<T>(c)) yield return x;
            }
        }

        // ⚡ ค่าตรวจการเปลี่ยนแปลง: รันทุกคำสั่ง (แถวแรก ทุกคอลัมน์) ต่อกันเป็นข้อความเดียว
        //   คำสั่งที่พัง (เช่นตารางยังไม่มีในฐานเก่า) = ข้าม ไม่ทำให้หน้าเสีย
        public static string DbToken(params string[] sqls)
        {
            var sb = new StringBuilder();
            using (var conn = new SqlConnection(GlobalConfig.ConnStr))
            {
                conn.Open();
                foreach (var sql in sqls)
                {
                    try
                    {
                        using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 })
                        using (var r = cmd.ExecuteReader())
                            if (r.Read())
                                for (int i = 0; i < r.FieldCount; i++) sb.Append(r.IsDBNull(i) ? "-" : r.GetValue(i).ToString()).Append('|');
                    }
                    catch (SqlException) { sb.Append("x|"); }
                    sb.Append('#');
                }
            }
            return sb.ToString();
        }
    }
}
