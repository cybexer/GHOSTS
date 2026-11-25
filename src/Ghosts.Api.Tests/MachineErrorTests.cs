// Copyright 2017 Carnegie Mellon University. All Rights Reserved. See LICENSE.md file for terms.

using Ghosts.Api;
using Ghosts.Api.Hubs;
using Ghosts.Api.Infrastructure.Data;
using Ghosts.Api.Infrastructure.Models;
using Ghosts.Api.Infrastructure.Services;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace Ghosts.Api.Tests;

public class MachineErrorTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task Summary_groups_recent_errors_of_active_machines_with_the_latest_message()
    {
        await using var context = TestDb.New("machine-errors");
        var first = Machine(context, StatusType.Active);
        var second = Machine(context, StatusType.Active);
        var deleted = Machine(context, StatusType.Deleted);
        Error(context, first, "Rdp", "WrongPassword", "old", Now.AddMinutes(-20));
        Error(context, first, "Rdp", "WrongPassword", "new", Now.AddMinutes(-5));
        Error(context, first, "Rdp", "WrongPassword", "too old", Now.AddMinutes(-90));
        Error(context, second, "Session", "SessionTakenOver", "Agent session is Disc", Now.AddMinutes(-1));
        Error(context, deleted, "Rdp", "AccountDisabled", "gone", Now.AddMinutes(-1));
        await context.SaveChangesAsync();

        var result = await Service(context).GetErrorSummary(60, default);

        Assert.Equal(2, result.Count);
        Assert.Equal((second, "SessionTakenOver", 1), (result[0].MachineId, result[0].Code, result[0].Count));
        Assert.Equal((first, "Rdp", "WrongPassword", "new", 2), (result[1].MachineId, result[1].Handler, result[1].Code, result[1].Message, result[1].Count));
    }

    [Fact]
    public async Task Errors_lists_one_machine_newest_first()
    {
        await using var context = TestDb.New("machine-errors");
        var machine = Machine(context, StatusType.Active);
        var other = Machine(context, StatusType.Active);
        Error(context, machine, "Rdp", "WrongPassword", "older", Now.AddMinutes(-10));
        Error(context, machine, "ExecuteFile", "FileMissing", "newer", Now.AddMinutes(-1));
        Error(context, other, "Rdp", "WrongPassword", "other", Now);
        await context.SaveChangesAsync();

        var result = await Service(context).GetErrors(machine, 0, 25, default);

        Assert.Equal(["newer", "older"], result.Select(o => o.Message));
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

    private static void Error(ApplicationDbContext context, Guid machineId, string handler, string code, string message, DateTime createdUtc) =>
        context.HistoryErrors.Add(new HistoryError { MachineId = machineId, Handler = handler, Code = code, Message = message, CreatedUtc = createdUtc });
}
