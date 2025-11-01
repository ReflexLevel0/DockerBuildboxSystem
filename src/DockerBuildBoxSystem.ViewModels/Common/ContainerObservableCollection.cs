using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace DockerBuildBoxSystem.ViewModels.Common
{
    /// <summary>
    /// An ObservableCollection with batch operations to reduce change notifications and improve the overall UI performance for container outputs.
    /// </summary>
    public class ContainerObservableCollection<T> : ObservableCollection<T>
    {
        public ContainerObservableCollection() : base() { }
        public ContainerObservableCollection(IEnumerable<T> collection) : base(collection) { }

        public void AddRange(IEnumerable<T> items)
        {
            if (items is null) return;
            var list = items as IList<T> ?? items.ToList();
            if (list.Count == 0) return;

            CheckReentrancy();

            foreach (var item in list)
            {
                Items.Add(item);
            }

            //raise property changes events
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));

            //use Reset since WPF views don't support range Add notifications (so far as i know)
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void ClearAndAddRange(IEnumerable<T> items)
        {
            if (items is null)
            {
                Clear();
                return;
            }

            var list = items as IList<T> ?? items.ToList();

            CheckReentrancy();
            Items.Clear();
            foreach (var item in list)
            {
                Items.Add(item);
            }

            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
