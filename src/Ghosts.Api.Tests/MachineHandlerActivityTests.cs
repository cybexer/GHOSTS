// Copyright 2017 Carnegie Mellon University. All Rights Reserved. See LICENSE.md file for terms.

using Ghosts.Api;
using Ghosts.Api.Hubs;
using Ghosts.Api.Infrastructure.Data;
using Ghosts.Api.Infrastructure.Models;
using Ghosts.Api.Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace Ghosts.Api.Tests;

public class MachineHandlerActivityTests
{
    private static readonly DateTime Hour = new(DateTime.UtcNow.AddHours(-3).Ticks / TimeSpan.TicksPerHour * TimeSpan.TicksPerHour, DateTimeKind.Utc);

    [Fact]
    public async Task Counts_active_machine_activity_per_handler_and_bucket()
    {
        await using var context = TestDb.New("handler-activity");
        var first = Machine(context, StatusType.Active);
        var second = Machine(context, StatusType.Active);
        var deleted = Machine(context, StatusType.Deleted);
        Activity(context, first, "Command", Hour.AddMinutes(1));
        Activity(context, first, "Command", Hour.AddMinutes(59).AddSeconds(59));
        Activity(context, second, "Command", Hour.AddMinutes(60));
        Activity(context, first, "Notepad", Hour.AddMinutes(30));
        Activity(context, first, "Notepad", DateTime.UtcNow.AddHours(-25));
        Activity(context, first, null, Hour.AddMinutes(5));
        Activity(context, deleted, "Command", Hour.AddMinutes(5));
        await context.SaveChangesAsync();

        var result = await Service(context).GetHandlerActivity(24, 60, default);

        Assert.Equal(2, result.Count);
        Assert.Equal(("Command", 3, 2), (result[0].Handler, result[0].Events, result[0].Machines));
        Assert.Equal(
            [new HandlerActivityBucket(Hour, 2), new HandlerActivityBucket(Hour.AddHours(1), 1)],
            result[0].Buckets);
        Assert.Equal(("Notepad", 1, 1), (result[1].Handler, result[1].Events, result[1].Machines));
        Assert.Equal([new HandlerActivityBucket(Hour, 1)], result[1].Buckets);
        Assert.Equal(DateTimeKind.Utc, result[0].Buckets[0].StartUtc.Kind);
    }

    [Fact]
    public async Task Aligns_buckets_to_the_bucket_size()
    {
        await using var context = TestDb.New("handler-activity");
        var machine = Machine(context, StatusType.Active);
        Activity(context, machine, "Command", Hour.AddMinutes(4).AddSeconds(59));
        Activity(context, machine, "Command", Hour.AddMinutes(5));
        Activity(context, machine, "Command", Hour.AddMinutes(17).AddSeconds(30));
        await context.SaveChangesAsync();

        var result = await Service(context).GetHandlerActivity(24, 5, default);

        Assert.Equal(
            [new HandlerActivityBucket(Hour, 1), new HandlerActivityBucket(Hour.AddMinutes(5), 1), new HandlerActivityBucket(Hour.AddMinutes(15), 1)],
            result.Single().Buckets);
    }

    private static MachineService Service(ApplicationDbContext context)
    {
        Program.ApplicationSettings ??= new ApplicationSettings();
        return new MachineService(context, new Mock<IHubContext<ActivityHub>>().Object, new Mock<INpcService>().Object);
    }

    private static Guid Machine(ApplicationDbContext context, StatusType status)
    {
        var machine = new Machine { Id = Guid.NewGuid(), Name = $"ws-{Guid.NewGuid()}", Status = status };
        context.Machines.Add(machine);
        return machine.Id;
    }

    private static void Activity(ApplicationDbContext context, Guid machineId, string handler, DateTime createdUtc) =>
        context.HistoryTimeline.Add(new HistoryTimeline { MachineId = machineId, Handler = handler, CreatedUtc = createdUtc });
}
