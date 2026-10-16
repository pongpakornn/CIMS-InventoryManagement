using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CIMS.Helpers
{
    // 🔎 ช่องกรอกที่พิมพ์แล้วขึ้นค่าที่มีในระบบให้เลือก (เช่น SUPPLIER / CUSTOMER / CATEGORY หน้า Inventory Registration)
    //   ค่าที่ขึ้นเรียงตาม: ขึ้นต้นด้วยคำที่พิมพ์ก่อน แล้วค่าที่มีคำนั้นอยู่ข้างใน (ไม่สนตัวพิมพ์เล็ก/ใหญ่)
    //   ↑ ↓ เลือก / Enter หรือคลิก = ใส่ค่า / Esc = ปิด - พิมพ์ค่าใหม่ที่ไม่มีในรายการก็ได้ตามเดิม
    //   ขึ้นเฉพาะตอนผู้ใช้พิมพ์ในช่อง (ใส่ค่าจากโค้ด เช่น กด VIEW / EDIT ไม่ขึ้น)
    public sealed class SuggestBox
    {
        private const int MaxItems = 12;
        private readonly TextBox _box;
        private readonly Popup _popup;
        private readonly ListBox _list;
        private List<string> _values = new List<string>();
        private bool _picking;

        public static SuggestBox Attach(TextBox box) => new SuggestBox(box);

        private SuggestBox(TextBox box)
        {
            _box = box;
            _list = new ListBox
            {
                MaxHeight = 260,
                BorderThickness = new Thickness(0),
                Background = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Focusable = false
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            _list.PreviewMouseLeftButtonUp += (s, e) =>
            {
                var item = ItemsControl.ContainerFromElement(_list, e.OriginalSource as DependencyObject) as ListBoxItem;
                if (item != null) Pick(item.Content as string);
            };

            _popup = new Popup
            {
                PlacementTarget = box,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0xD2, 0xE9)),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(4),
                    Margin = new Thickness(0, 2, 6, 6),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.25 },
                    Child = _list
                }
            };

            box.TextChanged += (s, e) => { if (!_picking && box.IsKeyboardFocusWithin) Show(); };
            box.PreviewKeyDown += Box_PreviewKeyDown;
            box.LostKeyboardFocus += (s, e) => { if (!_list.IsMouseOver) _popup.IsOpen = false; };
        }

        // ค่าที่มีในระบบ (โหลดใหม่ได้ทุกเมื่อ เช่น หลังลงทะเบียนสินค้าใหม่)
        public void SetValues(IEnumerable<string> values) =>
            _values = (values ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim())
                                                            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();

        public void Add(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || _values.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase)) return;
            _values.Add(value.Trim());
            _values.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private void Show()
        {
            string t = (_box.Text ?? "").Trim();
            if (t.Length == 0) { _popup.IsOpen = false; return; }
            var match = _values.Where(v => v.StartsWith(t, StringComparison.OrdinalIgnoreCase))
                               .Concat(_values.Where(v => !v.StartsWith(t, StringComparison.OrdinalIgnoreCase) && v.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0))
                               .Take(MaxItems).ToList();
            // พิมพ์ตรงกับค่าที่มีอยู่แล้วพอดี (ค่าเดียว) = ไม่ต้องขึ้น
            if (match.Count == 0 || (match.Count == 1 && string.Equals(match[0], t, StringComparison.OrdinalIgnoreCase))) { _popup.IsOpen = false; return; }
            _list.ItemsSource = match;
            _list.SelectedIndex = -1;
            _popup.MinWidth = _box.ActualWidth;
            _popup.IsOpen = true;
        }

        private void Box_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_popup.IsOpen) return;
            int n = _list.Items.Count;
            switch (e.Key)
            {
                case Key.Down: _list.SelectedIndex = Math.Min(n - 1, _list.SelectedIndex + 1); _list.ScrollIntoView(_list.SelectedItem); e.Handled = true; break;
                case Key.Up: _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1); _list.ScrollIntoView(_list.SelectedItem); e.Handled = true; break;
                case Key.Enter:
                case Key.Tab:
                    if (_list.SelectedItem is string v) { Pick(v); e.Handled = e.Key == Key.Enter; }
                    else _popup.IsOpen = false;
                    break;
                case Key.Escape: _popup.IsOpen = false; e.Handled = true; break;
            }
        }

        private void Pick(string value)
        {
            if (value == null) return;
            _picking = true;
            _box.Text = value;
            _box.CaretIndex = value.Length;
            _picking = false;
            _popup.IsOpen = false;
            _box.Focus();
        }
    }
}
