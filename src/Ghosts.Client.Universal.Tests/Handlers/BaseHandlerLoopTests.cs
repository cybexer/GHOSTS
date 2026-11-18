// Copyright 2017 Carnegie Mellon University. All Rights Reserved. See LICENSE.md file for terms.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ghosts.Client.Universal.Handlers;
using Ghosts.Domain;
using Xunit;

namespace Ghosts.Client.Universal.Tests.Handlers;

public class BaseHandlerLoopTests
{
    // A handler whose RunOnce() returns before its work is done, like Clicks (Task.Run) or Watcher (async).
    private sealed class DeferredHandler(Timeline timeline, TimelineHandler handler, CancellationToken token)
        : BaseHandler(timeline, handler, token)
    {
        public int Runs;

        protected override Task RunOnce() => Task.Run(() =>
        {
            Interlocked.Increment(ref Runs);
            if (Token.WaitHandle.WaitOne(100)) Token.ThrowIfCancellationRequested();
        }, Token);
    }

    [Fact]
    public void Run_Loop_WaitsForEachRunOnceBeforeStartingTheNext()
    {
        var handler = new TimelineHandler { HandlerType = HandlerType.Clicks, Loop = true, TimeLineEvents = new List<TimelineEvent>() };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(550));
        var sut = new DeferredHandler(new Timeline { Id = Guid.NewGuid() }, handler, cts.Token);

        sut.Run();

        // 550 ms of 100 ms runs is about 6. Before the fix the loop never waited and started thousands.
        Assert.InRange(sut.Runs, 1, 10);
    }
}
