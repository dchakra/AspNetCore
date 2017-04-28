﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Testing;
using Microsoft.Extensions.CommandLineUtils;
using Xunit.Abstractions;

namespace Microsoft.DotNet.Watcher.Tools.FunctionalTests
{
    public class WatchableApp : IDisposable
    {
        private const string StartedMessage = "Started";
        private const string ExitingMessage = "Exiting";

        private readonly ITestOutputHelper _logger;
        private string _appName;
        private bool _prepared;


        public WatchableApp(string appName, ITestOutputHelper logger)
        {
            _logger = logger;
            _appName = appName;
            Scenario = new ProjectToolScenario(logger);
            Scenario.AddTestProjectFolder(appName);
            SourceDirectory = Path.Combine(Scenario.WorkFolder, appName);
        }

        public ProjectToolScenario Scenario { get; }

        public AwaitableProcess Process { get; protected set; }

        public string SourceDirectory { get; }

        public Task HasRestarted()
            => Process.GetOutputLineAsync(StartedMessage).TimeoutAfter(TimeSpan.FromMinutes(2));

        public Task HasExited()
            => Process.GetOutputLineAsync(ExitingMessage);

        public bool UsePollingWatcher { get; set; }

        public async Task<int> GetProcessId()
        {
            var line = await Process.GetOutputLineAsync(l => l.StartsWith("PID ="));
            var pid = line.Split('=').Last();
            return int.Parse(pid);
        }

        public void Prepare()
        {
            Scenario.Restore(_appName);
            Scenario.Build(_appName);
            _prepared = true;
        }

        public void Start(IEnumerable<string> arguments, [CallerMemberName] string name = null)
        {
            if (!_prepared)
            {
                throw new InvalidOperationException("Call .Prepare() first");
            }

            var args = Scenario
                .GetDotnetWatchArguments()
                .Concat(arguments);

            var spec = new ProcessSpec
            {
                Executable = DotNetMuxer.MuxerPathOrDefault(),
                Arguments = args,
                WorkingDirectory = SourceDirectory,
                EnvironmentVariables =
                {
                    ["DOTNET_CLI_CONTEXT_VERBOSE"] = bool.TrueString
                },
            };

            Process = new AwaitableProcess(spec, _logger);
            Process.Start();
        }

        public Task StartWatcherAsync([CallerMemberName] string name = null)
            => StartWatcherAsync(Array.Empty<string>(), name);

        public async Task StartWatcherAsync(string[] arguments, [CallerMemberName] string name = null)
        {
            var args = new[] { "run", "--" }.Concat(arguments);
            Start(args, name);

            // Make this timeout long because it depends much on the MSBuild compilation speed.
            // Slow machines may take a bit to compile and boot test apps
            await Process.GetOutputLineAsync(StartedMessage).TimeoutAfter(TimeSpan.FromMinutes(2));
        }

        public virtual void Dispose()
        {
            _logger?.WriteLine("Disposing WatchableApp");
            Process?.Dispose();
            Scenario.Dispose();
        }
    }
}
