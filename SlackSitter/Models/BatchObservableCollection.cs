using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace SlackSitter.Models
{
    /// <summary>
    /// バッチ更新をサポートする ObservableCollection。
    /// BeginBatchUpdate() ～ EndBatchUpdate() の間は CollectionChanged 通知を抑制し、
    /// EndBatchUpdate() で Reset を1回だけ発火する。
    /// 100件の個別 Insert → 100回の UI 通知 が、1回の Reset 通知に削減される。
    /// </summary>
    public class BatchObservableCollection<T> : ObservableCollection<T>
    {
        private bool _isBatchUpdating;

        /// <summary>
        /// バッチ更新を開始する。この呼び出し以降、EndBatchUpdate() まで
        /// CollectionChanged イベントは抑制される。
        /// </summary>
        public void BeginBatchUpdate()
        {
            _isBatchUpdating = true;
        }

        /// <summary>
        /// バッチ更新を終了し、CollectionChanged(Reset) を1回発火する。
        /// </summary>
        public void EndBatchUpdate()
        {
            _isBatchUpdating = false;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_isBatchUpdating)
            {
                base.OnCollectionChanged(e);
            }
        }
    }
}
