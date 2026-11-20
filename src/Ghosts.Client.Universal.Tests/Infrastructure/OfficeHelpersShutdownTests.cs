// Copyright 2017 Carnegie Mellon University. All Rights Reserved. See LICENSE.md file for terms.

using Ghosts.Client.Universal.Infrastructure;
using Xunit;

namespace Ghosts.Client.Universal.Tests.Infrastructure;

public class OfficeHelpersShutdownTests
{
    public sealed class FakeOfficeApplication
    {
        public int QuitCalls;
        public void Quit() => QuitCalls++;
    }

    [Fact]
    public void Shutdown_QuitsTheApplication()
    {
        var app = new FakeOfficeApplication();

        OfficeHelpers.Shutdown(null, app);

        Assert.Equal(1, app.QuitCalls);
    }

    [Fact]
    public void Shutdown_ToleratesNothingToCloseAndAnApplicationWithoutQuit()
    {
        // A run that failed before Office started, and an object Quit cannot be called on: no exception may escape the finally.
        OfficeHelpers.Shutdown(null, null);
        OfficeHelpers.Shutdown(new object(), new object());
    }
}
