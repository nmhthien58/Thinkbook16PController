namespace Tb16p.Core.Hardware;

public enum SmuStatus : uint
{
    Ok = 0x01,
    Failed = 0xFF,
    UnknownCommand = 0xFE,
    RejectedPrerequisite = 0xFD,
    RejectedBusy = 0xFC,
    Timeout = 0x100,
    AccessError = 0x101,
}

public readonly record struct SmuMailbox(string Name, uint MessageAddress, uint ResponseAddress, uint ArgumentAddress);

/// <summary>
/// AMD SMU mailbox protocol (same flow as RyzenAdj and UXTU): wait idle, clear the
/// response register, write six arguments, write the message id, poll for a response.
/// </summary>
public sealed class Smu(ISmuRegisterAccess access)
{
    private const int ArgumentCount = 6;
    private const int PollLimit = 8192;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMilliseconds(500);

    public SmuStatus Send(SmuMailbox mailbox, uint message, uint argument0) =>
        Send(mailbox, message, [argument0, 0, 0, 0, 0, 0]);

    public SmuStatus Send(SmuMailbox mailbox, uint message, uint[] arguments)
    {
        if (!access.TryEnter(LockTimeout))
            return SmuStatus.RejectedBusy;
        try
        {
            if (!WaitForResponse(mailbox, out _))
                return SmuStatus.Timeout;

            if (!access.TryWrite(mailbox.ResponseAddress, 0))
                return SmuStatus.AccessError;

            for (var i = 0; i < ArgumentCount; i++)
            {
                var value = i < arguments.Length ? arguments[i] : 0u;
                if (!access.TryWrite(mailbox.ArgumentAddress + (uint)(i * 4), value))
                    return SmuStatus.AccessError;
            }

            if (!access.TryWrite(mailbox.MessageAddress, message))
                return SmuStatus.AccessError;

            if (!WaitForResponse(mailbox, out var response))
                return SmuStatus.Timeout;

            var status = (SmuStatus)response;
            if (status == SmuStatus.Ok)
            {
                for (var i = 0; i < Math.Min(arguments.Length, ArgumentCount); i++)
                {
                    if (!access.TryRead(mailbox.ArgumentAddress + (uint)(i * 4), out arguments[i]))
                        return SmuStatus.AccessError;
                }
            }

            return status;
        }
        finally
        {
            access.Exit();
        }
    }

    private bool WaitForResponse(SmuMailbox mailbox, out uint response)
    {
        response = 0;
        for (var i = 0; i < PollLimit; i++)
        {
            if (access.TryRead(mailbox.ResponseAddress, out response) && response != 0)
                return true;
        }
        return false;
    }
}
