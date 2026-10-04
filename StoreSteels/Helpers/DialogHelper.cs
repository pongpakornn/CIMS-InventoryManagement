using CIMS.Views;
using System;
using System.Linq;
using System.Windows;

namespace CIMS.Helpers
{
    public static class DialogHelper
    {
        // 1. สำหรับงานสำเร็จ (สีเขียว - Auto Close)
        public static void ShowSuccess(string message, string title = "SUCCESS")
        {
            OnUi(() =>
            {
                var win = new CustomDialogView(title, message, CustomDialogView.DialogType.Success);
                SetOwner(win);
                win.ShowDialog();
            });
        }

        // 2. สำหรับการยืนยัน Yes/No (สีฟ้า)
        public static bool ShowConfirm(string message, string title = "CONFIRMATION")
        {
            return OnUi(() =>
            {
                var win = new CustomDialogView(title, message, CustomDialogView.DialogType.Confirm);
                SetOwner(win);
                return win.ShowDialog() == true;
            });
        }

        // 3. สำหรับคำเตือน (สีส้มแดง - หน้าใหม่ที่เราเพิ่งทำ)
        public static void ShowWarning(string message, string title = "WARNING")
        {
            OnUi(() =>
            {
                var win = new CustomWarningView(title, message);
                SetOwner(win);
                win.ShowDialog();
            });
        }

        // 4. สำหรับข้อผิดพลาดร้ายแรง (สีแดงเข้ม - หน้าใหม่)
        public static void ShowError(string message, string title = "ERROR")
        {
            OnUi(() =>
            {
                var win = new CustomErrorView(title, message);
                SetOwner(win);
                win.ShowDialog();
            });
        }

        public static void ShowInfo(string message, string title = "PR DETAILS")
        {
            OnUi(() =>
            {
                var win = new CustomDialogView(title, message, CustomDialogView.DialogType.Info);
                SetOwner(win);
                win.ShowDialog();
            });
        }

        // 5. สำหรับกรอกจำนวน (ใช้กับ "คืนเหล็ก" ในหน้า Multi-Scanner และโอนย้ายบางส่วน) - คืนค่า null เมื่อกด Cancel
        //    maxQty: ถ้าระบุ ห้ามกรอกเกินค่านี้ (เช่น ยอดคงเหลือในคลังต้นทาง)
        public static int? ShowQuantityInput(string message, string title = "ENTER QUANTITY", int? maxQty = null)
        {
            return OnUi(() =>
            {
                var win = new QuantityInputDialog(title, message, maxQty);
                SetOwner(win);
                bool? result = win.ShowDialog();
                return result == true ? win.Quantity : (int?)null;
            });
        }

        // 6. สำหรับกรอกข้อความบังคับ (เช่น เหตุผลในการลบคลัง) - คืนค่า null เมื่อกด Cancel
        public static string ShowTextInput(string message, string title, string confirmText = "CONFIRM", bool isDanger = false)
        {
            return OnUi(() =>
            {
                var win = new TextInputDialog(title, message, confirmText, isDanger);
                SetOwner(win);
                return win.ShowDialog() == true ? win.InputText : null;
            });
        }

        // Popup ต้องเด้งตรงกลางหน้าจอเสมอ: ผูกกับหน้าต่างที่กำลังใช้งานอยู่ (โปรแกรมหลักเปิดเต็มจอ = กลางจอ)
        // ถ้าหาหน้าต่างเจ้าของไม่ได้ (เช่น ยังไม่ Login / หน้าต่างถูกปิดไปแล้ว) ให้กึ่งกลางจอแทน
        private static void SetOwner(Window win)
        {
            var app = Application.Current;
            Window owner = null;

            if (app != null)
            {
                owner = app.Windows.OfType<Window>()
                           .FirstOrDefault(w => w != win && w.IsVisible && w.IsActive && !(w is NotificationView));

                if (owner == null && app.MainWindow != null && app.MainWindow != win && app.MainWindow.IsVisible)
                    owner = app.MainWindow;

                if (owner == null)
                    owner = app.Windows.OfType<MainView>().FirstOrDefault(w => w.IsVisible);
            }

            if (owner != null)
            {
                win.Owner = owner;
                win.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        // เรียกจาก Thread ไหนก็ได้ (เช่น Task.Run / Global error handler) - เปิด Popup บน UI Thread เสมอ
        private static void OnUi(Action action)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess()) action();
            else d.Invoke(action);
        }

        private static T OnUi<T>(Func<T> func)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess()) return func();
            return d.Invoke(func);
        }
    }
}
