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
            if (!(el.Effect is DropShadowEffect sh))
            {
                sh = new DropShadowEffect { Color = Color.FromRgb(0x4A, 0x14, 0x8C), BlurRadius = 6, ShadowDepth = 1, Opacity = 0, Direction = 270 };
                el.Effect = sh;
            }
            sh.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(up ? 0.22 : 0, d));
            sh.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(up ? 16 : 6, d));
            sh.BeginAnimation(DropShadowEffect.ShadowDepthProperty, new DoubleAnimation(up ? 5 : 1, d));
        }
        #endregion
    }
}
