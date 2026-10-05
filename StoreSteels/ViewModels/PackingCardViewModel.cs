using CIMS.Helpers;
using CIMS.Models;
using CIMS.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace CIMS.ViewModels
{
    public class PackingCardViewModel : INotifyPropertyChanged
    {
        private readonly PackingCardErpService _erpService = new PackingCardErpService();
        private const int PageSize = 10;

        private List<PackingCardModel> _filteredItems = new List<PackingCardModel>();
        private int _revealedCount = 0;

        public UserSession CurrentUser { get; set; }

        // รายการที่แสดงจริงบนตาราง (ทยอยเพิ่มทีละ 10 แถวตอน Scroll กันข้อมูลเยอะแล้วเครื่องค้าง)
        public ObservableCollection<PackingCardModel> VisibleItems { get; } = new ObservableCollection<PackingCardModel>();

        public ICollectionView GroupedItems { get; private set; }

        public bool HasMoreToLoad => _revealedCount < _filteredItems.Count;

        private PackingCardModel _previewItem;
        public PackingCardModel PreviewItem
        {
            get => _previewItem;
            set { _previewItem = value; OnPropertyChanged(); }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(); QueueFilter(); }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public PackingCardViewModel()
        {
            GroupedItems = CollectionViewSource.GetDefaultView(VisibleItems);
            GroupedItems.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PackingCardModel.GroupCode)));

            LoadData();
        }

        private List<PackingCardModel> _allItems = new List<PackingCardModel>();

        public async void LoadData()
        {
            IsLoading = true;
            try
            {
                var data = await Task.Run(() => _erpService.GetPendingPackingCards());
                _allItems = data;
                ApplyFilter();
            }
            catch (Exception ex)
            {
                DialogHelper.ShowError("โหลดข้อมูล Packing Card ไม่สำเร็จ: " + ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        // 🔎 ค้นหาได้ทุกช่อง (Bill No / Item / Group / Material / Work Order / Lot / Job / Qty / วันที่) พิมพ์แล้วกรองทันที
        //    หลายคำคั่นด้วยช่องว่าง = ต้องเจอครบทุกคำ / เรียงที่ใกล้เคียงที่สุดก่อน (ตรงเป๊ะ > ขึ้นต้นด้วย > มีอยู่ข้างใน)
        //    กรองบน Thread เบื้องหลัง + ยกเลิกรอบเก่าเมื่อพิมพ์ต่อ หน้าจอจึงไม่ค้าง
        private int _filterVersion;

        private static string[] Fields(PackingCardModel x) => new[]
        {
            x.TicketNo, x.ItemNo, x.GroupCode, x.MaterialCode, x.WorkOrder, x.LotNo, x.JobName,
            x.Qty.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            x.TicketDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            x.TicketDate.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)
        };

        private static int Score(PackingCardModel x, string[] words, string kw)
        {
            var f = Fields(x);
            foreach (var w in words)
                if (!f.Any(v => v != null && v.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)) return -1;
            if (f.Any(v => string.Equals(v, kw, StringComparison.OrdinalIgnoreCase))) return 0;
            if (f.Any(v => v != null && v.StartsWith(words[0], StringComparison.OrdinalIgnoreCase))) return 1;
            return 2;
        }

        public async void ApplyFilter()
        {
            string kw = (SearchText ?? "").Trim();
            int version = ++_filterVersion;
            var source = _allItems;

            List<PackingCardModel> result;
            if (string.IsNullOrEmpty(kw)) result = source;
            else
            {
                var words = kw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                result = await Task.Run(() => source
                    .Select((x, i) => new { x, i, s = Score(x, words, kw) })
                    .Where(t => t.s >= 0)
                    .OrderBy(t => t.s).ThenBy(t => t.i)
                    .Select(t => t.x).ToList());
            }
            if (version != _filterVersion) return;   // พิมพ์ต่อแล้ว ผลรอบนี้ไม่ใช้

            _filteredItems = result;
            _revealedCount = 0;
            Application.Current.Dispatcher.Invoke(() =>
            {
                VisibleItems.Clear();
                LoadMore();
            });
        }

        // พิมพ์ในช่องค้นหา: รอให้หยุดพิมพ์ 250 ms แล้วกรอง
        private System.Windows.Threading.DispatcherTimer _searchTimer;
        private void QueueFilter()
        {
            if (_searchTimer == null)
            {
                _searchTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _searchTimer.Tick += (s, e) => { _searchTimer.Stop(); ApplyFilter(); };
            }
            _searchTimer.Stop();
            _searchTimer.Start();
        }
        public void ClearSearch()
        {
            SearchText = string.Empty;
            ApplyFilter();
        }

        // เรียกตอน Scroll ใกล้ล่างสุด เพิ่มทีละ 10 แถว
        public void LoadMore()
        {
            if (!HasMoreToLoad) return;

            var next = _filteredItems.Skip(_revealedCount).Take(PageSize).ToList();
            foreach (var item in next)
            {
                VisibleItems.Add(item);
            }
            _revealedCount += next.Count;

            GroupedItems.Refresh();
            OnPropertyChanged(nameof(HasMoreToLoad));
        }

        public List<PackingCardModel> GetSelectedItems()
            => VisibleItems.Where(x => x.IsSelected).ToList();

        // เรียกหลังพิมพ์สำเร็จ: ตัดรายการที่พิมพ์แล้วออกจากลิสต์ทันที (query รอบถัดไปก็จะไม่ดึงมาอยู่แล้ว
        // เพราะ PackingCardErpService กรองด้วย CIMS.PickListPrintLogs แล้ว แต่ตัดออกจาก UI เลยจะได้ไม่ต้องรอ)
        public void RemoveItems(IEnumerable<PackingCardModel> items)
        {
            if (items == null) return;

            foreach (var item in items.ToList())
            {
                VisibleItems.Remove(item);
                _allItems.Remove(item);
                _filteredItems.Remove(item);

                if (ReferenceEquals(PreviewItem, item)) PreviewItem = null;
            }

            _revealedCount = Math.Min(_revealedCount, _filteredItems.Count);

            GroupedItems.Refresh();
            OnPropertyChanged(nameof(HasMoreToLoad));
        }
    }
}
