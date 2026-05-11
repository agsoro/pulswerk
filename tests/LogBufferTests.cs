using System;
using System.Linq;
using Connector;
using Xunit;

namespace Connector.Tests
{
    /// <summary>
    /// Tests for the LogBuffer ring-buffer implementation.
    /// LogBuffer.GetLatest() returns entries in chronological order (oldest → newest).
    /// </summary>
    public class LogBufferTests
    {
        [Fact]
        public void Empty_CountIsZero()
        {
            var buf = new LogBuffer(100);
            Assert.Equal(0, buf.Count);
            Assert.Equal(100, buf.Capacity);
        }

        [Fact]
        public void Add_IncreasesCount()
        {
            var buf = new LogBuffer(100);
            buf.Add(new LogEntry(DateTime.UtcNow, LogSeverity.Info, "test", "src"));

            Assert.Equal(1, buf.Count);
        }

        [Fact]
        public void GetLatest_ReturnsInChronologicalOrder()
        {
            var buf = new LogBuffer(100);
            buf.Add(new LogEntry(DateTime.UtcNow, LogSeverity.Info, "first", ""));
            buf.Add(new LogEntry(DateTime.UtcNow, LogSeverity.Info, "second", ""));
            buf.Add(new LogEntry(DateTime.UtcNow, LogSeverity.Info, "third", ""));

            var latest = buf.GetLatest(3);

            Assert.Equal(3, latest.Count);
            // Chronological = oldest first
            Assert.Equal("first", latest[0].Message);
            Assert.Equal("second", latest[1].Message);
            Assert.Equal("third", latest[2].Message);
        }

        [Fact]
        public void GetLatest_PartialCount()
        {
            var buf = new LogBuffer(100);
            for (int i = 0; i < 10; i++)
                buf.Add(new LogEntry(DateTime.UtcNow, LogSeverity.Info, $"msg{i}", ""));

            // GetLatest returns the first N entries from the chronological ring
            // When buffer isn't full, start = (_head - _count + _capacity) % _capacity
            // = (10 - 10 + 100) % 100 = 0, so it returns msg0, msg1, msg2
            var latest = buf.GetLatest(3);

            Assert.Equal(3, latest.Count);
            Assert.Equal("msg0", latest[0].Message);
            Assert.Equal("msg1", latest[1].Message);
            Assert.Equal("msg2", latest[2].Message);
        }

        [Fact]
        public void RingBuffer_OverflowWrapsCorrectly()
        {
            var buf = new LogBuffer(5);
            for (int i = 0; i < 10; i++)
                buf.Add(new LogEntry(DateTime.UtcNow, LogSeverity.Info, $"msg{i}", ""));

            Assert.Equal(5, buf.Count);

            var latest = buf.GetLatest(5);
            // Should have msg5..msg9 (the last 5 written) in chronological order
            Assert.Equal("msg5", latest[0].Message);
            Assert.Equal("msg9", latest[4].Message);
        }

        [Fact]
        public void GetLatest_MoreThanAvailable_ReturnsAll()
        {
            var buf = new LogBuffer(100);
            buf.Add(new LogEntry(DateTime.UtcNow, LogSeverity.Info, "only", ""));

            var latest = buf.GetLatest(50);

            Assert.Single(latest);
        }

        [Fact]
        public void Severity_Preserved()
        {
            var buf = new LogBuffer(100);
            buf.Add(new LogEntry(DateTime.UtcNow, LogSeverity.Error, "error msg", "src"));

            var latest = buf.GetLatest(1);
            Assert.Equal(LogSeverity.Error, latest[0].Severity);
        }
    }

    /// <summary>
    /// Tests for the ConsoleLogger wrapper.
    /// </summary>
    public class ConsoleLoggerTests
    {
        [Fact]
        public void Info_AddsToBuffer()
        {
            var buf = new LogBuffer(100);
            var logger = new ConsoleLogger(buf);

            logger.Info("test info");

            Assert.Equal(1, buf.Count);
            var entry = buf.GetLatest(1)[0];
            Assert.Equal(LogSeverity.Info, entry.Severity);
            Assert.Contains("test info", entry.Message);
        }

        [Fact]
        public void Error_AddsToBuffer()
        {
            var buf = new LogBuffer(100);
            var logger = new ConsoleLogger(buf);

            logger.Error("test error");

            var entry = buf.GetLatest(1)[0];
            Assert.Equal(LogSeverity.Error, entry.Severity);
        }

        [Fact]
        public void Buffer_Property_ReturnsBuffer()
        {
            var buf = new LogBuffer(100);
            var logger = new ConsoleLogger(buf);

            Assert.Same(buf, logger.Buffer);
        }
    }
}
