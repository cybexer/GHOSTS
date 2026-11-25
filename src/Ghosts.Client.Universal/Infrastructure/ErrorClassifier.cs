// Copyright 2017 Carnegie Mellon University. All Rights Reserved. See LICENSE.md file for terms.

using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Ghosts.Domain.Messages.MesssagesForServer;
using Renci.SshNet.Common;

namespace Ghosts.Client.Universal.Infrastructure;

/// <summary>
/// Maps what a handler sees (an exception, FreeRDP output, an mstsc dialog text) to an ErrorCode for the server.
/// </summary>
public static class ErrorClassifier
{
    public static ErrorCode FromException(Exception e, ErrorCode fallback = ErrorCode.Unknown) => e switch
    {
        SshAuthenticationException => ErrorCode.WrongPassword,
        WebException { Response: FtpWebResponse { StatusCode: FtpStatusCode.NotLoggedIn } } => ErrorCode.WrongPassword,
        FileNotFoundException or DirectoryNotFoundException => ErrorCode.FileMissing,
        SocketException or SshConnectionException => ErrorCode.ConnectionFailed,
        Win32Exception { NativeErrorCode: 2 } => ErrorCode.DependencyMissing,
        _ => fallback
    };

    /// <summary>
    /// Classifies FreeRDP output (ERRCONNECT_*/ERRINFO_*/STATUS_* names) or Windows RDP dialog text.
    /// Specific account states are checked before the generic logon failure, as their text often includes both.
    /// Returns null when nothing matches.
    /// </summary>
    public static ErrorCode? FromText(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var t = text.ToUpperInvariant();

        if (t.Contains("ACCOUNT_DISABLED") || t.Contains("ACCOUNT HAS BEEN DISABLED") || t.Contains("ACCOUNT IS DISABLED"))
            return ErrorCode.AccountDisabled;
        if (t.Contains("LOCKED_OUT") || t.Contains("LOCKED OUT"))
            return ErrorCode.AccountLocked;
        if (t.Contains("PASSWORD_EXPIRED") || t.Contains("PASSWORD_MUST_CHANGE") || t.Contains("ACCOUNT_EXPIRED")
            || t.Contains("PASSWORD HAS EXPIRED") || t.Contains("MUST CHANGE YOUR PASSWORD") || t.Contains("ACCOUNT HAS EXPIRED"))
            return ErrorCode.PasswordExpired;
        if (t.Contains("OTHERCONNECTION") || t.Contains("OTHER_CONNECTION") || t.Contains("ANOTHER USER CONNECTED"))
            return ErrorCode.SessionTakenOver;
        if (t.Contains("LOGON_FAILURE") || t.Contains("AUTHENTICATION_FAILED") || t.Contains("WRONG_PASSWORD")
            || t.Contains("LOGON ATTEMPT FAILED") || t.Contains("CREDENTIALS THAT WERE USED TO CONNECT")
            || t.Contains("USER NAME OR PASSWORD IS INCORRECT"))
            return ErrorCode.WrongPassword;
        if (t.Contains("ERRCONNECT_") || t.Contains("CAN'T CONNECT TO THE REMOTE COMPUTER"))
            return ErrorCode.ConnectionFailed;
        return null;
    }
}
