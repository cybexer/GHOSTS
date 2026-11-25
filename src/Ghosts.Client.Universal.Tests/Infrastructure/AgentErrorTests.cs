// Copyright 2017 Carnegie Mellon University. All Rights Reserved. See LICENSE.md file for terms.

using System;
using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using Ghosts.Client.Universal.Handlers;
using Ghosts.Client.Universal.Health;
using Ghosts.Client.Universal.Infrastructure;
using Ghosts.Domain.Messages.MesssagesForServer;
using Renci.SshNet.Common;
using Xunit;

namespace Ghosts.Client.Universal.Tests.Infrastructure;

public class AgentErrorTests
{
    [Theory]
    [InlineData("[ERROR][com.freerdp.core] - transport_connect_nla:freerdp_set_last_error_ex ERRCONNECT_LOGON_FAILURE [0x00020014]", ErrorCode.WrongPassword)]
    [InlineData("nla_recv_pdu: ERRCONNECT_ACCOUNT_DISABLED [0x00020012]", ErrorCode.AccountDisabled)]
    [InlineData("SEC_E_LOGON_DENIED STATUS_ACCOUNT_LOCKED_OUT", ErrorCode.AccountLocked)]
    [InlineData("ERRCONNECT_PASSWORD_EXPIRED", ErrorCode.PasswordExpired)]
    [InlineData("ERRINFO_DISCONNECTED_BY_OTHERCONNECTION", ErrorCode.SessionTakenOver)]
    [InlineData("ERRCONNECT_DNS_NAME_NOT_FOUND", ErrorCode.ConnectionFailed)]
    [InlineData("Your Remote Desktop Services session has ended. Another user connected to the remote computer, so your connection was lost.", ErrorCode.SessionTakenOver)]
    [InlineData("The logon attempt failed", ErrorCode.WrongPassword)]
    [InlineData("Your account has been disabled. Please see your system administrator.", ErrorCode.AccountDisabled)]
    public void FromText_MapsRdpOutput(string text, ErrorCode expected)
    {
        Assert.Equal(expected, ErrorClassifier.FromText(text));
    }

    [Fact]
    public void FromText_ReturnsNull_WhenNothingMatches()
    {
        Assert.Null(ErrorClassifier.FromText("connected"));
        Assert.Null(ErrorClassifier.FromText(null));
    }

    [Fact]
    public void FromException_MapsKnownExceptions()
    {
        Assert.Equal(ErrorCode.WrongPassword, ErrorClassifier.FromException(new SshAuthenticationException("Permission denied (password).")));
        Assert.Equal(ErrorCode.FileMissing, ErrorClassifier.FromException(new DirectoryNotFoundException()));
        Assert.Equal(ErrorCode.ConnectionFailed, ErrorClassifier.FromException(new SocketException()));
        Assert.Equal(ErrorCode.DependencyMissing, ErrorClassifier.FromException(new Win32Exception(2)));
        Assert.Equal(ErrorCode.HandlerCrashed, ErrorClassifier.FromException(new InvalidOperationException(), ErrorCode.HandlerCrashed));
    }

    [Fact]
    public void ShouldSendError_ThrottlesSameKey()
    {
        var key = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        Assert.True(BaseHandler.ShouldSendError(key, now));
        Assert.False(BaseHandler.ShouldSendError(key, now.AddMinutes(1)));
        Assert.True(BaseHandler.ShouldSendError(key, now + BaseHandler.ErrorThrottle));
        Assert.True(BaseHandler.ShouldSendError(Guid.NewGuid().ToString(), now));
    }

    private const string Header = " USERNAME              SESSIONNAME        ID  STATE   IDLE TIME  LOGON TIME\r\n";

    [Fact]
    public void SessionWatch_ActiveConsoleSession_IsFine()
    {
        string start = null;
        Assert.Null(SessionWatch.Check(Header + ">ghosts                console             1  Active      none   11/25/2026 9:00 AM\r\n", ref start));
        Assert.Equal("console", start);
    }

    [Fact]
    public void SessionWatch_DisconnectedSession_WithOtherUser_IsTakenOver()
    {
        string start = "console";
        var problem = SessionWatch.Check(Header +
            ">ghosts                                    1  Disc        5   11/25/2026 9:00 AM\r\n" +
            " blueteam              rdp-tcp#3           2  Active      .   11/25/2026 9:30 AM\r\n", ref start);
        Assert.Equal("Agent session is Disc, active user: blueteam", problem);
    }

    [Fact]
    public void SessionWatch_SessionMovedToRdp_IsTakenOver()
    {
        string start = "console";
        var problem = SessionWatch.Check(Header + ">ghosts                rdp-tcp#5           1  Active      .   11/25/2026 9:00 AM\r\n", ref start);
        Assert.Equal("Agent session moved from console to rdp-tcp#5", problem);
    }
}
