using CIMS.Helpers;
using CIMS.Models;
using CIMS.ViewModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace CIMS.Views
{
    public partial class ScanInView : Page
    {
        private ScanInViewModel _viewModel;
        private DispatcherTimer _fastTimer; // ช่วยตัดรอบความเร็วสูง กรณีบาร์โค้ดไหลมาไม่ครบ
        private DateTime _lastScanTime = DateTime.MinValue; // ป้องกันการยิงเบิ้ลซ้ำซ้อน

        // ⚙️ ระบบคิวประสิทธิภาพสูง รองรับการยิงรัวสับๆ แบบไม่หน่วง UI
        private readonly ConcurrentQueue<string> _scanQueue = new ConcurrentQueue<string>();
        private bool _isProcessingQueue = false;
        private readonly object _queueLock = new object();

        public ScanInView(UserSession user)
        {
            InitializeComponent();
            RunEntryAnimation();

            _viewModel = new ScanInViewModel { CurrentUser = user };
            _viewModel.ApplyPermissions();
            this.DataContext = _viewModel;

            // สลับโหมด (สแกนออก / คืนเหล็ก / Test) แล้วโฟกัสกลับช่องสแกนทันที ยิงต่อได้เลยไม่ต้องคลิก
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ScanInViewModel.IsScanOutMode) ||
                    e.PropertyName == nameof(ScanInViewModel.IsReturnMode) ||
                    e.PropertyName == nameof(ScanInViewModel.IsTestMode) ||
                    e.PropertyName == nameof(ScanInViewModel.SelectedStock))
                {
                    FocusScanBox();
                }
            };

            // ช่องสแกนใช้แป้นภาษาอังกฤษเสมอ (สแกนเนอร์ส่งเป็นปุ่มกด แป้นไทยจะได้ตัวอักษรผิด) + ปิด IME
            System.Windows.Input.InputLanguageManager.SetInputLanguage(txtBarcodeInput, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
            System.Windows.Input.InputLanguageManager.SetRestoreInputLanguage(txtBarcodeInput, true);
            System.Windows.Input.InputMethod.SetIsInputMethodEnabled(txtBarcodeInput, false);
            txtBarcodeInput.PreviewTextInput += txtBarcodeInput_PreviewTextInput;
            txtBarcodeInput.LostKeyboardFocus += ScanBox_LostKeyboardFocus;
            this.PreviewTextInput += Page_PreviewTextInput;

            this.Loaded += ScanInView_Loaded;
            this.Unloaded += ScanInView_Unloaded;

            // ตั้งค่าตัวจับเวลาระดับด่วนพิเศษ (15 มิลลิวินาที) เผื่อกรณีไม่มีคำว่า Piece ส่งมา หรือหลุดจังหวะ
            _fastTimer = new DispatcherTimer(DispatcherPriority.Send);
            _fastTimer.Interval = TimeSpan.FromMilliseconds(300);
            _fastTimer.Tick += FastTimer_Tick;
        }

        #region === [ Auto Focus ช่องสแกน ] ===
        private Window _hostWindow;

        private void ScanInView_Loaded(object sender, RoutedEventArgs e)
        {
            // สลับกลับมาที่หน้าต่างโปรแกรม (Alt+Tab / คลิกหน้าต่าง) ก็ให้โฟกัสช่องสแกนให้เอง
            _hostWindow = Window.GetWindow(this);
            if (_hostWindow != null) _hostWindow.Activated += HostWindow_Activated;
            _viewModel.StartLive();
            FocusScanBox();
        }

        private void ScanInView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_hostWindow != null) _hostWindow.Activated -= HostWindow_Activated;
            _hostWindow = null;
            _viewModel.StopLive();
        }

        private void HostWindow_Activated(object sender, EventArgs e) => FocusScanBox();

        // 🕘 ประวัติการสแกน (เลือกคลังได้ทุกคลังที่มีสิทธิ์ดู)
        private void History_Click(object sender, RoutedEventArgs e)
        {
            List<StockModel> stocks;
            try { stocks = new CIMS.Services.StockService().GetStocks().Where(s => _viewModel.CurrentUser == null || _viewModel.CurrentUser.CanViewStock(s)).ToList(); }
            catch { stocks = _viewModel.Stocks.ToList(); }
            new ScanHistoryWindow(_viewModel.CurrentUser, stocks).ShowDialog();
            FocusScanBox();
        }

        // ✔ DEDUCTED: ป้ายที่รับเข้าแล้วตัดคลังต้นทาง (STOCK-PANTA) ได้
        private async void Deducted_Click(object sender, RoutedEventArgs e)
        {
            new DeductMissWindow(_viewModel.CurrentUser, deducted: true).ShowDialog();
            await _viewModel.RefreshNotDeductedAsync();
            FocusScanBox();
        }

        // ⚠ NOT DEDUCTED: ป้ายที่รับเข้าแล้วแต่ตัด Coil แม่ในคลังต้นทางไม่ได้
        private async void NotDeducted_Click(object sender, RoutedEventArgs e)
        {
            new DeductMissWindow(_viewModel.CurrentUser).ShowDialog();
            await _viewModel.RefreshNotDeductedAsync();
            FocusScanBox();
        }

        // 🏷️ DISPLAY: ค่าที่แสดงในช่อง PRODUCT CODE ต่อคลัง -> บันทึกแล้วใช้กับตารางวันนี้ทันที
        private void ScanDisplay_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.CurrentUser?.CanEditStore != true) return;
            List<StockModel> stocks;
            try { stocks = new CIMS.Services.StockService().GetStocks().Where(s => _viewModel.CurrentUser.CanViewStock(s)).ToList(); }
            catch { stocks = _viewModel.Stocks.ToList(); }
            var w = new ScanDisplayWindow(_viewModel.CurrentUser, stocks);
            w.ShowDialog();
            if (w.Saved) _viewModel.ReloadDisplaySettings();
            FocusScanBox();
        }

        // ✏️ ADJUST SCAN (Level 1 เท่านั้น): แก้จำนวน / ลบรายการสแกน -> ปรับยอดย้อนให้ แล้วโหลดตารางวันนี้ใหม่
        private void AdjustScan_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.CurrentUser?.UserLevel != 1)
            {
                DialogHelper.ShowWarning("เฉพาะผู้ดูแลระบบ (Level 1) เท่านั้นที่แก้ไขรายการสแกนได้", "ACCESS DENIED");
                return;
            }
            List<StockModel> stocks;
            try { stocks = new CIMS.Services.StockService().GetStocks(); }
            catch { stocks = _viewModel.Stocks.ToList(); }
            var w = new ScanAdjustWindow(_viewModel.CurrentUser, stocks);
            w.ShowDialog();
            if (w.Changed) _viewModel.ReloadToday();
            FocusScanBox();
        }

        // 🏬 เลือกคลังที่สแกน (การ์ดคลัง: 1 คลัง / หลายคลังสแกนร่วมกัน / AUTO) -> กลับไปโฟกัสช่องสแกนทันที
        private void ScanStocks_Click(object sender, RoutedEventArgs e)
        {
            var w = StockPickerWindow.ShowScanStocks(_viewModel.Stocks, _viewModel.ScopeAuto, _viewModel.ScopeIds.ToList(), _viewModel.PriorityStockId);
            if (w != null)
            {
                _viewModel.ApplyScope(w.IsAuto, w.CheckedIds, w.PriorityId);
                CIMS.Services.LogService.WriteLog(_viewModel.CurrentUser?.UserId, "SCAN_SCOPE", $"Scanner stocks: {_viewModel.ScopeText}", "");
            }
            FocusScanBox();
        }

        // Focus() ตรงๆ ตอน Loaded มักไม่ติด เพราะโฟกัสยังค้างอยู่ที่ปุ่ม Sidebar ที่เพิ่งกด
        // เลื่อนไปทำหลังหน้าจอ render เสร็จ แล้วใช้ Keyboard.Focus ให้เคอร์เซอร์เข้าช่องจริง
        private void FocusScanBox()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!txtBarcodeInput.IsKeyboardFocused)
                {
                    txtBarcodeInput.Focus();
                    Keyboard.Focus(txtBarcodeInput);
                }
            }));
        }
        #endregion

        #region === [ Logic ดักจับ Text เปลี่ยนแบบออโต้ (ถอดแบบจากเวอร์ชันเก่า) ] ===
        private void txtBarcodeInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            string rawData = txtBarcodeInput.Text; // ดึงค่าแบบไม่พึ่ง Trim เพื่อรักษาสปีด
            if (string.IsNullOrEmpty(rawData)) return;

            // ⚡ แป้นพิมพ์ยังเป็นภาษาไทย (สแกนเนอร์ส่งเป็นปุ่มกด) -> แปลงตัวอักษรไทยกลับเป็นปุ่มภาษาอังกฤษ
            if (_viewModel.TurboMode && ThaiKeyboard.HasThai(rawData))
            {
                int caret = txtBarcodeInput.CaretIndex;
                txtBarcodeInput.Text = ThaiKeyboard.ToEnglish(rawData);   // TextChanged จะเข้ามาใหม่อีกรอบ
                txtBarcodeInput.CaretIndex = Math.Min(caret, txtBarcodeInput.Text.Length);
                return;
            }

            // ✨ ป้ายที่ลงท้ายด้วย "piece" (เช่น StorePC) -> ตัดรอบทันทีไม่ต้องรอ (ไม่สนตัวพิมพ์เล็ก/ใหญ่)
            if (rawData.TrimEnd().EndsWith("piece", StringComparison.OrdinalIgnoreCase))
            {
                _fastTimer.Stop(); // สั่งเบรกเวลาทันที
                CommitBarcodeAction(rawData);
                return;
            }

            // ยังไม่จบ -> รอตัวอักษรถัดไป ถ้าเงียบไปตามเวลานี้ถือว่าจบบาร์โค้ด (TURBO 120 ms / ปกติ 300 ms)
            // สแกนเนอร์ส่งตัวอักษรห่างกันไม่ถึง 10 ms จึงไม่ตัดกลางบาร์โค้ดแม้จะยาวแค่ไหน
            _lastTextChange = DateTime.Now;
            _fastTimer.Interval = TimeSpan.FromMilliseconds(_viewModel.TurboMode ? 120 : 300);
            _fastTimer.Stop();
            _fastTimer.Start();
        }

        // Tab ต่อท้ายจากสแกนเนอร์ = จบบาร์โค้ด (ไม่ให้โฟกัสหลุดออกจากช่องแล้วตัวอักษรถัดไปหาย)
        // ⚡ แป้นยังเป็นภาษาไทยอยู่ (บังคับอังกฤษไม่สำเร็จ) -> แปลงทีละตัวตอนพิมพ์เข้ามาเป็นปุ่มอังกฤษที่ถูกกด
        //    รวมปุ่มที่แป้นไทยให้สัญลักษณ์อังกฤษ เช่น ปุ่ม 2 = "/" ปุ่ม 3 = "-" (2A250-00017 จะไม่กลายเป็น /A250ข00017)
        private void txtBarcodeInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (!_viewModel.TurboMode || string.IsNullOrEmpty(e.Text)) return;
            var lang = InputLanguageManager.Current?.CurrentInputLanguage;
            if (lang == null || lang.TwoLetterISOLanguageName != "th") return;

            string fixedText = ThaiKeyboard.FromThaiLayout(e.Text);
            if (fixedText == e.Text) return;
            e.Handled = true;
            txtBarcodeInput.SelectedText = fixedText;
            txtBarcodeInput.CaretIndex = txtBarcodeInput.SelectionStart + fixedText.Length;
            txtBarcodeInput.SelectionLength = 0;
        }

        private void txtBarcodeInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Tab) return;
            e.Handled = true;
            _fastTimer.Stop();
            if (!string.IsNullOrWhiteSpace(txtBarcodeInput.Text)) CommitBarcodeAction(txtBarcodeInput.Text);
        }

        // ⌛ ป้ายยังมาไม่ครบ (เช่น ได้แค่ "0" หรือ Panta ได้ช่องไม่ครบ 11 ช่อง) -> รอต่ออีกสูงสุด 1.5 วินาทีนับจากตัวอักษรล่าสุด
        //    (ต.ค. 2026 สแกนเนอร์เว้นจังหวะกลางป้าย ทำให้ป้ายถูกตัดเป็น 2 รายการ "0" + "790774-003;...")
        private DateTime _lastTextChange = DateTime.MinValue;
        private const int MaxWaitIncompleteMs = 1500;

        private void FastTimer_Tick(object sender, EventArgs e)
        {
            _fastTimer.Stop();
            string input = txtBarcodeInput.Text;
            if (string.IsNullOrWhiteSpace(input)) return;
            if ((DateTime.Now - _lastTextChange).TotalMilliseconds < MaxWaitIncompleteMs && _viewModel.LooksIncomplete(input))
            {
                _fastTimer.Interval = TimeSpan.FromMilliseconds(150);
                _fastTimer.Start();
                return;
            }
            CommitBarcodeAction(input);
        }

        // 🎯 ล็อกช่องสแกน: อยู่หน้านี้แล้วกดปุ่ม / คลิกตาราง โฟกัสกลับช่องสแกนเอง (ช่องพิมพ์อื่นยังพิมพ์ได้ปกติ)
        //    ตัวอักษรที่พิมพ์ / ยิงเข้ามาตอนโฟกัสไม่อยู่ในช่องพิมพ์ใด ๆ -> ส่งเข้าช่องสแกนแทน (ป้ายไม่ขาดหัว)
        private static bool IsTypingControl(object o) =>
            o is System.Windows.Controls.Primitives.TextBoxBase || o is PasswordBox ||
            (o is DependencyObject d && (FindParent<ComboBox>(d)?.IsEditable == true || FindParent<DatePicker>(d) != null));

        private static T FindParent<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T)) d = (d is System.Windows.Media.Visual || d is System.Windows.Media.Media3D.Visual3D) ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }

        private void Page_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (e.OriginalSource == txtBarcodeInput || IsTypingControl(e.OriginalSource) || string.IsNullOrEmpty(e.Text)) return;
            e.Handled = true;
            txtBarcodeInput.Focus();
            Keyboard.Focus(txtBarcodeInput);
            txtBarcodeInput.CaretIndex = txtBarcodeInput.Text.Length;
            txtBarcodeInput.SelectedText = e.Text;
            txtBarcodeInput.CaretIndex = txtBarcodeInput.Text.Length;
        }

        private void ScanBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (IsTypingControl(e.NewFocus) || !IsLoaded) return;
            // หน้าต่างอื่น (Popup / Dialog) เปิดอยู่ -> ไม่แย่งโฟกัส ปิดแล้วค่อยกลับมาเอง (HostWindow_Activated)
            var w = Window.GetWindow(this);
            if (w == null || !w.IsActive) return;
            if (e.NewFocus is DependencyObject nd && Window.GetWindow(nd) != w) return;
            FocusScanBox();
        }
        #endregion

        #region === [ ฟังก์ชันสับคัตเอาท์ข้อความ เคลียร์กล่องข้อความทันทีใน 1ms ] ===
        private void CommitBarcodeAction(string rawData)
        {
            // 1. ล้างช่องรับข้อมูลทันที เพื่อให้หน้าจอว่าง สแกนเนอร์ยิงนัดถัดไปได้เลย ไม่ต้องรอโหลดภาพ/ต่อ DB
            txtBarcodeInput.Text = string.Empty;

            // 2. ดักดักป้องกันการยิงซ้ำซ้อน (Double Scan) ภายใน 1 วินาที
            if ((DateTime.Now - _lastScanTime).TotalMilliseconds < 0)
            {
                return;
            }
            _lastScanTime = DateTime.Now; // บันทึกเวลาสแกนล่าสุด

            // 3. โยนข้อมูลเข้าคิวรอประมวลผลหลังบ้านแบบเงียบๆ
            _scanQueue.Enqueue(rawData);

            // 4. สั่งให้ระบบ Worker เริ่มทำงาน (ทำงานแบบ Async ไม่กวนหน้าจอ)
            _ = ProcessQueueAsync();
        }
        #endregion

        #region === [ รองรับกรณีคุณนนท์ต่อคีย์บอร์ดพิมพ์แล้วกด Enter เองด้วย ] ===
        private void txtBarcodeInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true; // บล็อกระบบไม่ให้ส่งเสียงตึ๊ดหรือกระตุก
                _fastTimer.Stop();

                string input = txtBarcodeInput.Text;
                if (!string.IsNullOrWhiteSpace(input))
                {
                    CommitBarcodeAction(input);
                }
            }
        }
        #endregion

        #region === [ ระบบคิวประมวลผลเบื้องหลังสปีดเทอร์โบ ] ===
        private async Task ProcessQueueAsync()
        {
            lock (_queueLock)
            {
                if (_isProcessingQueue) return;
                _isProcessingQueue = true;
            }

            try
            {
                // ไล่เคลียร์บาร์โค้ดที่ค้างอยู่ในคิวแบบเรียงลำดับ ป้องกันฐานข้อมูล Lock ตัวเอง
                while (_scanQueue.TryDequeue(out string barcode))
                {
                    if (_viewModel != null)
                    {
                        try
                        {
                            string cleanInput = barcode.Trim();

                            // 🚀 จุดนี้จะไปรันโค้ดต่อ DB ดึงรูปภาพ และ Log ลงฐานข้อมูลใน ViewModel แบบ Async 
                            await _viewModel.ProcessScan(cleanInput);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Error ในคิว]: {ex.Message}");
                        }
                    }
                }
            }
            finally
            {
                lock (_queueLock)
                {
                    _isProcessingQueue = false;
                }

                // คืนโฟกัสกลับมาที่ช่องยิงบาร์โค้ด (รวมถึงหลังปิด Popup แจ้ง Error)
                FocusScanBox();
            }
        }
        #endregion

        #region === [ ClearDaily & Animation ] ===
        private async void ClearDaily_Click(object sender, RoutedEventArgs e)
        {
            if (await Task.Run(() => DialogHelper.ShowConfirm("ต้องการล้างรายการวันนี้ทั้งหมดใช่หรือไม่?", "ยืนยันการล้างข้อมูล")))
            {
                _viewModel.ScannedItems?.Clear();
                DialogHelper.ShowSuccess("ล้างรายการเรียบร้อยแล้ว");
            }
        }

        private void RunEntryAnimation()
        {
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromSeconds(0.4) };
            this.BeginAnimation(Page.OpacityProperty, fadeIn);

            if (PageTransform != null)
            {
                var slideUp = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 20,
                    To = 0,
                    Duration = TimeSpan.FromSeconds(0.4),
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                PageTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideUp);
            }
        }
        #endregion
    }
}