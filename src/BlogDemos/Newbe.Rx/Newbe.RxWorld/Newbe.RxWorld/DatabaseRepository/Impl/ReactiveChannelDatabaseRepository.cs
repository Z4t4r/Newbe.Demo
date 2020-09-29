﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Newbe.RxWorld.DatabaseRepository.Impl
{
    public class ReactiveChannelDatabaseRepository : IDatabaseRepository
    {
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly IDatabase _database;
        private readonly Channel<BatchItem> _channel;
        private readonly IDisposable _handler;

        public ReactiveChannelDatabaseRepository(
            ITestOutputHelper testOutputHelper,
            IDatabase database)
        {
            _testOutputHelper = testOutputHelper;
            _database = database;
            _channel = Channel.CreateUnbounded<BatchItem>();
            var asyncEnumerable = _channel.Reader.ReadAllAsync();
            var observable = asyncEnumerable.ToObservable();
            _handler = observable.Buffer(TimeSpan.FromMilliseconds(50), 100)
                .Where(x => x.Count > 0)
                .Select(list => Observable.FromAsync(() => BatchInsertData(list)))
                .Concat()
                .Subscribe();
        }

        private async Task BatchInsertData(IEnumerable<BatchItem> items)
        {
            var batchItems = items as BatchItem[] ?? items.ToArray();
            try
            {
                var totalCount = await _database.InsertMany(batchItems.Select(x => x.Item));
                foreach (var batchItem in batchItems)
                {
                    batchItem.TaskCompletionSource.SetResult(totalCount);
                }
            }
            catch (Exception e)
            {
                foreach (var batchItem in batchItems)
                {
                    batchItem.TaskCompletionSource.SetException(e);
                }

                throw;
            }
        }


        public Task<int> InsertData(int item)
        {
            var tcs = new TaskCompletionSource<int>();
            var task = new BatchItem
            {
                Item = item,
                TaskCompletionSource = tcs
            };
            _channel.Writer.TryWrite(task);
            return tcs.Task;
        }

        private struct BatchItem
        {
            public TaskCompletionSource<int> TaskCompletionSource { get; set; }
            public int Item { get; set; }
        }
    }
}