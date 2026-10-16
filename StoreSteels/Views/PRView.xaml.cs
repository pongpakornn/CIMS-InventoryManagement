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
            LoadPrStocks();   // เลือกคลังที่จำไว้ แล้วโหลด PR ของคลังนั้น

            // 🔄 รายการที่แสกนออก (สร้าง PR อัตโนมัติจาก Trigger) ขึ้นตารางเองทุก 5 วินาที ระหว่างเปิดหน้านี้อยู่
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _refreshTimer.Tick += RefreshTimer_Tick;
            Loaded += (s, e) => _refreshTimer.Start();
            Unloaded += (s, e) => _refreshTimer.Stop();
        }

        #endregion

        #region === [ 🏬 คลังที่แสดง PR (เฉพาะคลังที่เปิดเก็บ PR ใน PR SETTINGS) ] ===
        private const string KeyPrStock = "PR.Stock";
        private List<StockModel> _prStocks = new List<StockModel>();
        private bool _loadingStocks;

        private void LoadPrStocks()
        {
            if (!DbSchema.HasPrStock) { cbPrStock.Visibility = Visibility.Collapsed; _ = _viewModel.LoadAllPR(); return; }   // ฐานเดิม = ทุกคลังเหมือนเดิม
            try { _prStocks = new StockService().GetPrStocks(); }
            catch (Exception ex) { _prStocks = new List<StockModel>(); System.Diagnostics.Debug.WriteLine($"PR stocks: {ex.Message}"); }

            _loadingStocks = true;
            cbPrStock.ItemsSource = _prStocks;
            string saved = UserSettings.Get(_viewModel.CurrentUser?.UserId, KeyPrStock, "");
            cbPrStock.SelectedItem = _prStocks.FirstOrDefault(s => s.StkId.ToString() == saved) ?? _prStocks.FirstOrDefault();
            _loadingStocks = false;

            txtNoPrStock.Visibility = _prStocks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            cbPrStock.Visibility = _prStocks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            ApplyStock();
        }

        private StockModel CurrentStock => cbPrStock.SelectedItem as StockModel;

        private void ApplyStock()
        {
            // ไม่มีคลังที่เปิดเก็บ PR -> ตารางว่าง (StockID -1) / มี = PR ของคลังนั้น
            _viewModel.StockId = CurrentStock?.StkId ?? -1;
            btnSubmitPR.IsEnabled = CurrentStock != null;
            _ = _viewModel.LoadAllPR(txtSearchPR.Text.Trim());
        }

        private void cbPrStock_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingStocks || _viewModel == null) return;
            if (CurrentStock != null) UserSettings.Set(_viewModel.CurrentUser?.UserId, KeyPrStock, CurrentStock.StkId.ToString());   // จำคลังตาม UserID
            ClearInputs();
            ApplyStock();
        }
        #endregion

        #region === [ 🔎 Product Code -> Product Name ] ===
        private PRPartOption _pickedPart;
        private int _codeVer, _lookupVer;

        private async void txtProductCode_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Down || e.Key == System.Windows.Input.Key.Up ||
                e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Escape ||
                e.Key == System.Windows.Input.Key.Tab) return;

            // พิมพ์แล้วหารหัสที่ตรง (คงข้อความ / ตำแหน่งเคอร์เซอร์ที่พิมพ์อยู่ไว้)
            string text = txtProductCode.Text ?? "";
            var editor = txtProductCode.Template.FindName("PART_EditableTextBox", txtProductCode) as TextBox;
            int caret = editor?.CaretIndex ?? text.Length;

            if (_pickedPart != null && !string.Equals(_pickedPart.Code, text.Trim(), StringComparison.OrdinalIgnoreCase)) SetPart(null);
            int ver = ++_codeVer;
            await _viewModel.UpdatePartOptions(text.Trim());
            // พิมพ์ต่อระหว่างรอผลค้นหา (พิมพ์เร็ว / สแกนเนอร์) -> ผลนี้เก่าแล้ว ไม่แตะข้อความ (กันตัวอักษรหาย)
            if (ver != _codeVer) return;

            if (txtProductCode.Text != text) txtProductCode.Text = text;
            if (editor != null) { editor.SelectionLength = 0; editor.CaretIndex = Math.Min(caret, text.Length); }

            // พิมพ์ตรงกับรหัสที่มีตัวเดียวพอดี -> เลือกให้เลย
            var exact = _viewModel.PartOptions.Where(o => string.Equals(o.Code, text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1) SetPart(exact[0]);
            bool open = text.Trim().Length > 0 && txtProductCode.IsKeyboardFocusWithin && _viewModel.PartOptions.Any() && _pickedPart == null;
            if (open != txtProductCode.IsDropDownOpen) txtProductCode.IsDropDownOpen = open;
            // เปิดรายการแล้ว ComboBox เลือกข้อความทั้งหมด -> ตัวต่อไปที่พิมพ์จะทับ: วางเคอร์เซอร์ไว้ท้ายข้อความ
            if (editor != null)
            {
                editor.SelectionLength = 0; editor.CaretIndex = editor.Text.Length;
                Dispatcher.BeginInvoke(new Action(() => { if (editor.SelectionLength > 0) { editor.SelectionLength = 0; editor.CaretIndex = editor.Text.Length; } }),
                                       System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void txtProductCode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (txtProductCode.SelectedItem is PRPartOption o) SetPart(o);
        }

        // ออกจากช่องแล้วพิมพ์ตรงกับรหัสในรายการ -> เลือกให้
        private void txtProductCode_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (_pickedPart != null) return;
            string t = (txtProductCode.Text ?? "").Trim();
            var m = _viewModel.PartOptions.FirstOrDefault(o => string.Equals(o.Code, t, StringComparison.OrdinalIgnoreCase));
            if (m != null) SetPart(m);
        }

        private void SetPart(PRPartOption o)
        {
            _pickedPart = o;
            txtProductName.Text = o?.Name ?? "";
            if (o != null && !string.Equals(txtProductCode.Text, o.Code, StringComparison.Ordinal)) txtProductCode.Text = o.Code;
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

            int ver = ++_lookupVer;
            await _viewModel.UpdateLookup(field, text);
            if (ver != _lookupVer) return;   // พิมพ์ต่อแล้ว -> ผลเก่า ไม่แตะข้อความ

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
            btnDeleteAllPR.Visibility = user?.UserLevel == 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        // 🗑 ลบ PR ทุกรายการที่แสดงในตาราง (ค้นหาไว้ = เฉพาะที่ค้นเจอ) - Admin Level 1 เท่านั้น ยืนยัน 2 ครั้ง
        private async void DeleteAllPR_Click(object sender, RoutedEventArgs e)
        {
            var user = _viewModel.CurrentUser;
            if (user?.UserLevel != 1) { DialogHelper.ShowWarning("ลบทั้งหมดได้เฉพาะ Admin (Level 1)", "ACCESS DENIED"); return; }
            var prs = _viewModel.PRHistory.Select(p => p.PRNumber).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
            if (prs.Count == 0) { DialogHelper.ShowWarning("ไม่มี PR ในตารางให้ลบ"); return; }
            string key = txtSearchPR.Text.Trim();
            var byStatus = _viewModel.PRHistory.GroupBy(p => p.Status).Select(g => $"{g.Key} {g.Count():N0}");
            if (!DialogHelper.ShowConfirm($"ลบ PR ทั้งหมดที่แสดงในตาราง {prs.Count:N0} รายการ" + (key.Length > 0 ? $"\n(เฉพาะที่ค้นหา \"{key}\")" : "") +
                                          $"\n{string.Join("  •  ", byStatus)}\n\nลบแล้วกู้คืนไม่ได้ ยืนยันหรือไม่?", "DELETE ALL PR")) return;
            if (!DialogHelper.ShowConfirm($"ยืนยันอีกครั้ง: ลบ PR {prs.Count:N0} รายการ", "DELETE ALL PR")) return;

            btnDeleteAllPR.IsEnabled = false;
            try
            {
                int n = await Task.Run(() => _prService.DeletePRs(prs));
                LogService.WritePRLog(user.UserId, "DELETE_ALL_PR", $"Deleted {n} PR{(key.Length > 0 ? $" (search: {key})" : "")}: {string.Join(", ", prs.Take(30))}{(prs.Count > 30 ? " ..." : "")}", "PR");
                NotificationManager.Show("Deleted", $"ลบ PR แล้ว {n:N0} รายการ", true);
                ClearInputs();
                await _viewModel.LoadAllPR(txtSearchPR.Text.Trim());
            }
            catch (Exception ex) { DialogHelper.ShowError("ลบไม่สำเร็จ\n" + ex.Message); }
            finally { btnDeleteAllPR.IsEnabled = true; }
        }

        #endregion

        #region === [ Event Submit PR ] ===

        private async void SubmitPR_Click(object sender, RoutedEventArgs e)
        {
            if (DbSchema.HasPrStock && CurrentStock == null)
            {
                DialogHelper.ShowWarning("กรุณาเลือกคลัง (STOCK) ก่อนครับ");
                return;
            }
            // สินค้าต้องเลือกจาก Product Code (Product Name ใส่ให้เอง)
            if (_pickedPart == null)
            {
                DialogHelper.ShowWarning("กรุณาเลือก Product Code ที่มีในระบบ (พิมพ์รหัสแล้วเลือกจากรายการ)");
                txtProductCode.Focus();
                return;
            }
            string partName = _pickedPart.Name;
            if (!int.TryParse(txtQTY.Text, out int q) || q <= 0)
            {
                DialogHelper.ShowWarning("กรุณาใส่ QTY เป็นตัวเลขมากกว่า 0");
                return;
            }

            if (string.IsNullOrWhiteSpace(txtRequester.Text) || string.IsNullOrWhiteSpace(txtDept.Text) ||
                string.IsNullOrWhiteSpace(txtTargetDept.Text))
            {
                DialogHelper.ShowWarning("กรุณากรอก Requester, Department และ Target Dept ให้ครบถ้วน");
                return;
            }

            var newItem = new PRModel { PartName = partName, PartCode = _pickedPart.Code, PartId = _pickedPart.PartId, QTY = q };
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
            var w = new PRApprovedWindow(_viewModel.CurrentUser, CurrentStock, _prStocks);
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
                    _pickedPart = null;
                    txtProductCode.Text = selected.PartCode == "N/A" ? "" : selected.PartCode;
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
            SetPart(null);
            txtProductCode.Text = "";
            txtProductName.Text = "";
            txtQTY.Clear();
            // คืนค่า Binding ให้ชื่อคนทำรายการและวันที่กลับมาเป็นปัจจุบัน (ของเก่าที่หายไป)
            txtPRDate.SetBinding(TextBox.TextProperty, new Binding("CurrentDateDisplay") { Mode = BindingMode.OneWay });
            SetDefaultLookupTexts();
            // เช็คสิทธิ์ปุ่มบันทึกอีกครั้ง
            if (_viewModel.CurrentUser.CanSubmitPR)
                btnSubmitPR.Visibility = Visibility.Visible;
            dgPR.SelectedItem = null;
            txtQTY.IsReadOnly = false;   // Product Name อ่านอย่างเดียวเสมอ (มาจาก Product Code)
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