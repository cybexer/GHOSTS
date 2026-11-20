// Copyright 2017 Carnegie Mellon University. All Rights Reserved. See LICENSE.md file for terms.

using System;
using System.Runtime.InteropServices;
using Ghosts.Client.Infrastructure;
using Ghosts.Domain;
using Newtonsoft.Json;
using NLog;

namespace Ghosts.Client.Universal.Infrastructure;

internal static class OfficeHelpers
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    internal static bool ShouldOpenExisting(TimelineHandler handler)
    {
        if (handler.HandlerArgs.TryGetValue("workingset", out var key))
        {
            try
            {
                dynamic obj = JsonConvert.DeserializeObject(key.ToString() ?? string.Empty);
                if (obj != null)
                {
                    var max = Convert.ToInt32(obj.max);
                    var maxAgeInHours = Convert.ToInt32(obj["max-age-in-hours"]);
                    var currentDocCount = FileListing.GetFileCount(handler.HandlerType, maxAgeInHours);
                    if (currentDocCount > max)
                    {
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                _log.Error(e);
            }
        }

        return false;
    }

    /// <summary>
    /// Ends one Office automation run: quits the application and releases its COM references.
    /// Quit() alone left the Office process running while .NET still held the references, one
    /// EXCEL.EXE per run, until the workstation ran out of memory and GHOSTS died (162 EXCEL.EXE,
    /// 0x800705AF, on a Windows 10 client after one night). Called from a finally, so a failed
    /// run is cleaned up too; a second Quit on an already closed application is harmless.
    /// </summary>
    internal static void Shutdown(object document, object application)
    {
        if (application != null)
        {
            try { ((dynamic)application).Quit(); }
            catch (Exception e) { _log.Trace($"Office Quit: {e.Message}"); }
        }

        if (OperatingSystem.IsWindows())
        {
            foreach (var o in new[] { document, application })
            {
                if (o == null || !Marshal.IsComObject(o)) continue;
                try { Marshal.FinalReleaseComObject(o); }
                catch (Exception e) { _log.Trace($"Office COM release: {e.Message}"); }
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
