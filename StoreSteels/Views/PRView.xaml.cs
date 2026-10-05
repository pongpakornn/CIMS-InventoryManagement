using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using CIMS.ViewModels;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CIMS.Views
{
    public partial class PRView : Page
    {
        private readonly PRViewModel _viewModel;
        private readonly PRService _prService = new PRService();
        private CancellationTokenSource _searchCts;
        private int _currentViewingId = -1;

        #region === [ PRView ] ===

        public PRView(UserSession session)
        {
            InitializeComponent();
            RunEntryAnimation();
            _viewModel = new PRViewModel { CurrentUser = session };
            this.DataContext = _viewModel;

            ApplyPermission(session);
            SetDefaultLookupTexts();
            _ = _viewModel.LoadAllLookups();
            _ = _viewModel.LoadAllPR(); // โหลดข้อมูลเริ่มต้น

            // 🔄 รายการที่แสกนออก (สร้าง PR อัตโนมัติจาก Trigger) ขึ้นตารางเองทุก 5 วินาที ระหว่างเปิดหน้านี้อยู่
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _refreshTimer.Tick += RefreshTimer_Tick;
            Loaded += (s, e) => _refreshTimer.Start();
            Unloaded += (s, e) => _refreshTimer.Stop();
        }

        #endregion

        #region === [ Auto Refresh (Scan Out -> PR) ] ===
        private readonly DispatcherTimer _refreshTimer;
        private bool _isRefreshing;

        private async void RefreshTimer_Tick(object sender, EventArgs e)
        {
            if (_isRefreshing) return;
            _isRefreshing = true;
            try { await _viewModel.RefreshIfChanged(txtSearchPR.Text.Trim()); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"PR refresh error: {ex.Message}"); }
            finally { _isRefreshing = false; }
        }
        #endregion

        #region === [ ช่องพิมพ์เองได้ + แนะนำค่าที่เคยใช้ (Requester / Department / Remark / Target Dept) ] ===

        // ค่าเริ่มต้น: ผู้ขอ = คนที่ Login, ที่เหลือ = ค่าเดิมที่เคยตั้งไว้ (พิมพ์ทับได้)
        private void SetDefaultLookupTexts()
        {
            txtRequester.Text = _viewModel.LoginUserName;
            // Department / Remark / Target Dept เริ่มว่าง -> เลือกจากรายการที่เคยใช้ หรือพิมพ์เพิ่มใหม่ได้เลย
            txtDept.Text = "";
            txtRemark.Text = "";
            txtTargetDept.Text = "";
        }

        private async void Lookup_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!(sender is ComboBox cb) || !(cb.Tag is string field)) return;
            if (e.Key == System.Windows.Input.Key.Down || e.Key == System.Windows.Input.Key.Up ||
                e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Escape ||
                e.Key == System.Windows.Input.Key.Tab) return;

            // เปลี่ยนรายการแนะนำแล้วต้องคงข้อความ/ตำแหน่งเคอร์เซอร์ที่พิมพ์อยู่ไว้
            string text = cb.Text ?? "";
            var editor = cb.Template.FindName("PART_EditableTextBox", cb) as TextBox;
            int caret = editor?.CaretIndex ?? text.Length;

            await _viewModel.UpdateLookup(field, text);

            if (cb.Text != text) cb.Text = text;
            if (editor != null) { editor.SelectionLength = 0; editor.CaretIndex = Math.Min(caret, text.Length); }
            cb.IsDropDownOpen = cb.IsKeyboardFocusWithin && _viewModel.LookupCollection(field).Any();
        }

        // คลิกเข้าช่อง: โหลดค่าที่เคยใช้ทั้งหมดไว้ให้กดลูกศรเลือกได้
        private async void Lookup_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (!(sender is ComboBox cb) || !(cb.Tag is string field)) return;
            string text = cb.Text ?? "";
            await _viewModel.UpdateLookup(field, "");
            if (cb.Text != text) cb.Text = text;
            // ช่องยังว่าง -> เปิดรายการให้เลือกเลย
            if (text.Length == 0 && cb.IsKeyboardFocusWithin && _viewModel.LookupCollection(field).Any()) cb.IsDropDownOpen = true;
        }

        #endregion

        #region === [ Permission Control ] ===

        // 🔐 สิทธิ์ PR: ADD = SUBMIT PR / VIEW = APPROVED LIST / EDIT = PR SETTINGS
        private void ApplyPermission(UserSession user)
        {
            btnSubmitPR.Visibility = user?.CanSubmitPR == true ? Visibility.Visible : Visibility.Collapsed;
            btnExport.Visibility = user?.CanViewPR == true ? Visibility.Visible : Visibility.Collapsed;
            btnPrSettings.Visibility = user?.CanManagePrSettings == true ? Visibility.Visible : Visibility.Collapsed;
        }

        // ⚙ เลือกคลังที่แสกนออกแล้วสร้าง PR อัตโนมัติ (การ์ดคลัง ติ๊กเปิด/ปิด)
        private void PrSettings_Click(object sender, RoutedEventArgs e)
        {
            var user = _viewModel.CurrentUser;
            if (user == null || !user.CanManagePrSettings)
            {
                DialogHelper.ShowWarning("คุณไม่มีสิทธิ์ตั้งค่า PR อัตโนมัติ", "ACCESS DENIED");
                return;
            }

            var service = new StockService();
            List<StockModel> stocks;
            Dictionary<int, bool> current;
            try
            {
                stocks = service.GetStocks();
                current = service.GetPrAutoMap();
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("โหลดการตั้งค่า PR ไม่สำเร็จ\n" + ex.Message);
                return;
            }

            var result = StockPickerWindow.ShowPrSettings(stocks, current);
            if (result == null) return;

            var changed = result.Where(kv => !current.TryGetValue(kv.Key, out bool old) || old != kv.Value)
                                .ToDictionary(kv => kv.Key, kv => kv.Value);
            if (changed.Count == 0) return;

            try
            {
                service.SetPrAuto(changed, user.UserId);
                string on = string.Join(", ", stocks.Where(s => result.TryGetValue(s.StkId, out bool v) && v).Select(s => s.Code));
                LogService.WritePRLog(user.UserId, "PR_SETTINGS", $"AUTO PR stocks: {(on.Length == 0 ? "-" : on)}", "PR_SETTINGS");
                DialogHelper.ShowSuccess("บันทึกการตั้งค่า PR อัตโนมัติเรียบร้อยแล้ว\n\nคลังที่สร้าง PR เมื่อแสกนออก: " + (on.Length == 0 ? "ไม่มี" : on));
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("บันทึกการตั้งค่า PR ไม่สำเร็จ\n" + ex.Message);
            }
        }

        #endregion

        #region === [ Event Submit PR ] ===

        private async void SubmitPR_Click(object sender, RoutedEventArgs e)
        {
            string partName = txtProductName.Text.Trim();
            if (string.IsNullOrEmpty(partName) || !int.TryParse(txtQTY.Text, out int q))
            {
                DialogHelper.ShowWarning("ข้อมูลไม่ครบถ้วน");
                return;
            }

            // ตรวจสอบสินค้าใน CIMS.Parts
            bool exists = await Task.Run(() => _prService.IsProductExists(partName));
            if (!exists)
            {
                DialogHelper.ShowWarning("ไม่พบสินค้าลำดับนี้ในระบบ");
                return;
            }

            if (string.IsNullOrWhiteSpace(txtRequester.Text) || string.IsNullOrWhiteSpace(txtDept.Text) ||
                string.IsNullOrWhiteSpace(txtTargetDept.Text))
            {
                DialogHelper.ShowWarning("กรุณากรอก Requester, Department และ Target Dept ให้ครบถ้วน");
                return;
            }

            var newItem = new PRModel { PartName = partName, QTY = q };
            if (await _viewModel.SavePRToDb(newItem, txtRequester.Text, txtDept.Text, txtRemark.Text, txtTargetDept.Text))
            {
                DialogHelper.ShowSuccess("บันทึกสำเร็จ");
                ClearInputs();
            }
        }

        #endregion

        #region === [ Event Export ] ===

        // 📋 รายการที่ Approve แล้ว (ค้นหา / ช่วงวันที่ / ติ๊กเลือก Export / EDIT / DEL)
        private async void ApprovedList_Click(object sender, RoutedEventArgs e)
        {
            var w = new PRApprovedWindow(_viewModel.CurrentUser);
            w.ShowDialog();
            if (w.Changed) await _viewModel.LoadAllPR(txtSearchPR.Text);
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            var selected = _viewModel.PRHistory.Where(x => x.IsSelected).ToList();
            if (!selected.Any())
            {
                DialogHelper.ShowWarning("เลือกรายการก่อนครับ");
                return;
            }

            if (await _viewModel.ProcessExportAsync(selected))
            {
                DialogHelper.ShowSuccess("ส่งออกเรียบร้อย");
            }
        }

        #endregion

        #region === [ Event Approve ] ===
        private async void Approve_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PRModel selected)
            {
                // หน้าต่างเลือก: อนุมัติทั้งหมด / กำหนดยอด (+ เก็บยอดที่เหลือหรือไม่) แล้วยืนยันอีกครั้ง
                var w = new PRApproveWindow(selected);
                if (w.ShowDialog() == true && w.Confirmed)
                    await _viewModel.ApprovePR(selected, w.ApprovedQty, w.KeepRemainder);
            }
        }
        #endregion
        
        #region === [ Event Reject ] ===
        private async void Reject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PRModel selected)
            {
                if (DialogHelper.ShowConfirm($"ปฏิเสธรายการ {selected.PRNumber}?", "ยืนยัน"))
                {
                    await _viewModel.RejectPR(selected);
                }
            }
        }
        #endregion

        #region === [ Event View ] ===
        private void ViewAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PRModel selected)
            {
                if (_currentViewingId == selected.ID) { ClearInputs(); }
                else
                {
                    _currentViewingId = selected.ID;
                    txtProductName.Text = selected.PartName;
                    txtQTY.Text = selected.QTY.ToString();
                    btnSubmitPR.Visibility = Visibility.Collapsed;
                    dgPR.SelectedItem = selected;
                }
            }
        }
        #endregion

        #region === [ Event ClearInputs ] ===
        private void ClearInputs()
        {
            _currentViewingId = -1;
            txtProductName.Text = "";
            txtQTY.Clear();
            // คืนค่า Binding ให้ชื่อคนทำรายการและวันที่กลับมาเป็นปัจจุบัน (ของเก่าที่หายไป)
            txtPRDate.SetBinding(TextBox.TextProperty, new Binding("CurrentDateDisplay") { Mode = BindingMode.OneWay });
            SetDefaultLookupTexts();
            // เช็คสิทธิ์ปุ่มบันทึกอีกครั้ง
            if (_viewModel.CurrentUser.CanSubmitPR)
                btnSubmitPR.Visibility = Visibility.Visible;
            dgPR.SelectedItem = null;
            txtProductName.IsReadOnly = false;
            txtQTY.IsReadOnly = false;
        }
        #endregion

        #region === [ Event txtProductName KeyUp ] ===
        private async void txtProductName_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Down || e.Key == System.Windows.Input.Key.Up ||
                e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Escape ||
                e.Key == System.Windows.Input.Key.Tab) return;

            // พิมพ์แล้วระบบหาสินค้าที่ใกล้เคียงที่สุด 5 รายการ (ทุกคลัง) - คงข้อความ/ตำแหน่งเคอร์เซอร์ที่พิมพ์อยู่ไว้
            string text = txtProductName.Text ?? "";
            var editor = txtProductName.Template.FindName("PART_EditableTextBox", txtProductName) as TextBox;
            int caret = editor?.CaretIndex ?? text.Length;

            await _viewModel.UpdateSuggestions(text.Trim());

            if (txtProductName.Text != text) txtProductName.Text = text;
            if (editor != null) { editor.SelectionLength = 0; editor.CaretIndex = Math.Min(caret, text.Length); }
            txtProductName.IsDropDownOpen = text.Trim().Length > 0 && txtProductName.IsKeyboardFocusWithin && _viewModel.ProductSuggestions.Any();
        }
        #endregion

        #region === [ Event SelectAllPR ] ===
        private void SelectAllPR_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk) _viewModel.ToggleSelectAll(chk.IsChecked ?? false);
        }
        #endregion

        #region ===[ Event txtSearchPR TextChanged ] ===
        private async void txtSearchPR_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            try
            {
                await Task.Delay(400, _searchCts.Token);
                await _viewModel.LoadAllPR(txtSearchPR.Text);
            }
            catch (TaskCanceledException) { }
        }
        #endregion

        #region ===[ Event SearchPR ] ===
        private void SearchPR_Click(object sender, RoutedEventArgs e)
        {
            _ = _viewModel.LoadAllPR(txtSearchPR.Text.Trim());
        }
        #endregion

        #region ===[ Event txtSearchPR KeyDown ] ===

        // สำหรับการกด Enter ในช่องค้นหา (ถ้าใน XAML เขียน KeyDown="txtSearchPR_KeyDown")
        private void txtSearchPR_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                _ = _viewModel.LoadAllPR(txtSearchPR.Text.Trim());
            }
        }

        #endregion

        #region === [ Event Animation ] ===

        private void RunEntryAnimation()
        {
            TimeSpan duration = TimeSpan.FromSeconds(0.6);
            IEasingFunction ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Fade In
            DoubleAnimation fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = duration
            };

            // Slide Up (PageTransform ต้องมีชื่อตรงกับใน XAML)
            DoubleAnimation slideUp = new DoubleAnimation
            {
                From = 30,
                To = 0,
                Duration = duration,
                EasingFunction = ease
            };

            this.BeginAnimation(Page.OpacityProperty, fadeIn);

            // ตรวจสอบความปลอดภัยก่อนรัน Animation
            if (PageTransform != null)
            {
                PageTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
            }
        }

        #endregion

    }
}