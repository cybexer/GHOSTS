// Copyright 2017 Carnegie Mellon University. All Rights Reserved. See LICENSE.md file for terms.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Ghosts.Domain.Messages.MesssagesForServer
{
    /// <summary>
    /// Why a handler could not do its work. Sent to the server as an ERROR| line in clientupdates.log.
    /// </summary>
    public enum ErrorCode
    {
        Unknown,
        WrongPassword,
        AccountDisabled,
        AccountLocked,
        PasswordExpired,
        SessionTakenOver,
        ConnectionFailed,
        FileMissing,
        CredentialsMissing,
        DependencyMissing,
        HandlerCrashed
    }

    public class ResultError
    {
        public string Handler { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public ErrorCode Code { get; set; }

        public string Message { get; set; }

        /// <summary>What the error is about: an RDP/SSH host, a file path, a credential key.</summary>
        public string Target { get; set; }
    }
}
