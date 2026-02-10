using System;

namespace VAL.Host.Commands
{
    public enum HostCommandExecutionStatus
    {
        Success = 0,
        Blocked = 1,
        Error = 2
    }

    public readonly record struct HostCommandExecutionResult(
        HostCommandExecutionStatus Status,
        string CommandName,
        string Reason,
        bool IsDockInvocation,
        string? DiagnosticDetail = null,
        Exception? Exception = null)
    {
        public bool IsSuccess => Status == HostCommandExecutionStatus.Success;
        public bool IsBlocked => Status == HostCommandExecutionStatus.Blocked;
        public bool IsError => Status == HostCommandExecutionStatus.Error;

        public static HostCommandExecutionResult Success(string commandName, bool isDockInvocation)
            => new(HostCommandExecutionStatus.Success, commandName, "Command executed.", isDockInvocation);

        public static HostCommandExecutionResult Blocked(string commandName, string reason, bool isDockInvocation, string? diagnosticDetail = null)
            => new(HostCommandExecutionStatus.Blocked, commandName, reason, isDockInvocation, diagnosticDetail);

        public static HostCommandExecutionResult Error(string commandName, string reason, bool isDockInvocation, Exception? exception = null, string? diagnosticDetail = null)
            => new(HostCommandExecutionStatus.Error, commandName, reason, isDockInvocation, diagnosticDetail, exception);
    }
}
