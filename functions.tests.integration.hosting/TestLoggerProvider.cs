// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Integration.Hosting
{
    public class TestLoggerProvider : ILoggerProvider
    {
        readonly ILogger _output;
        public TestLoggerProvider(ILogger output){
            _output = output;
        }

        private ConcurrentDictionary<string, ILogger> LoggerCache { get; } = new ConcurrentDictionary<string, ILogger>();

        public IEnumerable<ILogger> CreatedLoggers => LoggerCache.Values;

        public ILogger CreateLogger(string categoryName)
        {
            //return LoggerCache.GetOrAdd(categoryName, (key) => new TestLogger(key));
            return LoggerCache.GetOrAdd(categoryName, (key) => _output);
        }

        public IList<LogMessage> GetAllLogMessages()
        {
            return new List<LogMessage>();
            // return CreatedLoggers.SelectMany(l => l.GetLogMessages()).OrderBy(p => p.Timestamp).ToList();
        }

        public void ClearAllLogMessages()
        {
            // foreach (TestLogger logger in CreatedLoggers)
            // {
            //     logger.ClearLogMessages();
            // }
        }

        public void Dispose()
        {
        }
    }
}
