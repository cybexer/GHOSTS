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
        public readonly TaskCompletionSource FirstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource FirstCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource SecondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task RunOnce()
        {
            if (FirstStarted.TrySetResult())
                return FirstCompleted.Task;

            SecondStarted.TrySetResult();
            return Task.Delay(Timeout.Infinite, Token);
        }
    }

    [Fact]
    public async Task Run_Loop_WaitsForEachRunOnceBeforeStartingTheNext()
    {
        var handler = new TimelineHandler { HandlerType = HandlerType.Clicks, Loop = true, TimeLineEvents = new List<TimelineEvent>() };
        using var cts = new CancellationTokenSource();
        var sut = new DeferredHandler(new Timeline { Id = Guid.NewGuid() }, handler, cts.Token);
        var run = Task.Run(sut.Run);

        try
        {
            await sut.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var next = await Task.WhenAny(sut.SecondStarted.Task, Task.Delay(100));
            Assert.NotSame(sut.SecondStarted.Task, next);

            sut.FirstCompleted.SetResult();
            await sut.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            cts.Cancel();
            sut.FirstCompleted.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
