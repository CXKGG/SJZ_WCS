using System;

namespace Service.Common
{
    /// 雪花算法 ID 生成器（64 位）：1 位符号(恒 0) + 41 位毫秒时间戳(相对自定义纪元) + 10 位节点(workerId) + 12 位序列。
    /// 单进程内线程安全；跨进程/实例靠不同 workerId 区分，保证全局唯一。
    /// 生成的 long 可反解出时间戳（便于排查），但不再像 "T+时间" 那样可直接人读——以可读性换取全局唯一性，符合审查结论。
    public sealed class SnowflakeIdGenerator
    {
        // 自定义纪元（2026-01-01 UTC）；41 位毫秒时间戳可用约 69 年
        private static readonly DateTime Epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private const long WorkerIdBits = 10;
        private const long MaxWorkerId = (1L << (int)WorkerIdBits) - 1; // 1023
        private const long SequenceBits = 12;
        private const long MaxSequence = (1L << (int)SequenceBits) - 1; // 4095
        private const int WorkerIdShift = (int)SequenceBits;
        private const int TimestampShift = (int)(WorkerIdBits + SequenceBits);

        private readonly long _workerId;
        private readonly long _epochMillis;
        private long _lastTimestamp = -1;
        private long _sequence = 0;
        private readonly object _lock = new object();

        public SnowflakeIdGenerator(long workerId)
        {
            if (workerId < 0 || workerId > MaxWorkerId)
            {
                throw new ArgumentOutOfRangeException(nameof(workerId),
                    $"workerId 必须在 0~{MaxWorkerId} 之间");
            }

            _workerId = workerId;
            _epochMillis = (long)(Epoch - DateTime.UnixEpoch).TotalMilliseconds;
        }

        /// <summary>生成下一个唯一 ID。时钟回拨时抛 <see cref="InvalidOperationException"/>，由调用方降级处理。</summary>
        public long NextId()
        {
            lock (_lock)
            {
                long timestamp = CurrentMillis();
                if (timestamp < _lastTimestamp)
                {
                    /// 时钟回拨：直接抛，不静默返回可能重复的 ID（调用方可记日志并下一轮重试）
                    throw new InvalidOperationException(
                        $"系统时钟回拨，拒绝生成 ID（last={_lastTimestamp}, now={timestamp}）");
                }

                if (timestamp == _lastTimestamp)
                {
                    _sequence = (_sequence + 1) & MaxSequence;
                    if (_sequence == 0)
                    {
                        /// 当前毫秒序列号用尽，自旋等到下一毫秒
                        timestamp = WaitNextMillis(_lastTimestamp);
                    }
                }
                else
                {
                    _sequence = 0;
                }

                _lastTimestamp = timestamp;
                return ((timestamp - _epochMillis) << TimestampShift)
                       | (_workerId << WorkerIdShift)
                       | _sequence;
            }
        }

        /// <summary>
        /// 获取当前毫秒数
        /// </summary>
        /// <returns></returns>
        private static long CurrentMillis() => (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;

        /// <summary>
        /// 等待到下一毫秒
        /// </summary>
        /// <param name="lastTimestamp"></param>
        /// <returns></returns>
        private static long WaitNextMillis(long lastTimestamp)
        {
            long ts = CurrentMillis();
            while (ts <= lastTimestamp)
            {
                ts = CurrentMillis();
            }

            return ts;
        }
    }
}
