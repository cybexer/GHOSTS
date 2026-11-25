// Copyright 2017 Carnegie Mellon University. All Rights Reserved. See LICENSE.md file for terms.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Ghosts.Client.Universal.Handlers;
using Ghosts.Domain.Messages.MesssagesForServer;
using NLog;

namespace Ghosts.Client.Universal.Health;

/// <summary>
/// Windows only. Reports SessionTakenOver when the agent's own logon session is disconnected (another user signed in)
/// or moves to another session (someone RDPed in as the agent user).
/// Reads `quser`, whose output is English on the exercise images.
/// </summary>
public static class SessionWatch
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    public static void Start()
    {
        if (!OperatingSystem.IsWindows()) return;
        new Thread(Run) { IsBackground = true, Name = "SessionWatch" }.Start();
    }

    private static void Run()
    {
        string startSession = null;
        while (true)
        {
            try
            {
                var problem = Check(Quser(), ref startSession);
                if (problem != null)
                    BaseHandler.ReportError("Session", ErrorCode.SessionTakenOver, problem, Environment.UserName);
            }
            catch (Exception e)
            {
                _log.Debug(e);
            }
            Thread.Sleep(60000);
        }
        // ReSharper disable once FunctionNeverReturns
    }

    private static string Quser()
    {
        using var p = Process.Start(new ProcessStartInfo("quser") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
        var output = p!.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    /// <summary>
    /// Returns what is wrong with the agent's session (the row quser marks with '>'), or null when it is fine.
    /// The first session name seen is the baseline.
    /// </summary>
    internal static string Check(string quserOutput, ref string startSession)
    {
        var rows = quserOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(l => (Own: l.StartsWith('>'), Fields: l.TrimStart('>').Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Where(r => r.Fields.Length >= 3)
            // a disconnected session has no SESSIONNAME, so the ID comes right after the user name
            .Select(r => int.TryParse(r.Fields[1], out _)
                ? (r.Own, User: r.Fields[0], Session: "", State: r.Fields[2])
                : (r.Own, User: r.Fields[0], Session: r.Fields[1], State: r.Fields.Length > 3 ? r.Fields[3] : ""))
            .ToList();

        var own = rows.FirstOrDefault(r => r.Own);
        if (own.User == null) return null;

        if (own.State != "Active")
        {
            var others = rows.Where(r => !r.Own && r.State == "Active").Select(r => r.User).ToList();
            return $"Agent session is {own.State}" + (others.Count > 0 ? $", active user: {string.Join(", ", others)}" : "");
        }

        startSession ??= own.Session;
        if (own.Session != startSession)
            return $"Agent session moved from {startSession} to {own.Session}";
        return null;
    }
}
