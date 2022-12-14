using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StacksForce.Utils
{
    public interface IDataStream<T>
    {
        Task<List<T>?> ReadMoreAsync(int count);
    }

    public interface IDataStreamProvider<T>
    {
        IDataStream<T> GetStream();
    }

    public sealed class EmptyDataStream<T> : IDataStream<T>
    {
        static public readonly EmptyDataStream<T> EMPTY = new EmptyDataStream<T>();

        static private readonly List<T> EMPTY_LIST = new List<T>(0);
        public async Task<List<T>?> ReadMoreAsync(int count)
        {
            return EMPTY_LIST;
        }
    }

    public sealed class EmptyDataStreamProvider<T> : IDataStreamProvider<T>
    {
        static public readonly IDataStreamProvider<T> EMPTY = new EmptyDataStreamProvider<T>();
        public IDataStream<T> GetStream() => EmptyDataStream<T>.EMPTY;
    }

    public abstract class BasicDataStream<T> : IDataStream<T>
    {
        private int _index = 0;

        private int _inRead = 0;

        private bool _prepared;

        protected BasicDataStream()
        {
        }

        public async Task<List<T>?> ReadMoreAsync(int count)
        {
            if (Interlocked.Exchange(ref _inRead, 1) != 0)
            {
                Log.Fatal("ReadMoreAsync: incorrect read state");
                throw new InvalidOperationException();
            }

            if (!_prepared)
            {
                _prepared = true;
                var prepareTask = Prepare();
                if (prepareTask != null)
                    await prepareTask.ConfigureAwait();
            }

            List<T> allItems = new List<T>();

            bool hasError = false;

            var items = await GetRange(_index, count).ConfigureAwait();
            if (items != null)
            {
                allItems.AddRange(items);
                _index += items.Count;
            }
            else
                hasError = true;

            _inRead = 0;

            if (hasError)
                return null;

            return allItems;
        }

        protected virtual Task? Prepare() => null;

        protected abstract Task<List<T>?> GetRange(long index, long count);
    }

    public class BasicCachedDataStream<T> : IDataStreamProvider<T>
    {
        private readonly List<T> _cache = new List<T>();
        private readonly IDataStream<T>? _dataStream = null;
        private bool _readAll = false;
        private Task<List<T>?>? _currentReadOp;

        public BasicCachedDataStream(IDataStream<T> dataStream, List<T>? data = null)
        {
            _dataStream = dataStream;
            if (data != null)
                _cache.AddRange(data);
        }

        public BasicCachedDataStream(List<T> data)
        {
            _cache = data;
            _readAll = true;
        }

        public IDataStream<T> GetStream() => new Reader(this);

        private async Task<List<T>?> GetRangeThreaded(int index, int count)
        {
            if (_readAll || index + count <= _cache.Count)
                return GetFromCache(index, count);

            if (_currentReadOp != null && !_currentReadOp.IsCompleted)
                await _currentReadOp.ConfigureAwait();

            _currentReadOp = GetRange(index, count);

            var result = await _currentReadOp.ConfigureAwait();
            _currentReadOp = null;

            return result;
        }

        private async Task<List<T>?> GetRange(int index, int count)
        {
            var fromCache = GetFromCache(index, count);
            if (_readAll || count == fromCache.Count)
                return fromCache;

            index += fromCache.Count;
            var res = await _dataStream!.ReadMoreAsync(count - fromCache.Count).ConfigureAwait();
            if (res == null)
                return null;

            if (res.Count == 0)
                _readAll = true;

            _cache.AddRange(res);

            return GetFromCache(index, count);
        }

        private List<T> GetFromCache(int index, int count)
        {
            int inCacheCount = Math.Min(count, _cache.Count - index);
            return inCacheCount > 0 ? _cache.GetRange(index, inCacheCount) : new List<T>();
        }

        public class Reader : IDataStream<T>
        {
            private int _index = 0;
            private readonly BasicCachedDataStream<T> _stream;

            public Reader(BasicCachedDataStream<T> stream)
            {
                _stream = stream;
            }

            public async Task<List<T>?> ReadMoreAsync(int count)
            {
                var res = await _stream.GetRangeThreaded(_index, count).ConfigureAwait();
                if (res != null)
                    _index += res.Count;
                return res;
            }
        }
    }
}
