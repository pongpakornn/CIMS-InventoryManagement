using System.Collections.Generic;
using System.Windows;
using System;

namespace CIMS.Helpers
{
    public static class NotificationManager
    {
        private static List<Window> _openNotifications = new List<Window>();
        private const double Margin = 10;

        public static void Show(string title, string message, bool isSuccess)
        {
            var toast = new CIMS.Views.NotificationView(title, message, isSuccess);

            // คำนวณตำแหน่งเริ่มต้น (ล่างสุด)
            var workingArea = SystemParameters.WorkArea;
            toast.Left = workingArea.Right - toast.Width - Margin;
            toast.Top = workingArea.Bottom - toast.Height - Margin;

            // ตรวจสอบว่ามีตัวเก่าอยู่ไหม ถ้ามีให้ดันตัวใหม่ขึ้นไปข้างบน
            double totalOffset = 0;
            foreach (var openToast in _openNotifications)
            {
                totalOffset += openToast.ActualHeight > 0 ? openToast.ActualHeight : 100;
            }
            toast.Top -= totalOffset;

            _openNotifications.Add(toast);

            toast.Closed += (s, e) => {
                _openNotifications.Remove(toast);
                RearrangeNotifications(); // เมื่อตัวหนึ่งปิด ให้จัดแถวใหม่
            };

            toast.Show();
        }

        private static void RearrangeNotifications()
        {
            var workingArea = SystemParameters.WorkArea;
            double currentOffset = 0;

            // ไล่จากตัวที่เหลืออยู่ จัดตำแหน่งใหม่จากล่างขึ้นบน
            foreach (var toast in _openNotifications)
            {
                double targetTop = workingArea.Bottom - toast.Height - Margin - currentOffset;

                // ใช้ Animation เลื่อนลงมา (นนท์จะเห็นมันไหลลงมาแทนที่ตัวที่หายไป)
                System.Windows.Media.Animation.DoubleAnimation anim = new System.Windows.Media.Animation.DoubleAnimation
                {
                    To = targetTop,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new System.Windows.Media.Animation.QuadraticEase()
                };
                toast.BeginAnimation(Window.TopProperty, anim);

                currentOffset += toast.ActualHeight;
            }
        }
    }
}