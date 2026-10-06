using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using CIMS.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CIMS.Views
{
    public partial class PackingCardView : Page
    {
        private readonly PackingCardViewModel _viewModel;
        private readonly PackingCardPrintService _printService = new PackingCardPrintService();
        private readonly PackingPrintLogService _printLogService = new PackingPrintLogService();

        public PackingCardView(UserSession session)
        {
            InitializeComponent();

            _viewModel = new PackingCardViewModel();
            _viewModel.CurrentUser = session;
            _viewModel.PropertyChanged += (s, e) => UpdateItemCountText();

            this.DataContext = _viewModel;

            _viewModel.GroupedItems.CollectionChanged += (s, e) => { UpdateItemCountText(); RefitColumns(); };

            RunEntryAnimation();
        }

        private void UpdateItemCountText()
        {
            int total = _viewModel.VisibleItems.Count;
            int selected = _viewModel.VisibleItems.Count(x => x.IsSelected);
            txtItemCount.Text = $"SHOWING {total} ITEMS  |  SELECTED {selected} ITEMS";
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ApplyFilter();
            UpdateItemCountText();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ClearSearch();
            UpdateItemCountText();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            if (checkBox == null) return;

            bool isChecked = checkBox.IsChecked ?? false;
            foreach (var item in _viewModel.VisibleItems)
            {
                item.IsSelected = isChecked;
            }

            dgPackingCards.Items.Refresh();
            UpdateItemCountText();
        }

        private void RowCheckBox_Click(object sender, RoutedEventArgs e)
        {
            // อัปเดตพรีวิวการ์ดแบบ real-time ตามแถวล่าสุดที่ถูกเลือก
            if (sender is CheckBox chk && chk.DataContext is PackingCardModel item)
            {
                _viewModel.PreviewItem = item;
            }
            UpdateItemCountText();
        }

        private void dgPackingCards_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // คลิกแถวไหน (ไม่ต้องติ๊ก checkbox) ให้ไปพรีวิวการ์ดฝั่งขวาทันที
            if (dgPackingCards.SelectedItem is PackingCardModel item)
            {
                _viewModel.PreviewItem = item;
            }
        }

        // 📐 ให้คอลัมน์เต็มความกว้างตารางเสมอ และยืด/หดตามขนาดหน้าจอ (เหมือนหน้า Store Max-Min)
        // DataGrid วัดขนาดครั้งแรกตอนที่ยังไม่รู้ความกว้างจริง แล้วตอนความกว้าง Viewport ข้างในเปลี่ยน มันจะหักส่วนต่าง
        // ออกจากคอลัมน์ซ้ำ จนทุกคอลัมน์โดนบีบเหลือ MinWidth ค้างไว้ (ต้องลากกางเองทุกครั้ง) - เก็บความกว้างที่กำหนดใน
        // XAML ไว้ แล้วตั้งค่ากลับใหม่ตอนตารางโหลด / ข้อมูลเปลี่ยน / ความกว้าง Viewport เปลี่ยน (หลังจาก DataGrid ปรับเสร็จแล้ว)
        private List<DataGridLength> _declaredColumnWidths;
        private bool _refitPending;

        private void dgPackingCards_Loaded(object sender, RoutedEventArgs e)
        {
            if (_declaredColumnWidths == null)
                _declaredColumnWidths = dgPackingCards.Columns.Select(c => c.Width).ToList();
            RefitColumns();
        }

        private void RefitColumns()
        {
            if (_declaredColumnWidths == null || _refitPending) return;
            _refitPending = true;

            // รวบหลายๆ ครั้งที่เกิดติดกัน (เช่น เพิ่มแถวทีละรายการ) ให้เหลือทำครั้งเดียวหลัง Layout เสร็จ
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _refitPending = false;
                for (int i = 0; i < dgPackingCards.Columns.Count && i < _declaredColumnWidths.Count; i++)
                {
                    var col = dgPackingCards.Columns[i];
                    // ต้องเปลี่ยนค่าก่อนแล้วค่อยใส่ค่าเดิมกลับ DataGrid ถึงจะคำนวณ Star ใหม่จากความกว้างปัจจุบัน
                    col.Width = new DataGridLength(0);
                    col.Width = _declaredColumnWidths[i];
                }
            }));
        }

        private void dgPackingCards_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // ความกว้างพื้นที่แสดงผลเปลี่ยน (ย่อ/ขยายหน้าจอ, Scrollbar แนวตั้งโผล่/หาย) -> จัดคอลัมน์ให้เต็มใหม่
            if (e.ViewportWidthChange != 0) RefitColumns();

            // Infinite scroll: โหลดเพิ่มทีละ 10 แถวเมื่อเลื่อนใกล้สุดล่าง กันข้อมูลเยอะแล้วเครื่องค้าง
            if (!_viewModel.HasMoreToLoad) return;

            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 40)
            {
                _viewModel.LoadMore();
            }
        }

        private void PrintSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = _viewModel.GetSelectedItems();
            if (selected.Count == 0)
            {
                DialogHelper.ShowWarning("กรุณาเลือกรายการที่ต้องการพิมพ์อย่างน้อย 1 รายการครับ");
                return;
            }

            RunPrintFlow(selected);
        }

        private void RunPrintFlow(List<PackingCardModel> items)
        {
            // 🖨 เลือกเครื่องพิมพ์ก่อน: QL-800 = ฉลากทีละดวงแบบเดิม / เครื่องอื่น (Fuji ApeosPort ฯลฯ) = A4 หลายใบต่อแผ่น
            var choice = PrinterChoiceWindow.Choose(items);
            if (choice == null) return;

            var progressView = new PrintProgressView();
            if (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
            {
                progressView.Owner = Application.Current.MainWindow;
            }
            progressView.UpdateProgress(0, items.Count);
            progressView.Show();

            try
            {
                var printedItems = _printService.PrintCards(items, choice.SelectedQueue, choice.IsA4, choice.Cols, choice.Rows, (current, total) =>
                {
                    progressView.UpdateProgress(current, total);
                    // ปั๊มข้อความ UI ให้หลอดโปรเกรสขยับจริงระหว่างพิมพ์ทีละใบ (loop นี้ทำงานบน UI thread)
                    Application.Current.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                });

                string userId = _viewModel.CurrentUser?.UserId ?? "Unknown";
                string logError = null;
                foreach (var item in printedItems)
                {
                    // บันทึกประวัติการพิมพ์ลง CIMS.PickListPrintLogs (ฐานของเราเอง) - รอบถัดไป ERP query
                    // จะไม่ดึงรายการนี้กลับมาอีก และตัดออกจากลิสต์ที่แสดงอยู่ทันทีด้านล่าง
                    // พิมพ์ออกไปแล้ว -> บันทึกไม่สำเร็จต้องแจ้งให้รู้ (ไม่ใช่ "พิมพ์ไม่สำเร็จ") เพราะรายการจะกลับมาให้พิมพ์ซ้ำ
                    try { _printLogService.LogPrinted(item, userId); }
                    catch (Exception ex) { logError ??= ex.Message; }
                    LogService.WriteLog(userId, "PRINT_PACKING_CARD", $"Printed Packing Card | Ticket: {item.TicketNo} | Lot: {item.LotNo} | Printer: {choice.SelectedQueue.FullName}{(choice.IsA4 ? $" (A4 {choice.Cols}x{choice.Rows})" : "")}", item.MaterialCode);
                }

                if (printedItems.Count > 0)
                {
                    _viewModel.RemoveItems(printedItems);
                    UpdateItemCountText();
                    if (logError == null) DialogHelper.ShowSuccess($"พิมพ์ Packing Card สำเร็จ {printedItems.Count} ใบ");
                    else DialogHelper.ShowWarning($"พิมพ์ Packing Card แล้ว {printedItems.Count} ใบ แต่บันทึกประวัติการพิมพ์ไม่สำเร็จ\nรายการนี้อาจกลับมาแสดงให้พิมพ์อีกครั้ง\n\n{logError}");
                }
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("พิมพ์ไม่สำเร็จ: " + ex.Message);
            }
            finally
            {
                progressView.Finish();
            }
        }

        private void RunEntryAnimation()
        {
            if (PageTransform != null)
            {
                DoubleAnimation anim = new DoubleAnimation
                {
                    To = 0,
                    Duration = TimeSpan.FromSeconds(0.4),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                PageTransform.BeginAnimation(TranslateTransform.YProperty, anim);
            }
        }
    }
}
