using System.Text.RegularExpressions;

namespace Services.Core
{
    public sealed class LogHistoryQueryService
    {
        private static readonly Regex LineRegex = new(
            @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[(?<lvl>[A-Z]{3})\] \[ThreadId:(?<tid>\d+)\] (?<msg>.*)$",
            RegexOptions.Compiled);
        public sealed class QueryParameter
        {
            public DateTime StartInclusive { get; init; }
            public DateTime EndInclusive { get; init; }
            public string? LevelFilter { get; init; }
            public string? Keyword { get; init; }
            public int PageIndex { get; init; }
            public int PageSize { get; init; } = 200;
        }
        public sealed class QueryResult
        {
            public IReadOnlyList<LogEventDto> Rows { get; init; } = Array.Empty<LogEventDto>();
            public int TotalCount { get; init; }
        }
        /// <param name="parameter">查询条件。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>分页结果。</returns>
        public async Task<QueryResult> QueryAsync(QueryParameter parameter, CancellationToken cancellationToken = default)
        {
            // 逻辑意图：历史查询在后台线程执行，避免阻塞 WPF UI。
            return await Task.Run(() => QueryFromFilesInternal(parameter, cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        private static QueryResult QueryFromFilesInternal(QueryParameter parameter, CancellationToken cancellationToken)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var logsDir = Path.Combine(baseDir, "Logs");
            if (!Directory.Exists(logsDir))
            {
                return new QueryResult { Rows = Array.Empty<LogEventDto>(), TotalCount = 0 };
            }

            var all = new List<LogEventDto>();
            var files = Directory.EnumerateFiles(logsDir, "log-*.txt")
                .Concat(Directory.EnumerateFiles(logsDir, "log*.txt"))
                .Distinct()
                .OrderByDescending(static f => f);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var line in File.ReadLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var dto = TryParseLine(line);
                    if (dto == null)
                    {
                        continue;
                    }

                    if (dto.Timestamp < parameter.StartInclusive || dto.Timestamp > parameter.EndInclusive)
                    {
                        continue;
                    }

                    if (!MatchesLevel(dto.Level, parameter.LevelFilter))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(parameter.Keyword))
                    {
                        var k = parameter.Keyword.Trim();
                        if (dto.Message.IndexOf(k, StringComparison.OrdinalIgnoreCase) < 0
                            && dto.Source.IndexOf(k, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                    }

                    all.Add(dto);
                }
            }

            // 新日志在上：按时间倒序
            all.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            var total = all.Count;
            var skip = Math.Max(0, parameter.PageIndex) * Math.Max(1, parameter.PageSize);
            var take = Math.Max(1, parameter.PageSize);
            var page = all.Skip(skip).Take(take).ToList();

            return new QueryResult { Rows = page, TotalCount = total };
        }
        /// <param name="parameter">查询参数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>当前未启用库表时返回 null。</returns>
        public static Task<QueryResult?> TryQuerySqliteAsync(QueryParameter parameter, CancellationToken cancellationToken = default)
        {
            _ = parameter;
            _ = cancellationToken;
            return Task.FromResult<QueryResult?>(null);
        }

        private static LogEventDto? TryParseLine(string line)
        {
            var m = LineRegex.Match(line);
            if (!m.Success)
            {
                return null;
            }

            if (!DateTime.TryParse(m.Groups["ts"].Value, out var ts))
            {
                return null;
            }

            var lvlRaw = m.Groups["lvl"].Value;
            var level = lvlRaw switch
            {
                "INF" => "Info",
                "WRN" => "Warn",
                "ERR" => "Error",
                "DBG" => "Debug",
                "VRB" => "Verbose",
                "FTL" => "Error",
                _ => lvlRaw
            };

            return new LogEventDto
            {
                Timestamp = ts,
                Level = level,
                Source = "File",
                Message = m.Groups["msg"].Value
            };
        }

        private static bool MatchesLevel(string level, string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter) || filter.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return filter.Equals(level, StringComparison.OrdinalIgnoreCase);
        }
    }
}

