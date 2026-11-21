using System.IO.Pipes;
using OpenCodePhoneProxy.Models;

namespace OpenCodePhoneProxy.Services;

/// <summary>
/// Elevated side of the route-metric session protocol. It applies the change,
/// reports the result, then waits on the pipe. A restore request, or the pipe
/// closing because the tray app crashed, puts the original metrics back.
/// </summary>
public static class RouteSessionProtocol
{
    public const byte Applied = (byte)'A';
    public const byte Failed = (byte)'F';
    public const byte RestoreRequested = (byte)'R';
    public const byte RestoreDone = (byte)'D';
    public const byte RestoreFailed = (byte)'X';
    public const byte KeepRequested = (byte)'K';

    public static int Run(
        RouteSnapshot snapshot,
        string pipeName,
        Func<RouteSnapshot, RouteChangeResult> apply,
        Func<RouteSnapshot, RouteChangeResult> restore,
        Logger log,
        int connectTimeoutMs = 60_000)
    {
        NamedPipeClientStream? pipe = null;
        var applied = false;
        var restored = false;

        try
        {
            pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            try
            {
                pipe.Connect(connectTimeoutMs);
            }
            catch (Exception ex)
            {
                log.Warn("The elevated route helper could not connect to the tray app: " + ex.Message);
                return 1;
            }

            applied = apply(snapshot) == RouteChangeResult.Applied;

            try
            {
                pipe.WriteByte(applied ? Applied : Failed);
                pipe.Flush();
            }
            catch
            {
                // The tray app disappeared before the handshake completed, so
                // nothing is left to ask for a restore. Undo the change now.
                if (applied)
                {
                    restored = restore(snapshot) == RouteChangeResult.Applied;
                }

                return 1;
            }

            if (!applied)
            {
                return 1;
            }

            while (true)
            {
                int command;
                try
                {
                    command = pipe.ReadByte();
                }
                catch
                {
                    command = -1;
                }

                if (command == KeepRequested)
                {
                    log.Info("The tray app exited while OpenCode is running; keeping the route metric.");
                    try
                    {
                        // Acknowledge before exiting so the parent can close the
                        // pipe without the helper treating it as a crash.
                        pipe.WriteByte(KeepRequested);
                        pipe.Flush();
                    }
                    catch
                    {
                        // The tray app is gone; keeping the metric is still correct.
                    }

                    return 0;
                }

                if (command == RestoreRequested || command == -1)
                {
                    restored = restore(snapshot) == RouteChangeResult.Applied;
                    log.Info(restored
                        ? "The elevated route helper restored the adapter metric."
                        : "The elevated route helper could not restore the adapter metric.");

                    if (command == RestoreRequested)
                    {
                        try
                        {
                            pipe.WriteByte(restored ? RestoreDone : RestoreFailed);
                            pipe.Flush();
                        }
                        catch
                        {
                            // The tray app is gone; the metrics are already handled.
                        }
                    }

                    return restored ? 0 : 1;
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                if (applied && !restored)
                {
                    restore(snapshot);
                }

                log.Error("Elevated route session failed: " + ex.Message);
            }
            catch
            {
                // Never let cleanup crash the helper.
            }

            return 1;
        }
        finally
        {
            try
            {
                pipe?.Dispose();
            }
            catch
            {
                // Best effort.
            }
        }
    }
}
