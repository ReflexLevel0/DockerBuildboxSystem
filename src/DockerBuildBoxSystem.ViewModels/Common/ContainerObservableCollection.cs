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

        /// <summary>
        /// Adds the elements of the specified collection to the end of the current collection.
        /// </summary>
        /// <remarks>This method raises a collection changed event with the <see
        /// cref="System.Collections.Specialized.NotifyCollectionChangedAction.Reset"/> action after the items are
        /// added. Property change notifications for <c>Count</c> and the indexer are also raised. If <paramref
        /// name="items"/> is <see langword="null"/> or contains no elements, the method performs no action.</remarks>
        /// <param name="items">The collection whose elements should be added to the end of the collection. If <paramref name="items"/> is
        /// <see langword="null"/> or empty, no elements are added.</param>
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

        /// <summary>
        /// Removes all items from the collection and adds the elements of the specified sequence.
        /// </summary>
        /// <remarks>This method raises a single <see
        /// cref="System.Collections.Specialized.NotifyCollectionChangedEventArgs"/> event with the <see
        /// cref="System.Collections.Specialized.NotifyCollectionChangedAction.Reset"/> action after the operation
        /// completes. Property change notifications for <c>Count</c> and the indexer are also raised.</remarks>
        /// <param name="items">The sequence of items to add to the collection. If <paramref name="items"/> is <see langword="null"/>, the
        /// collection is cleared and no items are added.</param>
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
