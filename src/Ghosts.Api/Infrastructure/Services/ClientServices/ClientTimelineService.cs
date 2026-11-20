// Copyright 2017 Carnegie Mellon University. All Rights Reserved. See LICENSE.md file for terms.

using System;
using System.Threading;
using System.Threading.Tasks;
using Ghosts.Domain;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using NLog;

namespace Ghosts.Api.Infrastructure.Services.ClientServices;

public interface IClientTimelineService
{
    Task<(bool Success, object Result, string Error)> ProcessTimelineAsync(HttpContext context, string rawJson,
        CancellationToken ct);
}

public class ClientTimelineService(IMachineService machineService, IMachineTimelinesService timelineService)
    : IClientTimelineService
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    public async Task<(bool Success, object Result, string Error)> ProcessTimelineAsync(HttpContext context,
        string rawJson, CancellationToken ct)
    {
        try
        {
            if (!context.Request.Headers.TryGetValue("ghosts-id", out var id) || string.IsNullOrEmpty(id))
            {
                _log.Warn("Missing ghosts-id header");
                return (false, null, "Missing ghosts-id header");
            }

            _log.Info($"Request by {id}");

            // The same lookup as every other check-in. Creating a record from the id alone added a second
            // record for a machine whose id the server did not know but whose name it did.
            var found = await machineService.FindOrCreate(context, ct);
            if (!found.IsValid())
            {
                return (false, null, found.Error);
            }

            var machine = found.Machine;

            Timeline timeline;
            try
            {
                timeline = JsonConvert.DeserializeObject<Timeline>(rawJson);
            }
            catch (Exception e)
            {
                _log.Error(e, "Invalid timeline file");
                return (false, null, "Invalid timeline format");
            }

            var result = await timelineService.CreateAsync(machine, timeline, ct);
            return (true, result, string.Empty);
        }
        catch (Exception e)
        {
            _log.Error(e);
            return (false, null, $"Exception occured: {e.Message}");
        }
    }
}
