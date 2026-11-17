namespace Tb16p.Core.Hardware;

/// <summary>
/// Raw access to the SMU register space. Implemented by WinRing0 today; a PawnIO
/// implementation can replace it without touching the SMU mailbox logic.
/// Callers must hold <see cref="TryEnter"/> for a whole mailbox transaction.
/// </summary>
public interface ISmuRegisterAccess : IDisposable
{
    bool TryEnter(TimeSpan timeout);
    void Exit();
    bool TryRead(uint smuAddress, out uint value);
    bool TryWrite(uint smuAddress, uint value);
}
