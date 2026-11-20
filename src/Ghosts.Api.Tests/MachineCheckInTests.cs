// Copyright 2017 Carnegie Mellon University. All Rights Reserved. See LICENSE.md file for terms.

using Ghosts.Api;
using Ghosts.Api.Hubs;
using Ghosts.Api.Infrastructure.Models;
using Ghosts.Api.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Ghosts.Api.Tests;

/// <summary>
/// A new machine's first check-ins arrive together (its clientid request and its socket's SendId), each on its
/// own request and database context. They must end up on one record, not one each.
/// </summary>
public class MachineCheckInTests
{
    [Fact]
    public async Task Simultaneous_first_check_ins_from_one_machine_create_one_record()
    {
        Program.ApplicationSettings ??= new ApplicationSettings();
        var db = $"checkin-{Guid.NewGuid()}";

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await using var context = TestDb.Named(db);
            var service = new MachineService(context, new Mock<IHubContext<ActivityHub>>().Object, new Mock<INpcService>().Object);
            return (await service.FindOrCreate(CheckIn("ws1-01"), default)).Machine.Id;
        })));

        await using var check = TestDb.Named(db);
        Assert.Equal(1, await check.Machines.CountAsync(m => m.Name == "ws1-01"));
        Assert.Single(results.Distinct());
    }

    private static HttpContext CheckIn(string name)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["ghosts-name"] = name;
        context.Request.Headers["ghosts-fqdn"] = $"{name}.int.example";
        context.Request.Headers["ghosts-host"] = name;
        context.Request.Headers["ghosts-resolvedhost"] = $"{name}.int.example";
        context.Request.Headers["ghosts-ip"] = "10.1.3.11";
        context.Request.Headers["ghosts-user"] = "npc";
        return context;
    }
}
