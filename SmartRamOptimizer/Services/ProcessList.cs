namespace SmartRamOptimizer.Services;

/// <summary>One running process. Only valid while the <see cref="ProcessSnapshot"/> it came from is alive.</summary>
internal readonly unsafe ref struct ProcessRow
{
    private readonly NativeMethods.SystemProcessInfo* _info;

    internal ProcessRow(NativeMethods.SystemProcessInfo* info)
    {
        _info = info;
    }

    /// <summary>The name Windows shows for the process: its exe name without ".exe".</summary>
    public ReadOnlySpan<char> Name
    {
        get
        {
            var name = new ReadOnlySpan<char>(_info->ImageName.Buffer, _info->ImageName.Length / sizeof(char));
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        }
    }

    public int ProcessId => (int)_info->UniqueProcessId;

    public int SessionId => (int)_info->SessionId;

    public long WorkingSetBytes => (long)_info->WorkingSetSize;

    public long PrivateBytes => (long)_info->PrivatePageCount;

    /// <summary>The figure Task Manager shows in its Memory column.</summary>
    public long PrivateWorkingSetBytes => _info->PrivateWorkingSetSize;
}

/// <summary>
/// Every running process with its memory counters, read with a single system call.
/// Nothing is allocated per process and the buffer is handed back to Windows on dispose,
/// so watching other apps' memory costs this app almost none of its own.
/// </summary>
internal unsafe ref struct ProcessSnapshot
{
    private const uint SlackBytes = 64 * 1024;

    // How much the last call needed; processes come and go, so the next one starts a little above it.
    private static uint s_lastSizeBytes = 512 * 1024;

    private byte* _buffer;

    private ProcessSnapshot(byte* buffer)
    {
        _buffer = buffer;
    }

    /// <summary>An empty snapshot is returned if Windows refuses the query.</summary>
    public static ProcessSnapshot Capture()
    {
        var capacity = s_lastSizeBytes + SlackBytes;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var buffer = (byte*)NativeMethods.VirtualAlloc(
                null,
                capacity,
                NativeMethods.MemoryCommitAndReserve,
                NativeMethods.PageReadWrite);
            if (buffer is null)
            {
                break;
            }

            var status = NativeMethods.NtQuerySystemInformation(
                NativeMethods.SystemProcessInformation,
                buffer,
                capacity,
                out var neededBytes);
            if (status == 0)
            {
                s_lastSizeBytes = neededBytes;
                return new ProcessSnapshot(buffer);
            }

            NativeMethods.VirtualFree(buffer, 0, NativeMethods.MemoryRelease);
            if (status != NativeMethods.StatusInfoLengthMismatch)
            {
                break;
            }

            capacity = Math.Max(neededBytes, capacity) + SlackBytes;
        }

        return default;
    }

    public readonly Enumerator GetEnumerator()
    {
        return new Enumerator(_buffer);
    }

    public void Dispose()
    {
        if (_buffer is not null)
        {
            NativeMethods.VirtualFree(_buffer, 0, NativeMethods.MemoryRelease);
            _buffer = null;
        }
    }

    public ref struct Enumerator
    {
        private byte* _next;
        private NativeMethods.SystemProcessInfo* _current;

        internal Enumerator(byte* first)
        {
            _next = first;
            _current = null;
        }

        public readonly ProcessRow Current => new(_current);

        public bool MoveNext()
        {
            if (_next is null)
            {
                return false;
            }

            _current = (NativeMethods.SystemProcessInfo*)_next;
            _next = _current->NextEntryOffset == 0 ? null : _next + _current->NextEntryOffset;
            return true;
        }
    }
}
