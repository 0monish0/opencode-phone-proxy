using System.Diagnostics;
using System.IO.Pipes;
using OpenCodePhoneProxy.Models;

namespace OpenCodePhoneProxy.Services;

/// <summary>
/// A live route-metric guard. When the app is not elevated, a small elevated
/// helper process keeps the original metrics and restores them on request (or
/// when the pipe closes because the tray app crashed). This keeps a normal
/// session to one UAC prompt.
/// </summary>
public sealed class RouteSession : IDisposable
{
    private readonly object _gate = new();
    private readonly NetworkRouteGuard? _directGuard;
    private readonly RouteSnapshot _snapshot;
    private readonly NamedPipeServerStream? _pipe;
    private readonly Process? _helper;
    private readonly Logger _log;
    private readonly byte[] _buffer = new byte[1];

    private bool _finished;
    private bool _resourcesClosed;
    private RouteChangeResult? _result;

    private RouteSession(
        NetworkRouteGuard? directGuard,
        RouteSnapshot snapshot,
        NamedPipeServerStream? pipe,
        Process? helper,
        Logger log)
    {
        _directGuard = directGuard;
        _snapshot = snapshot;
        _pipe = pipe;
        _helper = helper;
        _log = log;
    }

    internal static RouteSession CreateDirect(
        NetworkRouteGuard guard,
        RouteSnapshot snapshot,
        Logger log) => new(guard, snapshot, null, null, log);

    internal static RouteSession CreateHelper(
        RouteSnapshot snapshot,
        NamedPipeServerStream pipe,
        Process helper,
        Logger log) => new(null, snapshot, pipe, helper, log);

    public RouteSnapshot Snapshot => _snapshot;

    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return !_finished && (_directGuard is not null || _pipe?.IsConnected == true);
            }
        }
    }

    /// <summary>
    /// Restore the captured metrics. Safe to call more than once.
    /// </summary>
    public RouteChangeResult Restore()
    {
        lock (_gate)
        {
            if (_result is not null)
            {
                return _result.Value;
            }

            if (_finished)
            {
                return RouteChangeResult.Failed;
            }

            if (_directGuard is not null)
            {
                _result = _directGuard.RestoreDirect(_snapshot);
                _finished = true;
                return _result.Value;
            }

            try
            {
                _pipe!.WriteByte((byte)'R');
                _pipe.Flush();
                var status = ReadStatus(TimeSpan.FromSeconds(45));
                if (status == (byte)'D')
                {
                    _result = RouteChangeResult.Applied;
                    _log.Info("The elevated route helper restored the adapter metric.");
                }
                else
                {
                    _log.Warn("The elevated route helper did not confirm the restore.");
                    _result = RouteChangeResult.Failed;
                }
            }
            catch (Exception ex)
            {
                _log.Warn("Could not ask the elevated route helper to restore: " + ex.Message);
                _result = RouteChangeResult.Failed;
            }
            finally
            {
                _finished = true;
                CloseResources(waitForHelper: true);
            }

            return _result.Value;
        }
    }

    /// <summary>
    /// Leave the metric changed and stop the helper. The persisted snapshot is
    /// restored on the next app start or by the uninstaller.
    /// </summary>
    public void Keep()
    {
        lock (_gate)
        {
            if (_finished)
            {
                return;
            }

            if (_directGuard is not null)
            {
                _finished = true;
                return;
            }

            try
            {
                _pipe!.WriteByte((byte)'K');
                _pipe.Flush();
                // Wait for the helper's acknowledgement before closing the pipe.
                // If the pipe closed first, the helper would see EOF and restore.
                ReadStatus(TimeSpan.FromSeconds(10));
            }
            catch
            {
                // Closing the pipe below makes the helper restore instead. The
                // persisted snapshot still holds the correct baseline.
            }

            _finished = true;
            CloseResources(waitForHelper: true);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_finished)
            {
                if (_directGuard is not null)
                {
                    _result = _directGuard.RestoreDirect(_snapshot);
                }

                // For a helper session, closing the pipe makes the helper restore.
                _finished = true;
            }

            CloseResources(waitForHelper: true);
        }
    }

    private byte? ReadStatus(TimeSpan timeout)
    {
        try
        {
            var task = _pipe!.ReadAsync(_buffer, 0, 1);
            if (!task.Wait(timeout))
            {
                return null;
            }

            return task.Result == 1 ? _buffer[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private void CloseResources(bool waitForHelper)
    {
        if (_resourcesClosed)
        {
            return;
        }

        _resourcesClosed = true;

        try
        {
            _pipe?.Dispose();
        }
        catch
        {
            // Best effort.
        }

        if (_helper is null)
        {
            return;
        }

        try
        {
            if (waitForHelper && !_helper.HasExited)
            {
                _helper.WaitForExit(20_000);
            }

            if (!_helper.HasExited)
            {
                _helper.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The helper may already be gone.
        }
        finally
        {
            _helper.Dispose();
        }
    }
}
