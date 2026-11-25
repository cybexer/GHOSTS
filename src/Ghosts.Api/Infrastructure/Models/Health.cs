// Copyright 2017 Carnegie Mellon University. All Rights Reserved. See LICENSE.md file for terms.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Ghosts.Api.Infrastructure.Models
{
    [Table("history_health")]
    public class HistoryHealth
    {
        public HistoryHealth()
        {
            CreatedUtc = DateTime.UtcNow;
        }

        public int Id { get; set; }

        [ForeignKey("MachineId")]
        public Guid MachineId { get; set; }

        public DateTime CreatedUtc { get; set; }

        public bool? Internet { get; set; }
        public bool? Permissions { get; set; }
        public long ExecutionTime { get; set; }
        public string Errors { get; set; }
        public string LoggedOnUsers { get; set; }
        public string Stats { get; set; }
    }

    /// <summary>
    /// A handler error reported by a client (ERROR| line), e.g. a wrong password or a missing file.
    /// Code is a Ghosts.Domain ErrorCode name.
    /// </summary>
    [Table("history_errors")]
    public class HistoryError
    {
        public int Id { get; set; }
        public Guid MachineId { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string Handler { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string Target { get; set; }
    }

    /// <summary>One machine's errors of one handler and code in the summary window, with the latest message.</summary>
    public record MachineErrorSummary(Guid MachineId, string Handler, string Code, string Message, string Target, int Count, DateTime LastSeenUtc);

    public record HandlerActivityBucket(DateTime StartUtc, int Events);

    public record HandlerActivity(string Handler, int Events, int Machines, List<HandlerActivityBucket> Buckets);

    [Table("history_timeline")]
    public class HistoryTimeline
    {
        public HistoryTimeline()
        {
            CreatedUtc = DateTime.UtcNow;
        }

        public int Id { get; set; }

        [ForeignKey("MachineId")]
        public Guid MachineId { get; set; }

        public DateTime CreatedUtc { get; set; }

        public string Handler { get; set; }
        public string Command { get; set; }
        public string CommandArg { get; set; }
        public string Result { get; set; }

        public string Tags { get; set; }

        public IEnumerable<string> GetTags()
        {
            if (string.IsNullOrEmpty(Tags)) return new List<string>();
            return Tags.ToLower().Split(",");
        }

        public void SetTags(string value)
        {
            if (value != null) Tags = string.Join(",", value.ToLower());
        }

        public string ToActivityPlainText()
        {
            var sb = new StringBuilder();
            try
            {
                switch (this.Handler)
                {
                    case "BrowserChrome":
                    case "BrowserFirefox":
                        sb.Append($"Used {this.Handler.Replace("Browser", "")} to visit {this.Command}\n");
                        break;
                    default:
                        sb.Append($"Used {this.Handler} to perform some activity\n");
                        break;
                }
            }
            catch
            {
                return "";
            }

            return sb.ToString();
        }
    }

    [Table("history_trackables")]
    public class HistoryTrackable
    {
        public HistoryTrackable()
        {
            CreatedUtc = DateTime.UtcNow;
        }

        public int Id { get; set; }

        [ForeignKey("MachineId")]
        public Guid MachineId { get; set; }

        [ForeignKey("TrackableId")]
        public Guid TrackableId { get; set; }

        public DateTime CreatedUtc { get; set; }

        public string Handler { get; set; }
        public string Command { get; set; }
        public string CommandArg { get; set; }
        public string Result { get; set; }
    }
}
