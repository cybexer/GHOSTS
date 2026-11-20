// Copyright 2017 Carnegie Mellon University. All Rights Reserved. See LICENSE.md file for terms.

using Ghosts.Api.Infrastructure.Models;
using Ghosts.Api.Infrastructure.Services;

namespace Ghosts.Api.Tests;

/// <summary>
/// A checking-in client is matched to an existing machine by exact value, ignoring case. A name that is a prefix of
/// another (vm1, vm10) is a different machine, and an empty value matches nothing.
/// </summary>
public class MachineMatchingTests
{
    [Fact]
    public async Task A_name_that_is_a_prefix_of_another_does_not_match_it()
    {
        await using var context = TestDb.New("machines");
        context.Machines.Add(Machine("vm10", "vm10.office.local"));
        await context.SaveChangesAsync();

        var match = await MachineService.FindMatchAsync(context.Machines, Incoming("vm1", "vm1.office.local"), "name", default);

        Assert.Null(match);
    }

    [Fact]
    public async Task The_same_name_in_another_case_matches()
    {
        await using var context = TestDb.New("machines");
        var existing = Machine("DESK-03", "desk-03.office.local");
        context.Machines.Add(existing);
        await context.SaveChangesAsync();

        var match = await MachineService.FindMatchAsync(context.Machines, Incoming("desk-03", ""), "name", default);

        Assert.Equal(existing.Id, match?.Id);
    }

    [Fact]
    public async Task With_no_match_setting_an_empty_fqdn_matches_nothing()
    {
        await using var context = TestDb.New("machines");
        context.Machines.Add(Machine("desk-11", "desk-11.office.local"));
        await context.SaveChangesAsync();

        var match = await MachineService.FindMatchAsync(context.Machines, Incoming("desk-31", ""), null, default);

        Assert.Null(match);
    }

    [Fact]
    public async Task With_no_match_setting_a_matching_fqdn_finds_the_machine_when_the_names_differ()
    {
        await using var context = TestDb.New("machines");
        var existing = Machine("old-name", "desk-11.office.local");
        context.Machines.Add(existing);
        await context.SaveChangesAsync();

        var match = await MachineService.FindMatchAsync(context.Machines, Incoming("desk-11", "DESK-11.office.local"), null, default);

        Assert.Equal(existing.Id, match?.Id);
    }

    [Fact]
    public async Task The_id_the_client_sent_wins_over_a_more_recently_reported_duplicate()
    {
        await using var context = TestDb.New("machines");
        var own = Machine("ws1-01", "ws1-01.int.example");
        own.LastReportedUtc = DateTime.UtcNow.AddMinutes(-10);
        var duplicate = Machine("ws1-01", "ws1-01.int.example");
        duplicate.LastReportedUtc = DateTime.UtcNow;
        context.Machines.AddRange(own, duplicate);
        await context.SaveChangesAsync();

        var incoming = Incoming("ws1-01", "ws1-01.int.example");
        incoming.Id = own.Id;
        var match = await MachineService.FindCheckInAsync(context.Machines, incoming, "name", default);

        Assert.Equal(own.Id, match?.Id);
    }

    [Fact]
    public async Task An_id_with_no_record_falls_back_to_matching_by_name()
    {
        await using var context = TestDb.New("machines");
        var existing = Machine("ws1-02", "ws1-02.int.example");
        context.Machines.Add(existing);
        await context.SaveChangesAsync();

        var incoming = Incoming("ws1-02", "ws1-02.int.example");
        incoming.Id = Guid.NewGuid();
        var match = await MachineService.FindCheckInAsync(context.Machines, incoming, "name", default);

        Assert.Equal(existing.Id, match?.Id);
    }

    private static Machine Machine(string name, string fqdn) => new()
    {
        Id = Guid.NewGuid(), Name = name, FQDN = fqdn, Host = name, ResolvedHost = fqdn, HostIp = "10.20.8.3", CurrentUsername = "npc"
    };

    private static Machine Incoming(string name, string fqdn) => new()
    {
        Name = name, FQDN = fqdn, Host = string.Empty, ResolvedHost = string.Empty, HostIp = "10.20.8.3", CurrentUsername = "npc"
    };
}
