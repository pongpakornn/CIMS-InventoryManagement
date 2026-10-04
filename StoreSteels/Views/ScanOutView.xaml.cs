using CIMS.Helpers; // CORE : Helpers
using CIMS.Models; // CORE : Models
using CIMS.ViewModels; // CORE : ViewModels
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace CIMS.Views
{
    public partial class ScanOutView : Page
    {
        private ScanOutViewModel _viewModel;
        private DispatcherTimer _fastTimer; // ช่วยตัดรอบความเร็วสูง กรณีบาร์โค้ดไหลมาไม่ครบ
        private DateTime _lastScanTime = DateTime.MinValue; // ป้องกันการยิงเบิ้ลซ้ำซ้อน

        // ⚙️ ระบบคิวประสิทธิภาพสูง รองรับการยิงรัวสับๆ แบบไม่หน่วง UI (ถอดแบบจากหน้า ScanIn)
        private readonly ConcurrentQueue<string> _scanQueue = new ConcurrentQueue<string>();
        private bool _isProcessingQueue = false;
        private readonly object _queueLock = new object();

        #region === [ Constructor ] ===
        public ScanOutView(UserSession user)
        {
            InitializeComponent();
            RunEntryAnimation();

            _viewModel = new ScanOutViewModel { CurrentUser = user };
            this.DataContext = _viewModel;

            txtBarcodeInput.Focus();
            this.Loaded += (s, e) => txtBarcodeInput.Focus();

            // ตั้งค่าตัวจับเวลาระดับด่วนพิเศษ (19 มิลลิวินาที) เผื่อกรณีไม่มีคำว่า Piece ส่งมา หรือหลุดจังหวะ
            _fastTimer = new DispatcherTimer(DispatcherPriority.Send);
            _fastTimer.Interval = TimeSpan.FromMilliseconds(300);
            _fastTimer.Tick += FastTimer_Tick;
        }
        #endregion

        #region === [ Logic ดักจับ Text เปลี่ยนแบบออโต้ (ถอดแบบจากหน้า ScanIn) ] ===
        private void txtBarcodeInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            string rawData = txtBarcodeInput.Text; // ดึงค่าแบบไม่พึ่ง Trim เพื่อรักษาสปีด

            // ✨ ถอด Logic ทองคำจาก WinForms: ถ้าเจอนามสกุลท้ายบาร์โค้ดคำว่า "Piece" ให้ตัดรอบทันทีไม่ต้องรอใคร!
            if (!string.IsNullOrEmpty(rawData) && rawData.Contains("Piece"))
            {
                _fastTimer.Stop(); // สั่งเบรกเวลาทันที
                CommitBarcodeAction(rawData);
                return;
            }

            // ถ้ายังไม่เจอคำว่า Piece ให้จับเวลารอเผื่อสแกนเนอร์กำลังส่งตัวอักษรถัดไปมา (Reset Timer)
            _fastTimer.Stop();
            _fastTimer.Start();
        }

        private void FastTimer_Tick(object sender, EventArgs e)
        {
            _fastTimer.Stop();
            string input = txtBarcodeInput.Text;
            if (!string.IsNullOrWhiteSpace(input))
            {
                CommitBarcodeAction(input);
            }
        }
        #endregion

        #region === [ ฟังก์ชันสับคัตเอาท์ข้อความ เคลียร์กล่องข้อความทันทีใน 1ms ] ===
        private void CommitBarcodeAction(string rawData)
        {
            // 1. ล้างช่องรับข้อมูลทันที เพื่อให้หน้าจอว่าง สแกนเนอร์ยิงนัดถัดไปได้เลย ไม่ต้องรอโหลดภาพ/ต่อ DB
            txtBarcodeInput.Text = string.Empty;

            // 2. ดักป้องกันการยิงซ้ำซ้อน (Double Scan) ภายใน 1 วินาที
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

        #region === [ รองรับกรณีต่อคีย์บอร์ดพิมพ์แล้วกด Enter เอง ] ===
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

                            // 🚀 ไปรันโค้ดต่อ DB ดึงรูปภาพ และ Log ลงฐานข้อมูลใน ViewModel แบบ Async 
                            await _viewModel.ProcessScan(cleanInput);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Error ในคิว ScanOut]: {ex.Message}");
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

                // คืนโฟกัสกลับมาที่ช่องยิงบาร์โค้ดแบบนุ่มนวล
                Dispatcher.Invoke(() =>
                {
                    if (!txtBarcodeInput.IsFocused)
                    {
                        txtBarcodeInput.Focus();
                    }
                });
            }
        }
        #endregion

        #region === [ ClearDaily ] ===
        private async void ClearDaily_Click(object sender, RoutedEventArgs e)
        {
            if (await Task.Run(() => DialogHelper.ShowConfirm("ต้องการล้างรายการวันนี้ทั้งหมดใช่หรือไม่?", "ยืนยันการล้างข้อมูล")))
            {
                _viewModel.ScannedItems?.Clear();
                DialogHelper.ShowSuccess("ล้างรายการเรียบร้อยแล้ว");
            }
        }
        #endregion

        #region === [ RemoveItem ] ===
        private async void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            // ดักจับไอเทมจากตารางซ้าย dgTodayOut
            if (dgTodayOut != null && dgTodayOut.SelectedItems.Count > 0)
            {
                if (await Task.Run(() => DialogHelper.ShowConfirm("ต้องการลับรายการที่เลือกใช่หรือไม่?", "ยืนยันการลบ")))
                {
                    var selectedItems = dgTodayOut.SelectedItems.Cast<ScanItemModel>().ToList();
                    _viewModel.RemoveItems(selectedItems);
                    DialogHelper.ShowSuccess("ลบรายการที่เลือกสำเร็จ");
                }
            }
            else
            {
                DialogHelper.ShowWarning("กรุณาเลือกรายการที่ต้องการลบในตารางสินค้าขาออก (DAILY OUTGOING) ก่อนครับ");
            }
        }
        #endregion

        #region === [ Animation Page ] ===
        private void RunEntryAnimation()
        {
            DoubleAnimation fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromSeconds(0.4) };
            this.BeginAnimation(Page.OpacityProperty, fadeIn);

            if (PageTransform != null)
            {
                DoubleAnimation slideUp = new DoubleAnimation
                {
                    From = 20,
                    To = 0,
                    Duration = TimeSpan.FromSeconds(0.4),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                PageTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
            }
        }
        #endregion
    }
}