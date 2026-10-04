using CIMS.Core;
using CIMS.Helpers;
using CIMS.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace CIMS
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // ⏱️ ถ้า UI ไม่ตอบสนองเกินเวลานี้ ถือว่า "ระบบค้าง" และแจ้งผู้ใช้เมื่อกลับมาทำงานได้
        private static readonly TimeSpan HangThreshold = TimeSpan.FromSeconds(8);
        private Timer _hangWatchdog;
        private int _pingPending;
        private bool _isShowingError;

        // เปลี่ยนชื่อระบบ StoreSteels -> CIMS: ย้ายค่าที่จำไว้ในเครื่อง (%LocalAppData%\StoreSteels) มาที่ %LocalAppData%\CIMS ครั้งแรกครั้งเดียว
        static App()
        {
            try
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string oldDir = System.IO.Path.Combine(local, "StoreSteels"), newDir = System.IO.Path.Combine(local, "CIMS");
                if (System.IO.Directory.Exists(oldDir) && !System.IO.Directory.Exists(newDir))
                {
                    System.IO.Directory.CreateDirectory(newDir);
                    foreach (var f in System.IO.Directory.GetFiles(oldDir))
                        System.IO.File.Copy(f, System.IO.Path.Combine(newDir, System.IO.Path.GetFileName(f)), false);
                }
            }
            catch { /* ไม่มีค่าเดิม = ใช้ค่าเริ่มต้น */ }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 🛡️ ดัก Error ที่ไม่มีใครจับไว้ทุกช่องทาง -> แจ้งผู้ใช้เป็นภาษาไทยกลางจอ แทนที่โปรแกรมจะเด้งปิดเงียบๆ
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            StartHangWatchdog();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _hangWatchdog?.Dispose();
            base.OnExit(e);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true; // ให้โปรแกรมทำงานต่อได้
            ReportError(e.Exception, "เกิดข้อผิดพลาดที่ไม่คาดคิดในระบบ");
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            ReportError(e.Exception?.GetBaseException(), "เกิดข้อผิดพลาดในการทำงานเบื้องหลัง");
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ReportError(e.ExceptionObject as Exception,
                e.IsTerminating ? "เกิดข้อผิดพลาดร้ายแรง โปรแกรมจำเป็นต้องปิดตัวลง" : "เกิดข้อผิดพลาดที่ไม่คาดคิดในระบบ");
        }

        private void ReportError(Exception ex, string headline)
        {
            string detail = ex?.Message ?? "ไม่ทราบสาเหตุ";
            System.Diagnostics.Debug.WriteLine($"[Unhandled] {headline}: {ex}");

            try { LogService.WriteLog(CurrentUserId(), "SYSTEM_ERROR", $"{headline} | {ex}", ""); } catch { }

            // กัน Error ซ้อนกันจนเด้ง Popup รัวๆ
            if (_isShowingError) return;
            try
            {
                _isShowingError = true;
                DialogHelper.ShowError($"{headline}\n\nรายละเอียด: {detail}\n\nหากเกิดซ้ำ กรุณาแจ้งผู้ดูแลระบบ", "SYSTEM ERROR");
            }
            catch { /* ถ้าแสดง Popup ไม่ได้ (เช่น UI ปิดไปแล้ว) ก็ปล่อยผ่าน - บันทึก Log ไว้แล้ว */ }
            finally { _isShowingError = false; }
        }

        // ผู้ใช้ที่ Login อยู่ (ดึงจากหน้าต่างหลัก) สำหรับบันทึก Log
        private static string CurrentUserId()
        {
            try { foreach (Window w in Current.Windows) if (w is Views.MainView mv) return mv.CurrentUser?.UserId; } catch { }
            return null;
        }

        #region === [ Hang Watchdog : แจ้งเตือนเมื่อระบบค้าง ] ===

        // Thread เบื้องหลังส่ง "ping" เข้า UI ทุก 1 วินาที ถ้า UI ไม่ตอบกลับเกิน HangThreshold = ค้าง
        // (ตอนค้างอยู่แสดง Popup ไม่ได้เพราะ UI ไม่ว่าง) -> พอ UI กลับมาทำงาน แจ้งผู้ใช้ด้วย Toast + บันทึก Log
        private void StartHangWatchdog()
        {
            _hangWatchdog = new Timer(_ =>
            {
                var d = Dispatcher;
                if (d == null || d.HasShutdownStarted) return;
                if (Interlocked.Exchange(ref _pingPending, 1) == 1) return; // ping รอบก่อนยังไม่ได้รับคำตอบ

                long sentAt = DateTime.UtcNow.Ticks;
                d.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    var frozenFor = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - sentAt);
                    Interlocked.Exchange(ref _pingPending, 0);

                    if (frozenFor >= HangThreshold)
                    {
                        int seconds = (int)frozenFor.TotalSeconds;
                        try { LogService.WriteLog(CurrentUserId(), "SYSTEM_HANG", $"UI not responding for {seconds}s", ""); } catch { }
                        NotificationManager.Show("System", $"ระบบไม่ตอบสนองชั่วคราวประมาณ {seconds} วินาที (กลับมาทำงานปกติแล้ว)", false);
                    }
                }));
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1));
        }

        #endregion
    }
}
