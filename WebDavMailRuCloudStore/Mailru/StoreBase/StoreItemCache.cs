﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using NWebDav.Server;
using NWebDav.Server.Http;
using NWebDav.Server.Stores;

namespace YaR.WebDavMailRu.CloudStore.Mailru.StoreBase
{
    //TODO: not thread-safe, refact
    public class StoreItemCache
    {
        private static readonly log4net.ILog Logger = log4net.LogManager.GetLogger(typeof(StoreItemCache));

        public StoreItemCache(IStore store, TimeSpan expirePeriod)
        {
            _store = store;
            _expirePeriod = expirePeriod;

            long cleanPeriod = (long) CleanUpPeriod.TotalMilliseconds;

            _cleanTimer = new Timer(state => RemoveExpired() , null, cleanPeriod, cleanPeriod);
        }

        private readonly IStore _store;
        private readonly Timer _cleanTimer;
        private readonly ConcurrentDictionary<string, TimedItem<IStoreItem>> _items = new ConcurrentDictionary<string, TimedItem<IStoreItem>>();
        private readonly object _locker = new object();

        public TimeSpan CleanUpPeriod
        {
            get => _cleanUpPeriod;
            set
            {
                _cleanUpPeriod = value;
                long cleanPreiod = (long)value.TotalMilliseconds;
                _cleanTimer.Change(cleanPreiod, cleanPreiod);
            }
        }

        public int RemoveExpired()
        {
            if (!_items.Any()) return 0;

            int removedCount = 0;
            foreach (var item in _items)
            {
                if (DateTime.Now - item.Value.Created > TimeSpan.FromMinutes(5))
                {
                    bool removed = _items.TryRemove(item.Key, out _);
                    if (removed) removedCount++;
                }
            }
            if (removedCount > 0)
                Logger.Debug($"Items cache clean: removed {removedCount} expired items");

            return removedCount;
        }

        public IStoreItem Get(WebDavUri uri, IHttpContext context)
        {
            if (_items.TryGetValue(uri.AbsoluteUri, out var item))
            {
                if (IsExpired(item))
                    _items.TryRemove(uri.AbsoluteUri, out item);
                else
                    return item.Item;
            }

            lock (_locker)
            {
                if (!_items.TryGetValue(uri.AbsoluteUri, out item))
                {
                    item = new TimedItem<IStoreItem>
                    {
                        Created = DateTime.Now,
                        Item = _store.GetItemAsync(uri, context).Result
                    };

                    if (!_items.TryAdd(uri.AbsoluteUri, item))
                        _items.TryGetValue(uri.AbsoluteUri, out item);
                }
            }

            return item.Item;
        }

        private bool IsExpired(TimedItem<IStoreItem> item)
        {
            return DateTime.Now - item.Created > _expirePeriod;
        }

        private readonly TimeSpan _expirePeriod;
        private TimeSpan _cleanUpPeriod = TimeSpan.FromMinutes(5);

        private class TimedItem<T>
        {
            public DateTime Created { get; set; }
            public T Item { get; set; }
        }
    }



}