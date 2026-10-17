using CIMS.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CIMS.Helpers
{
    // 🧲 แถว Coil ใต้สินค้า (กด PD CODE / PRODUCT CODE) - ใช้ร่วมกันหน้า Store (Max-Min) และ Inventory Registration ให้หน้าตาเหมือนกัน
    //   เต็มความกว้างตาราง: หัวคอลัมน์ (พื้นม่วง) -> แถบ Coil แม่ (จำนวน + KG + TON) -> Coil ลูกแถวละ 1 ม้วน
    //   คอลัมน์ตามที่คลังตั้งไว้ (ADD / EDIT STOCK -> COIL SUB ROW COLUMNS): COILNO / MOTHER / WEIGHT / COIL / TON / RECEIVED
    //   เปิดแล้วแถวแรก ๆ ไหลลงทีละแถว · เมาส์ชี้แถว = พื้นม่วงอ่อน
    public static class CoilRowsView
    {
        private static readonly Dictionary<string, double> Widths = new Dictionary<string, double>
        {
            ["NO"] = 0.45, ["COILNO"] = 1.4, ["MOTHER"] = 1.3, ["WEIGHT"] = 1, ["COIL"] = 0.8, ["TON"] = 1, ["RECEIVED"] = 1.3
        };
        private static readonly Brush Line = Frozen(Color.FromRgb(0xEE, 0xEA, 0xF4));
        private static readonly Brush GroupBg = Frozen(Color.FromRgb(0xF1, 0xE8, 0xF8));
        private static readonly Brush HoverBg = Frozen(Color.FromRgb(0xF6, 0xF0, 0xFD));
        private static readonly Brush PanelBg = Frozen(Color.FromRgb(0xFC, 0xFA, 0xFF));

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private static string Head(string key, StockModel s) => key switch
        {
            "NO" => "NO.", "COILNO" => "COIL NO.", "MOTHER" => "MOTHER COIL", "WEIGHT" => $"WEIGHT ({s?.UnitDisplay ?? "KG"}.)",
            "COIL" => "STOCK (COIL)", "TON" => "WEIGHT (TON)", "RECEIVED" => "RECEIVED", _ => ""
        };

        private static string Cell(string key, CoilRowModel c, StockModel s) => key switch
        {
            "NO" => c.NoText,
            "COILNO" => c.CoilNo,
            "MOTHER" => string.IsNullOrWhiteSpace(c.MotherCoil) || c.MotherCoil == "-" ? "-" : c.MotherCoil,
            "WEIGHT" => Qty.Text(c.WeightKG, s?.AllowDecimal ?? true),
            "COIL" => "1",
            "TON" => c.WeightTonText,
            "RECEIVED" => c.ReceivedDate?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? "-",
            _ => ""
        };

        private static Grid Line_(List<string> keys, Func<string, string> text, Brush fg, FontWeight weight, double size)
        {
            var g = new Grid();
            for (int i = 0; i < keys.Count; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Widths.TryGetValue(keys[i], out double w) ? w : 1, GridUnitType.Star) });
                var tb = new TextBlock
                {
                    Text = text(keys[i]), Foreground = fg, FontWeight = weight, FontSize = size, TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 4, 0)
                };
                Grid.SetColumn(tb, i);
                g.Children.Add(tb);
            }
            return g;
        }

        // รายการ Coil ดิบ (เช่น SHOW COILS โหลดทุกสินค้าทีเดียว) = ยังไม่มีแถบ Coil แม่ -> จัดกลุ่มแบบ CoilService.GetCoilRows
        private static IList<CoilRowModel> Grouped(IList<CoilRowModel> rows)
        {
            if (rows.Count == 0 || rows.Any(r => r.IsMother)) return rows;
            var list = new List<CoilRowModel>();
            foreach (var g in rows.GroupBy(c => string.IsNullOrWhiteSpace(c.MotherCoil) ? "-" : c.MotherCoil))
            {
                var items = g.ToList();
                list.Add(new CoilRowModel { IsMother = true, MotherCoil = g.Key, CoilNo = $"{items.Count} COIL", WeightKG = items.Sum(c => c.WeightKG) });
                int no = 1;
                foreach (var c in items)
                    list.Add(new CoilRowModel { CoilId = c.CoilId, PartId = c.PartId, No = no++, CoilNo = c.CoilNo, MotherCoil = c.MotherCoil,
                                                WeightKG = c.WeightKG, LabelDate = c.LabelDate, ReceivedDate = c.ReceivedDate });
            }
            return list;
        }

        // rows = CoilService.GetCoilRows (แถบ Coil แม่ IsMother + Coil ลูก) / stock = คลังของแถว (คอลัมน์ + หน่วย + ทศนิยม)
        public static void Build(Panel host, IList<CoilRowModel> rows, StockModel stock, Brush accent, bool animate = true)
        {
            host.Children.Clear();
            rows = Grouped(rows ?? new List<CoilRowModel>());
            var box = new StackPanel();
            var frame = new Border
            {
                Margin = new Thickness(0, 0, 0, 10), BorderBrush = accent, BorderThickness = new Thickness(4, 0, 0, 0),
                Background = PanelBg, Child = box, SnapsToDevicePixels = true
            };
            host.Children.Add(frame);

            if (!rows.Any(r => !r.IsMother))
            {
                box.Children.Add(new TextBlock
                {
                    Text = "ยังไม่มี Coil ในคลังของสินค้านี้ (สแกนรับเข้า หรือ Import COIL LIST)", Margin = new Thickness(0, 14, 0, 14),
                    FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center
                });
                if (animate) Motion.SlideIn(frame, 0, 220, false, 8);
                return;
            }

            var keys = new List<string> { "NO" };
            keys.AddRange(stock?.CoilRowOrder() ?? StockModel.CoilRowKeys.ToList());

            box.Children.Add(new Border { Background = accent, Height = 42, Child = Line_(keys, k => Head(k, stock), Brushes.White, FontWeights.Bold, 13) });
            int n = 0;
            foreach (var r in rows)
            {
                Border b;
                if (r.IsMother)
                {
                    var t = new TextBlock { FontSize = 13, FontWeight = FontWeights.ExtraBold, Foreground = accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
                    t.Inlines.Add(new System.Windows.Documents.Run("🧲 MOTHER COIL : " + (string.IsNullOrWhiteSpace(r.MotherCoil) ? "-" : r.MotherCoil)));
                    t.Inlines.Add(new System.Windows.Documents.Run($"     {r.CoilNo}  •  {Qty.Text(r.WeightKG, stock?.AllowDecimal ?? true)} {stock?.UnitDisplay ?? "KG"}  •  {r.WeightTonText} TON")
                    { FontWeight = FontWeights.SemiBold, Foreground = Brushes.DimGray });
                    b = new Border { Height = 36, Background = GroupBg, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1), Child = t };
                }
                else
                {
                    var row = r;
                    b = new Border
                    {
                        Height = 48, Background = Brushes.Transparent, BorderBrush = Line, BorderThickness = new Thickness(0, 0, 0, 1),
                        Child = Line_(keys, k => Cell(k, row, stock), Brushes.Black, FontWeights.Bold, 13)
                    };
                    b.MouseEnter += (s, a) => ((Border)s).Background = HoverBg;
                    b.MouseLeave += (s, a) => ((Border)s).Background = Brushes.Transparent;
                }
                box.Children.Add(b);
                if (animate && n++ < 14) Motion.SlideIn(b, n * 25, 240, false, 8);   // แถวแรก ๆ ไหลลงทีละแถว
            }
        }
    }
}
