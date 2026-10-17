using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace CIMS.Helpers
{
    // ✨ ลูกเล่นการเคลื่อนไหวกลางของระบบ (ใช้ซ้ำได้ทุกหน้า)
    //   PopIn    = เปิดหน้าต่าง / แผง: จางเข้า + ขยายจาก 96% + เลื่อนขึ้นเล็กน้อย
    //   SlideIn  = เปลี่ยนหน้า / เปิดแถวย่อย: จางเข้า + เลื่อนจากล่าง (หรือขวา) - หน่วงเวลาได้ ทำเป็นลำดับ (stagger)
    //   HoverLift (XAML: helpers:Motion.HoverLift="True") = เมาส์ชี้การ์ด: ยกขึ้น 3px + เงาเข้มขึ้น / ออก = กลับที่เดิม
    //   ไม่แตะ LayoutTransform (ใช้ย่อหน้าต่างให้พอดีจอ) - ใช้ RenderTransform อย่างเดียว
    public static class Motion
    {
        private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        private static readonly IEasingFunction Back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };

        private static TransformGroup Prepare(UIElement el, out ScaleTransform scale, out TranslateTransform move)
        {
            if (el.RenderTransform is TransformGroup g && g.Children.Count == 2 && g.Children[0] is ScaleTransform s0 && g.Children[1] is TranslateTransform t0)
            {
                scale = s0; move = t0; return g;
            }
            scale = new ScaleTransform(1, 1); move = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(scale); group.Children.Add(move);
            el.RenderTransform = group;
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            return group;
        }

        // ตัวที่มี RenderTransform ของตัวเองอยู่แล้ว (มีแอนิเมชันเดิม) -> ไม่ทับ
        private static bool HasOwnTransform(UIElement el) =>
            el.RenderTransform != null && el.RenderTransform != Transform.Identity && !(el.RenderTransform is TransformGroup g && g.Children.Count == 2);

        public static void PopIn(UIElement el, double ms = 260)
        {
            if (el == null || HasOwnTransform(el)) return;
            Prepare(el, out var scale, out var move);
            var d = TimeSpan.FromMilliseconds(ms);
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, d) { EasingFunction = Ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, d) { EasingFunction = Back });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, d) { EasingFunction = Back });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, d) { EasingFunction = Ease });
        }

        public static void SlideIn(UIElement el, double delayMs = 0, double ms = 280, bool fromRight = false, double distance = 16)
        {
            if (el == null || HasOwnTransform(el)) return;
            Prepare(el, out _, out var move);
            var begin = TimeSpan.FromMilliseconds(delayMs);
            var d = TimeSpan.FromMilliseconds(ms);
            el.Opacity = 0;
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, d) { BeginTime = begin, EasingFunction = Ease });
            var dp = fromRight ? TranslateTransform.XProperty : TranslateTransform.YProperty;
            move.BeginAnimation(dp, new DoubleAnimation(distance, 0, d) { BeginTime = begin, EasingFunction = Ease });
        }

        // รายการในลิสต์ / การ์ด ไหลเข้ามาทีละชิ้น (ชิ้นแรก ๆ เท่านั้น - ที่เหลือแสดงทันที)
        public static void Stagger(System.Windows.Controls.ItemsControl ic, double stepMs = 35, int max = 20)
        {
            if (ic == null) return;
            for (int i = 0; i < Math.Min(ic.Items.Count, max); i++)
                if (ic.ItemContainerGenerator.ContainerFromIndex(i) is UIElement c) SlideIn(c, i * stepMs, 260, false, 12);
        }

        #region === [ ทั้งระบบ: ปุ่ม / การ์ดในรายการ / ขอบหน้าต่างโค้ง (App.OnStartup เรียก InstallGlobal ครั้งเดียว) ] ===
        //   ปุ่ม (Button): เมาส์ชี้ = ขยาย 4% · กด = ยุบ 4% - ปุ่มที่มีแอนิเมชันของตัวเองอยู่แล้ว (CyberButton / AnimatedButton ฯลฯ) ไม่ยุ่ง
        //   การ์ดใน ItemsControl / ListBox (ไม่ใช่ ComboBox / DataGrid): เมาส์ชี้ = ยกขึ้น + เงา · รายการมี Padding ให้การ์ดที่ยกขึ้นไม่ติดขอบ
        //   ไม่อยากให้รายการไหนยก = helpers:Motion.NoLift="True" ที่ ItemsControl นั้น (เช่น แถวแบบตาราง)
        public static readonly DependencyProperty NoLiftProperty =
            DependencyProperty.RegisterAttached("NoLift", typeof(bool), typeof(Motion), new PropertyMetadata(false));
        public static bool GetNoLift(DependencyObject d) => (bool)d.GetValue(NoLiftProperty);
        public static void SetNoLift(DependencyObject d, bool v) => d.SetValue(NoLiftProperty, v);

        private static bool _installed;
        public static void InstallGlobal()
        {
            if (_installed) return;
            _installed = true;
            EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Button), UIElement.MouseEnterEvent, new System.Windows.Input.MouseEventHandler((s, e) => ButtonScale(s as System.Windows.Controls.Button, 1.04)));
            EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Button), UIElement.MouseLeaveEvent, new System.Windows.Input.MouseEventHandler((s, e) => ButtonScale(s as System.Windows.Controls.Button, 1)));
            EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Button), UIElement.PreviewMouseLeftButtonDownEvent, new System.Windows.Input.MouseButtonEventHandler((s, e) => ButtonScale(s as System.Windows.Controls.Button, 0.96)), true);
            EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Button), UIElement.PreviewMouseLeftButtonUpEvent, new System.Windows.Input.MouseButtonEventHandler((s, e) => { var b = s as System.Windows.Controls.Button; ButtonScale(b, b != null && b.IsMouseOver ? 1.04 : 1); }), true);

            // ตัวเลือกแบบชิป / การ์ด (CheckBox / RadioButton ที่มี Style ของตัวเอง เช่น StkChip, LayoutOption, QrOption) = ขยายเล็กน้อยตอนชี้
            foreach (var t in new[] { typeof(System.Windows.Controls.CheckBox), typeof(System.Windows.Controls.RadioButton) })
            {
                EventManager.RegisterClassHandler(t, UIElement.MouseEnterEvent, new System.Windows.Input.MouseEventHandler((s, e) => { if (IsOption(s as FrameworkElement)) ElementScale((FrameworkElement)s, 1.04); }));
                EventManager.RegisterClassHandler(t, UIElement.MouseLeaveEvent, new System.Windows.Input.MouseEventHandler((s, e) => { if (IsOption(s as FrameworkElement)) ElementScale((FrameworkElement)s, 1); }));
            }

            foreach (var t in new[] { typeof(System.Windows.Controls.ContentPresenter), typeof(System.Windows.Controls.ListBoxItem) })
            {
                EventManager.RegisterClassHandler(t, UIElement.MouseEnterEvent, new System.Windows.Input.MouseEventHandler((s, e) => { if (IsCard(s as FrameworkElement)) Lift((FrameworkElement)s, true); }));
                EventManager.RegisterClassHandler(t, UIElement.MouseLeaveEvent, new System.Windows.Input.MouseEventHandler((s, e) => { if (IsCard(s as FrameworkElement)) Lift((FrameworkElement)s, false); }));
            }
            // รายการการ์ด: เว้นขอบด้านใน 6px ให้การ์ดที่ยกขึ้น + เงา ไม่ถูกตัดที่ขอบ
            EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ItemsControl), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) =>
            {
                if (s is System.Windows.Controls.ItemsControl ic && IsCardList(ic) && ic.Padding == default(Thickness)) ic.Padding = new Thickness(6);
            }));
        }

        private static readonly IEasingFunction Soft = new QuadraticEase { EasingMode = EasingMode.EaseOut };

        // ตัวกว้างขยายน้อยลง (ขอบโตไม่เกิน ~10px) - ปุ่มเล็กยังได้ 4% เต็ม
        private static double Fit(FrameworkElement el, double to)
        {
            if (to == 1 || el.ActualWidth <= 0) return to;
            double max = 10.0 / el.ActualWidth;
            return to > 1 ? 1 + Math.Min(to - 1, max) : 1 - Math.Min(1 - to, max);
        }

        private static bool IsOption(FrameworkElement el)
        {
            if (el == null || el.Style == null || GetNoLift(el) || !el.IsEnabled) return false;
            for (DependencyObject p = el; p != null; p = System.Windows.Media.VisualTreeHelper.GetParent(p))
                if (p is System.Windows.Controls.DataGrid) return false;   // ช่องติ๊กในตาราง = ไม่ขยาย
            return true;
        }

        private static void ElementScale(FrameworkElement el, double to)
        {
            if (HasOwnTransform(el)) return;
            Prepare(el, out var scale, out _);
            to = Fit(el, to);
            var d = TimeSpan.FromMilliseconds(140);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, d) { EasingFunction = Soft });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, d) { EasingFunction = Soft });
        }

        private static void ButtonScale(System.Windows.Controls.Button b, double to)
        {
            if (b == null || (!b.IsEnabled && to > 1) || GetNoLift(b)) return;
            if (HasOwnTransform(b)) return;
            // ปุ่มที่ Template ทำแอนิเมชันเอง (ตัวแรกใน Template มี RenderTransform) = ไม่ซ้อน
            if (System.Windows.Media.VisualTreeHelper.GetChildrenCount(b) > 0 && System.Windows.Media.VisualTreeHelper.GetChild(b, 0) is UIElement root &&
                root.RenderTransform != null && root.RenderTransform != Transform.Identity) return;
            Prepare(b, out var scale, out _);
            to = Fit(b, to);
            var d = TimeSpan.FromMilliseconds(to < 1 ? 70 : 140);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, d) { EasingFunction = Soft });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, d) { EasingFunction = Soft });
        }

        // รายการที่เป็นการ์ด: ItemsControl / ListBox ธรรมดา (ไม่ใช่ ComboBox / DataGrid / Menu ฯลฯ) ที่ไม่ได้ปิดไว้ และไม่ได้อยู่ในการ์ดอีกที
        private static bool IsCardList(System.Windows.Controls.ItemsControl ic)
        {
            if (ic == null || GetNoLift(ic)) return false;
            var t = ic.GetType();
            if (t != typeof(System.Windows.Controls.ItemsControl) && t != typeof(System.Windows.Controls.ListBox)) return false;
            DependencyObject p = System.Windows.Media.VisualTreeHelper.GetParent(ic);
            while (p != null && !(p is Window) && !(p is System.Windows.Controls.Page))
            {
                if (p is System.Windows.Controls.ListBoxItem || p is System.Windows.Controls.DataGridRow) return false;
                if (p is System.Windows.Controls.ContentPresenter cp && System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(cp) != null) return false;
                p = System.Windows.Media.VisualTreeHelper.GetParent(p);
            }
            return true;
        }

        private static bool IsCard(FrameworkElement el)
        {
            if (el == null || el is System.Windows.Controls.ComboBoxItem) return false;
            var owner = System.Windows.Controls.ItemsControl.ItemsControlFromItemContainer(el);
            if (owner == null || !IsCardList(owner)) return false;
            // การ์ดที่เป็นปุ่ม (เช่นตัวเลือกใน TEMPLATE) = ให้ปุ่มขยายเองแทน
            if (System.Windows.Media.VisualTreeHelper.GetChildrenCount(el) > 0 && System.Windows.Media.VisualTreeHelper.GetChild(el, 0) is System.Windows.Controls.Primitives.ButtonBase) return false;
            // การ์ดที่มีแอนิเมชันของตัวเอง (ตัวแรกข้างในมี RenderTransform) = ไม่ซ้อน
            if (System.Windows.Media.VisualTreeHelper.GetChildrenCount(el) > 0 && System.Windows.Media.VisualTreeHelper.GetChild(el, 0) is UIElement c &&
                c.RenderTransform != null && c.RenderTransform != Transform.Identity && !(c.RenderTransform is TransformGroup)) return false;
            return true;
        }

        // หน้าต่างขอบโค้ง (Border มุมโค้งเป็นตัวนอกสุด): ตัดเนื้อหาข้างในให้โค้งตามขอบ ไม่มีมุมเหลี่ยมโผล่
        public static void RoundClip(Window w)
        {
            if (!(w.Content is System.Windows.Controls.Border b) || b.CornerRadius.TopLeft <= 0 || !(b.Child is FrameworkElement ch)) return;
            double r = Math.Max(0, b.CornerRadius.TopLeft - Math.Max(b.BorderThickness.Left, b.BorderThickness.Top));
            void Apply() => ch.Clip = ch.ActualWidth > 0 ? new RectangleGeometry(new Rect(0, 0, ch.ActualWidth, ch.ActualHeight), r, r) : null;
            ch.SizeChanged += (s, e) => Apply();
            Apply();
        }
        #endregion

        #region === [ HoverLift ] ===
        public static readonly DependencyProperty HoverLiftProperty =
            DependencyProperty.RegisterAttached("HoverLift", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnHoverLiftChanged));
        public static bool GetHoverLift(DependencyObject d) => (bool)d.GetValue(HoverLiftProperty);
        public static void SetHoverLift(DependencyObject d, bool v) => d.SetValue(HoverLiftProperty, v);

        private static void OnHoverLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is FrameworkElement el)) return;
            el.MouseEnter -= Lift_Enter; el.MouseLeave -= Lift_Leave;
            if ((bool)e.NewValue) { el.MouseEnter += Lift_Enter; el.MouseLeave += Lift_Leave; }
        }

        private static void Lift_Enter(object sender, System.Windows.Input.MouseEventArgs e) => Lift((FrameworkElement)sender, true);
        private static void Lift_Leave(object sender, System.Windows.Input.MouseEventArgs e) => Lift((FrameworkElement)sender, false);

        private static void Lift(FrameworkElement el, bool up)
        {
            if (HasOwnTransform(el)) return;
            Prepare(el, out var scale, out var move);
            var d = TimeSpan.FromMilliseconds(170);
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(up ? -3 : 0, d) { EasingFunction = Ease });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(up ? 1.012 : 1, d) { EasingFunction = Ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(up ? 1.012 : 1, d) { EasingFunction = Ease });
            if (el.Effect != null && !(bool)el.GetValue(OwnShadowProperty)) return;   // มีเงา / เอฟเฟกต์ของตัวเองอยู่แล้ว = ยกอย่างเดียว ไม่แตะเงา
            if (!(el.Effect is DropShadowEffect sh))
            {
                if (!up) return;   // ไม่ได้ยกอยู่ = ไม่ต้องทำเงา
                sh = new DropShadowEffect { Color = Color.FromRgb(0x4A, 0x14, 0x8C), BlurRadius = 6, ShadowDepth = 1, Opacity = 0, Direction = 270 };
                el.Effect = sh;
                el.SetValue(OwnShadowProperty, true);
            }
            var fade = new DoubleAnimation(up ? 0.22 : 0, d);
            // ⚡ ลื่น: เมาส์ออกแล้วเงาจางหมด = เอาเงาที่เราใส่ออก (การ์ดเยอะ ๆ ไม่ค้างเงาไว้ทุกใบ)
            if (!up && (bool)el.GetValue(OwnShadowProperty))
                fade.Completed += (s, e) => { if (!el.IsMouseOver && el.Effect == sh) { el.Effect = null; el.SetValue(OwnShadowProperty, false); } };
            sh.BeginAnimation(DropShadowEffect.OpacityProperty, fade);
            sh.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(up ? 16 : 6, d));
            sh.BeginAnimation(DropShadowEffect.ShadowDepthProperty, new DoubleAnimation(up ? 5 : 1, d));
        }

        // เงาที่ Motion ใส่เอง (ถอดออกได้) - ไม่แตะเงาเดิมของตัวควบคุม
        private static readonly DependencyProperty OwnShadowProperty =
            DependencyProperty.RegisterAttached("OwnShadow", typeof(bool), typeof(Motion), new PropertyMetadata(false));
        #endregion
    }
}
