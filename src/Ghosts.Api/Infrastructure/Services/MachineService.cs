// Copyright 2017 Carnegie Mellon University. All Rights Reserved. See LICENSE.md file for terms.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ghosts.Api.Infrastructure.Models;
using Ghosts.Api.Hubs;
using Ghosts.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace Ghosts.Api.Infrastructure.Services
{
    public interface IMachineService
    {
        Task<List<Machine>> GetAsync(string q, CancellationToken ct);
        Task<Machine> GetByIdAsync(Guid id, CancellationToken ct);

        Task<FindMachineResponse> FindOrCreate(HttpContext httpContext, CancellationToken ct);
        List<MachineListItem> GetList();
        Task<Guid> CreateAsync(Machine model, CancellationToken ct);
        Task<Machine> UpdateAsync(Machine model, CancellationToken ct);
        Task<Guid> DeleteAsync(Guid model, CancellationToken ct);
        Task<List<HistoryHealth>> GetMachineHistoryHealth(Guid model, CancellationToken ct);
        Task<List<Machine.MachineHistoryItem>> GetMachineHistory(Guid model, CancellationToken ct);
        Task<List<HistoryTimeline>> GetActivity(Guid id, int skip, int take, CancellationToken ct);
        Task AddActivity(Guid id, HistoryTimeline historyTimeline, CancellationToken ct);
        Task<List<HandlerActivity>> GetHandlerActivity(int hours, int bucketMinutes, CancellationToken ct);
    }

    public class MachineService(ApplicationDbContext context, IHubContext<ActivityHub> activityHubContext, INpcService npcService) : IMachineService
    {
        // Check-ins run concurrently; without this, two machines can both create the same group name
        private static readonly SemaphoreSlim _groupLock = new(1, 1);

        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private readonly ApplicationDbContext _context = context;
        private readonly int _lookBack = Program.ApplicationSettings.LookbackRecords;

        public async Task<List<Machine>> GetAsync(string q, CancellationToken ct)
        {
            var list = await _context.Machines.Where(o => o.Status == StatusType.Active).OrderByDescending(o => o.LastReportedUtc).ToListAsync(ct);
            foreach (var item in list)
            {
                item.HistoryHealth = null;
                item.HistoryTimeline = null;
                item.HistoryTrackables = null;
                item.History = null;
            }

            return list;
        }

        private static readonly SemaphoreSlim CreateLock = new(1, 1);

        public async Task<FindMachineResponse> FindOrCreate(HttpContext httpContext, CancellationToken ct)
        {
            var httpHeadersMachine = WebRequestReader.GetMachine(httpContext);
            var matchBy = Program.ApplicationSettings.MatchMachinesBy;
            var m = await FindCheckInAsync(_context.Machines, httpHeadersMachine, matchBy, ct);

            if (m is null)
            {
                // Two check-ins from a new machine at once (its clientid request and its socket's SendId) both
                // found nothing and both created a record. Look again under a lock before creating.
                // ponytail: one lock per process; several API instances on one database would need a unique index.
                await CreateLock.WaitAsync(ct);
                try
                {
                    m = await FindCheckInAsync(_context.Machines, httpHeadersMachine, matchBy, ct);
                    if (m is null) return await CreateFromCheckIn(httpHeadersMachine, ct);
                }
                finally
                {
                    CreateLock.Release();
                }
            }

            m.UpdateFromCheckIn(httpHeadersMachine);
            _context.Entry(m).State = EntityState.Modified;
            await _context.SaveChangesAsync(ct);

            return new FindMachineResponse { Machine = m };
        }

        private async Task<FindMachineResponse> CreateFromCheckIn(Machine m, CancellationToken ct)
        {
            // Check before saving: a request without the machine headers (a connectivity probe) used to leave an
            // empty record behind for every request.
            if (!m.IsValid())
            {
                return new FindMachineResponse { Error = "Invalid machine request" };
            }

            m.History.Add(new Machine.MachineHistoryItem { Type = Machine.MachineHistoryItem.HistoryType.Created });
            await CreateAsync(m, ct);

            m.History.Add(new Machine.MachineHistoryItem { Type = Machine.MachineHistoryItem.HistoryType.RequestedId });
            await UpdateAsync(m, ct);

            return new FindMachineResponse { Machine = m };
        }

        /// <summary>
        /// The machine a checking-in client is: the record with the id the client sent (ghosts-id), otherwise
        /// <see cref="FindMatchAsync"/>. Without the id lookup every check-in matched by name, so the client's own
        /// record lost to a duplicate of the same name.
        /// </summary>
        public static async Task<Machine> FindCheckInAsync(IQueryable<Machine> machines, Machine machine, string matchBy, CancellationToken ct)
        {
            if (machine.Id != Guid.Empty)
            {
                var byId = await machines.FirstOrDefaultAsync(o => o.Id == machine.Id, ct);
                if (byId != null) return byId;
            }

            return await FindMatchAsync(machines, machine, matchBy, ct);
        }

        /// <summary>
        /// The machine a checking-in client is, by exact value ignoring case: the setting's field, or with no setting
        /// the first of name, FQDN, host and resolved host that the client sent. An empty value matches nothing, and
        /// among duplicates the most recently reported wins.
        /// </summary>
        public static async Task<Machine> FindMatchAsync(IQueryable<Machine> machines, Machine machine, string matchBy, CancellationToken ct)
        {
            var fields = (matchBy ?? string.Empty).ToLowerInvariant() switch
            {
                "fqdn" => new[] { "fqdn" },
                "host" => ["host"],
                "resolvedhost" => ["resolvedhost"],
                "name" => ["name"],
                "" => ["name", "fqdn", "host", "resolvedhost"],
                _ => ["name"]
            };

            foreach (var field in fields)
            {
                var value = (field switch
                {
                    "fqdn" => machine.FQDN,
                    "host" => machine.Host,
                    "resolvedhost" => machine.ResolvedHost,
                    _ => machine.Name
                })?.Trim().ToLower();
                if (string.IsNullOrEmpty(value)) continue;

                var query = field switch
                {
                    "fqdn" => machines.Where(o => o.FQDN.ToLower() == value),
                    "host" => machines.Where(o => o.Host.ToLower() == value),
                    "resolvedhost" => machines.Where(o => o.ResolvedHost.ToLower() == value),
                    _ => machines.Where(o => o.Name.ToLower() == value)
                };
                var match = await query.OrderByDescending(o => o.LastReportedUtc).FirstOrDefaultAsync(ct);
                if (match != null) return match;
            }

            return null;
        }

        public List<MachineListItem> GetList()
        {
            var items = new List<MachineListItem>();
            var q = from r in _context.Machines
                    select new
                    {
                        r.Id,
                        r.Name
                    };
            foreach (var item in q)
                items.Add(new MachineListItem
                {
                    Id = item.Id,
                    Name = item.Name
                });
            return items;
        }

        public async Task<Machine> GetByIdAsync(Guid id, CancellationToken ct)
        {
            var machine = await _context.Machines.FirstOrDefaultAsync(o => o.Id == id, ct);

            if (machine == null)
                return null;

            machine.AddHistoryMachine(await _context.HistoryMachine.Where(o => o.MachineId == machine.Id)
                .OrderByDescending(o => o.CreatedUtc).Take(_lookBack).ToListAsync(ct));
            machine.AddHistoryHealth(await _context.HistoryHealth.Where(o => o.MachineId == machine.Id)
                .OrderByDescending(o => o.CreatedUtc).Take(_lookBack).ToListAsync(ct));
            machine.AddHistoryTimeline(await _context.HistoryTimeline.Where(o => o.MachineId == machine.Id)
                .OrderByDescending(o => o.CreatedUtc).Take(_lookBack).ToListAsync(ct));

            return machine;
        }

        public async Task<Guid> CreateAsync(Machine model, CancellationToken ct)
        {
            model.StatusUp = Machine.UpDownStatus.Up;
            model.LastReportedUtc = DateTime.UtcNow;

            var ex = await _context.Machines.FirstOrDefaultAsync(o => o.Id == model.Id, ct);
            if (ex != null) return ex.Id;

            if (model.Id == Guid.Empty)
                model.Id = Guid.NewGuid();
            _context.Machines.Add(model);

            var operation = await _context.SaveChangesAsync(ct);
            if (operation < 1)
            {
                _log.Error($"Count not create machine: {operation}");
                throw new InvalidOperationException("Could not create Machine");
            }

            await AddToGroups(model, ct);

            if (Program.ApplicationSettings.NpcPerMachine)
            {
                try
                {
                    await npcService.CreateForMachine(model);
                }
                catch (Exception e)
                {
                    _log.Error(e, $"Could not create an NPC for machine {model.Id}");
                }
            }

            return model.Id;
        }

        public async Task<Machine> UpdateAsync(Machine model, CancellationToken ct)
        {
            model.StatusUp = Machine.UpDownStatus.Up;
            model.LastReportedUtc = DateTime.UtcNow;

            var ex = await _context.Machines.FirstOrDefaultAsync(o => o.Id == model.Id, ct);
            if (ex == null)
            {
                _log.Error("Machine not found");
                throw new InvalidOperationException("Machine not found");
            }

            _context.Entry(model).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException e)
            {
                _log.Error($"MachineService Update save error {e}");
            }

            try
            {
                _context.Machines.Update(ex);

                var operation = await _context.SaveChangesAsync(ct);
                if (operation < 1)
                {
                    _log.Error($"Count not update machine: {operation}");
                    throw new InvalidOperationException($"Could not update Machine, operation was {operation}");
                }
            }
            catch (Exception e)
            {
                _log.Error(e, "MachineService Update machine error");
            }

            await AddToGroups(model, ct);

            return model;
        }

        public async Task<Guid> DeleteAsync(Guid id, CancellationToken ct)
        {
            var a = await _context.Machines.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (a == null)
            {
                _log.Error($"Machine not found: {id}");
                throw new InvalidOperationException("Machine not found");
            }

            a.Status = StatusType.Deleted;
            _context.Machines.Update(a); // force tracking

            var operation = await _context.SaveChangesAsync(ct);
            if (operation >= 1) return id;

            _log.Error($"Could not delete machine: {operation}");
            throw new InvalidOperationException("Could not delete Machine");
        }

        public async Task<List<HistoryHealth>> GetMachineHistoryHealth(Guid id, CancellationToken ct)
        {
            return await _context.HistoryHealth
                .Where(o => o.MachineId == id)
                .OrderByDescending(o => o.CreatedUtc)
                .ToListAsync(ct);
        }

        public async Task<List<Machine.MachineHistoryItem>> GetMachineHistory(Guid id, CancellationToken ct)
        {
            return await _context.HistoryMachine.Where(o => o.MachineId == id).ToListAsync(ct);
        }

        public async Task<List<HistoryTimeline>> GetActivity(Guid id, int skip, int take, CancellationToken ct)
        {
            if (take < 1)
                take = 25;

            var machine = await _context.Machines.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (machine == null)
            {
                _log.Error($"Machine not found: {id}");
                throw new InvalidOperationException("Machine not found");
            }

            try
            {
                return _context.HistoryTimeline.Where(o => o.MachineId == id).OrderByDescending(o => o.CreatedUtc).Skip(skip).Take(take).ToList();
            }
            catch (Exception e)
            {
                _log.Debug(e);
                throw;
            }
        }

        public async Task AddActivity(Guid id, HistoryTimeline historyTimeline, CancellationToken ct)
        {
            await _context.HistoryTimeline.AddAsync(historyTimeline, ct);

            await activityHubContext.Show(
                1,
                id.ToString(),
                "activity",
                new { action = historyTimeline.ToActivityPlainText(), reasoning = "", handler = "MachineUpdate" },
                null, ct);
        }

        public async Task<List<HandlerActivity>> GetHandlerActivity(int hours, int bucketMinutes, CancellationToken ct)
        {
            var since = DateTime.UtcNow.AddHours(-hours);
            var bucketTicks = TimeSpan.FromMinutes(bucketMinutes).Ticks;
            var minutes = await _context.HistoryTimeline
                .Where(o => o.CreatedUtc >= since && o.Handler != null)
                .Join(_context.Machines.Where(m => m.Status == StatusType.Active), o => o.MachineId, m => m.Id, (o, _) => o)
                .GroupBy(o => new { o.Handler, o.MachineId, o.CreatedUtc.Year, o.CreatedUtc.Month, o.CreatedUtc.Day, o.CreatedUtc.Hour, o.CreatedUtc.Minute })
                .Select(g => new { g.Key, Events = g.Count() })
                .ToListAsync(ct);

            return minutes
                .Select(m => new
                {
                    m.Key.Handler,
                    m.Key.MachineId,
                    m.Events,
                    StartUtc = BucketStart(new DateTime(m.Key.Year, m.Key.Month, m.Key.Day, m.Key.Hour, m.Key.Minute, 0, DateTimeKind.Utc), bucketTicks)
                })
                .GroupBy(m => m.Handler)
                .Select(handler => new HandlerActivity(
                    handler.Key,
                    handler.Sum(m => m.Events),
                    handler.Select(m => m.MachineId).Distinct().Count(),
                    handler.GroupBy(m => m.StartUtc).OrderBy(b => b.Key).Select(b => new HandlerActivityBucket(b.Key, b.Sum(m => m.Events))).ToList()))
                .OrderByDescending(h => h.Events)
                .ToList();
        }

        private static DateTime BucketStart(DateTime minute, long bucketTicks) =>
            new(minute.Ticks - (minute.Ticks - DateTime.UnixEpoch.Ticks) % bucketTicks, DateTimeKind.Utc);

        private async Task AddToGroups(Machine model, CancellationToken ct)
        {
            var groups = GroupNames.GetGroupNames(model).ToList();
            if (groups.Count == 0) return;

            // shortcut: an in-process lock, enough for the single API instance; several instances would need a unique index on Name
            await _groupLock.WaitAsync(ct);
            try
            {
                foreach (var group in groups)
                {
                    // Oldest first, so groups duplicated by earlier races keep collecting into one
                    var existingGroup = await _context.Groups.Include(o => o.GroupMachines)
                        .Where(o => o.Name == group).OrderBy(o => o.Id).FirstOrDefaultAsync(ct);

                    if (existingGroup != null)
                    {
                        if (existingGroup.GroupMachines.Any(o => o.MachineId.Equals(model.Id))) continue;
                        existingGroup.GroupMachines.Add(new GroupMachine { GroupId = existingGroup.Id, MachineId = model.Id });
                    }
                    else
                    {
                        var newGroup = new Group { Name = group, Status = StatusType.Active };
                        newGroup.GroupMachines.Add(new GroupMachine { MachineId = model.Id });
                        _context.Groups.Add(newGroup);
                    }

                    await _context.SaveChangesAsync(ct);
                }
            }
            finally
            {
                _groupLock.Release();
            }
        }
    }
}
