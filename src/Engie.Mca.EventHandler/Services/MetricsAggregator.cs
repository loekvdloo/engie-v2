using System;
using System.Collections.Generic;
using System.Linq;
using Engie.Mca.EventHandler.Models;
using StackExchange.Redis;

namespace Engie.Mca.EventHandler.Services;

public sealed class MetricsAggregator : IDisposable
{
    private readonly IDatabase? _db;
    private readonly ConnectionMultiplexer? _mux;

    // In-memory fallback (used when Redis unavailable)
    private long _total, _ack, _nack, _delivered, _failed;
    private readonly List<double> _durs = [];
    private readonly Dictionary<string, long> _codes = new();
    private readonly object _lk = new();

    public MetricsAggregator(string? redisUrl)
    {
        if (string.IsNullOrWhiteSpace(redisUrl)) return;
        try
        {
            _mux = ConnectionMultiplexer.Connect(redisUrl);
            _db  = _mux.GetDatabase();
        }
        catch { /* fall back to in-memory */ }
    }

    public void Record(ResponseType? responseType, ProcessingStatus status, double? durationMs, List<ValidationError> errors)
    {
        if (_db is not null)
        {
            try
            {
                _db.StringIncrement("engie:total");
                if (responseType == ResponseType.Ack)     _db.StringIncrement("engie:ack");
                if (responseType == ResponseType.Nack)    _db.StringIncrement("engie:nack");
                if (status == ProcessingStatus.Delivered) _db.StringIncrement("engie:delivered");
                if (status == ProcessingStatus.Failed)    _db.StringIncrement("engie:failed");
                if (durationMs.HasValue)
                {
                    _db.ListRightPush("engie:durations", durationMs.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                    _db.ListTrim("engie:durations", -1000, -1);
                }
                foreach (var err in errors)
                {
                    _db.StringIncrement($"engie:errors:{err.Code}");
                    _db.SetAdd("engie:error_codes", err.Code);
                }
                return;
            }
            catch { /* fall through to in-memory on Redis error */ }
        }

        lock (_lk)
        {
            _total++;
            if (responseType == ResponseType.Ack)     _ack++;
            if (responseType == ResponseType.Nack)    _nack++;
            if (status == ProcessingStatus.Delivered) _delivered++;
            if (status == ProcessingStatus.Failed)    _failed++;
            if (durationMs.HasValue) _durs.Add(durationMs.Value);
            foreach (var err in errors)
            {
                _codes.TryGetValue(err.Code, out var c);
                _codes[err.Code] = c + 1;
            }
        }
    }

    public MetricsSnapshot GetSnapshot()
    {
        if (_db is not null)
        {
            try
            {
                var total     = (long?)_db.StringGet("engie:total")     ?? 0;
                var ack       = (long?)_db.StringGet("engie:ack")       ?? 0;
                var nack      = (long?)_db.StringGet("engie:nack")      ?? 0;
                var delivered = (long?)_db.StringGet("engie:delivered") ?? 0;
                var failed    = (long?)_db.StringGet("engie:failed")    ?? 0;

                var rawDurs = _db.ListRange("engie:durations")
                    .Select(v => double.TryParse(v.ToString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0)
                    .OrderBy(v => v)
                    .ToList();

                double avg = rawDurs.Count > 0 ? rawDurs.Average() : 0;
                double p95 = rawDurs.Count > 0 ? rawDurs[(int)(Math.Ceiling(rawDurs.Count * 0.95) - 1)] : 0;

                var codeMembers = _db.SetMembers("engie:error_codes");
                var errorsByCode = codeMembers
                    .Select(c => (Code: c.ToString(), Count: (long?)_db.StringGet($"engie:errors:{c}") ?? 0))
                    .Where(x => x.Count > 0)
                    .OrderByDescending(x => x.Count)
                    .ToList();

                return new MetricsSnapshot(total, ack, nack, delivered, failed, avg, p95, errorsByCode);
            }
            catch { /* fall through to in-memory on Redis error */ }
        }

        lock (_lk)
        {
            var sorted = _durs.OrderBy(v => v).ToList();
            double avg = sorted.Count > 0 ? sorted.Average() : 0;
            double p95 = sorted.Count > 0 ? sorted[(int)(Math.Ceiling(sorted.Count * 0.95) - 1)] : 0;
            var byCode = _codes.OrderByDescending(k => k.Value)
                .Select(k => (k.Key, k.Value))
                .ToList();
            return new MetricsSnapshot(_total, _ack, _nack, _delivered, _failed, avg, p95, byCode);
        }
    }

    public void Dispose() => _mux?.Dispose();
}

public record MetricsSnapshot(
    long Total, long Ack, long Nack, long Delivered, long Failed,
    double AvgDurationMs, double P95DurationMs,
    List<(string Code, long Count)> ErrorsByCode
);
