using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CIMS.Helpers
{
    // ObservableCollection ที่ใส่ข้อมูลทั้งชุดได้ในครั้งเดียว: แจ้งตาราง 1 ครั้ง (Reset) แทนการแจ้งทีละแถว
    // ตารางที่จัดกลุ่ม (Store Max-Min) จะจัดกลุ่ม/วาดใหม่รอบเดียว แทนที่จะทำซ้ำเป็นพันรอบตอนโหลดสินค้าเป็นพันรายการ
    public class BulkObservableCollection<T> : ObservableCollection<T>
    {
        public void ReplaceAll(IEnumerable<T> items)
        {
            CheckReentrancy();
            Items.Clear();
            foreach (var item in items) Items.Add(item);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        // ต่อท้ายทีละแถว (ใช้ตอนเลื่อนลงโหลดเพิ่มทีละไม่กี่แถว - ตารางคงตำแหน่งเลื่อนไว้)
        public void AddRange(IEnumerable<T> items)
        {
            foreach (var item in items) Add(item);
        }
    }
}
